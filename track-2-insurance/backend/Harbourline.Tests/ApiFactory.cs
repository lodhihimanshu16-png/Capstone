// =====================================================================================================
// Test fixtures for Harbourline.Tests (read this before adding a test)
//
// ApiFactory (in-memory)
//   - Environment "Testing": Program.cs does not register MySQL, this factory registers the EF in-memory provider.
//   - Every ApiFactory instance has its own in-memory database (own name and own InMemoryDatabaseRoot), so a test
//     that creates `using var f = new ApiFactory();` shares nothing with any other test. Create one per test.
//   - TimeProvider is replaced by FakeClock (property Clock), fixed at 2026-10-20T10:00:00Z. Change Clock.Now to
//     move time. No test may read the real clock.
//   - Seed (see Ids): regions NTH 1, MID 2, LSE 3, SWW 4; grades G1..G4 with limits 2000/10000/50000/250000;
//     users Admin 1, Manager "Priya Raman" 2, ReadOnly 15 and handler users 3..12 (handler id == user id);
//     9 policies (one in force, one starting after the loss date, one Cancelled, one expired, five used by the
//     bordereau sample); 2 products; 7 policyholders. No claims are seeded.
//   - f.AddClaim(spec => ...) inserts a claim in ANY status with chosen assessor, handler, amounts, history rows
//     and the date it entered its status, so each test builds exactly its Given. Claim ids start at 100.
//   - In-memory cannot check SQL types, lengths, unique indexes, row versions or query counts. Those criteria
//     (AC-32, AC-33, AC-41, AC-48) live in MySqlTests and use MySqlApiFactory.
//
// MySqlApiFactory (real MySQL, see MySqlApiFactory.cs)
//   - Environment variable HARBOURLINE_TEST_MYSQL_ADMIN holds an admin connection string for a MySQL server
//     (server, port, admin user and that user's secret). Set it in your own shell; never write the secret in a
//     file in this repo. A throw-away database harbourline_test_<guid> is created for each factory and dropped
//     again when the factory is disposed.
//   - Environment variable REQUIRE_MYSQL_TESTS=1: a missing HARBOURLINE_TEST_MYSQL_ADMIN then makes the MySQL
//     tests FAIL. When it is not set, the MySQL tests are SKIPPED with "HARBOURLINE_TEST_MYSQL_ADMIN is not set".
// =====================================================================================================

using Harbourline.Api.Data;
using Harbourline.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Harbourline.Tests;

/// <summary>A clock the test controls. UTC everywhere so no test depends on the machine time zone.</summary>
public sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = Fixed.NowOffset;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

/// <summary>Fixed dates used by the tests. The default clock is 2026-10-20T10:00:00Z.</summary>
public static class Fixed
{
    public static readonly DateTimeOffset NowOffset = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);
    public static readonly DateTime Now = new(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Today = new(2026, 10, 20);

    public static DateTime Utc(int year, int month, int day, int hour = 9, int minute = 0) =>
        new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}

/// <summary>Ids of the seeded users and handlers. A handler's id equals its user id.</summary>
public static class Ids
{
    public const int Admin = 1;
    public const int Priya = 2;          // Head of Claims, role Manager, no limit
    public const int Anna = 3;           // G1 Claims Assistant, NTH
    public const int Ben = 4;            // G2 Claims Handler, NTH
    public const int Carl = 5;           // G2 Claims Handler, NTH
    public const int Dina = 6;           // G3 Senior Handler, MID
    public const int Eve = 7;            // G4 Technical Lead, MID
    public const int Finn = 8;           // G2, LSE, MaxOpenClaims = 1
    public const int Gus = 9;            // G1, SWW
    public const int HalInactiveHandler = 10;  // user active, handler record inactive
    public const int IvanInactiveUser = 11;    // handler record active, user inactive
    public const int Ivy = 12;           // G2 Claims Handler, NTH
    public const int ReadOnly = 15;

    public const int RegionNth = 1, RegionMid = 2, RegionLse = 3, RegionSww = 4;

    public const int PolicyInForce = 1;       // 2026-01-01 .. 2026-12-31, Active
    public const int PolicyStartsLater = 2;   // 2026-07-01 .. 2027-06-30
    public const int PolicyCancelled = 3;     // 2026-01-01 .. 2026-12-31, Cancelled
    public const int PolicyExpired = 4;       // 2025-01-01 .. 2025-12-31

    public const string SecretHash = "hash-secret-value-123";
}

public class HistorySpec
{
    public string? From { get; set; }
    public string To { get; set; } = "New";
    public int By { get; set; } = Ids.Ben;
    public DateTime At { get; set; } = Fixed.Utc(2026, 10, 20, 9);
    public string? Reason { get; set; }
    public decimal? Amount { get; set; }
}

/// <summary>Describes one claim to insert. Defaults give a New claim on the in-force policy.</summary>
public class ClaimSpec
{
    public string Status { get; set; } = "New";
    public int PolicyId { get; set; } = Ids.PolicyInForce;
    public int? HandlerId { get; set; }
    public int RegionId { get; set; } = Ids.RegionNth;
    public DateOnly LossDate { get; set; } = new(2026, 5, 1);
    public DateOnly ReportedDate { get; set; } = new(2026, 5, 2);
    public string Description { get; set; } = "Test claim";
    public decimal Estimated { get; set; } = 1000m;
    public decimal? Approved { get; set; }
    public decimal? Paid { get; set; }
    public decimal? Settlement { get; set; }
    public int? AssessedBy { get; set; }
    public int? ApprovedBy { get; set; }
    public int ReopenedCount { get; set; }
    public DateOnly? ClosedDate { get; set; }

    /// <summary>When the claim entered its current status. Default: 2026-10-20T09:00Z (the same day as the clock).</summary>
    public DateTime? EnteredStatusAt { get; set; }

    /// <summary>
    /// Explicit history rows. When null, one row (ToStatus = Status, at EnteredStatusAt) is added so the claim has
    /// a history. When set (even empty) exactly these rows are added.
    /// </summary>
    public List<HistorySpec>? History { get; set; }
}

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "harbourline-" + Guid.NewGuid();
    private readonly InMemoryDatabaseRoot _root = new();
    private int _lastClaimId = 99;

    public FakeClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // The in-memory provider ignores transactions; without the first warning below it throws when the code under test opens one.
            // Every test gets its own factory and its own database root, so EF builds one internal service provider per test and
            // would throw after 20 of them. That is expected here (see the second warning), not a leak.
            services.AddDbContext<HarbourlineDbContext>(o => o
                .UseInMemoryDatabase(_dbName, _root)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning, CoreEventId.ManyServiceProvidersCreatedWarning)));

            var options = new DbContextOptionsBuilder<HarbourlineDbContext>().UseInMemoryDatabase(_dbName, _root)
                .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning)).Options;
            using var db = new HarbourlineDbContext(options);
            Seed(db);
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    /// <summary>Runs code against the factory's database (a new scope and context each call).</summary>
    public void Db(Action<HarbourlineDbContext> action)
    {
        using var scope = Services.CreateScope();
        action(scope.ServiceProvider.GetRequiredService<HarbourlineDbContext>());
    }

    /// <summary>Inserts a claim (and its history) in any state. Returns the claim id.</summary>
    public int AddClaim(Action<ClaimSpec>? configure = null)
    {
        var s = new ClaimSpec();
        configure?.Invoke(s);

        var id = Interlocked.Increment(ref _lastClaimId);
        var entered = s.EnteredStatusAt ?? Fixed.Utc(2026, 10, 20, 9);

        var claim = new Claim
        {
            Id = id,
            ClaimNumber = $"HLC-26-{id:D5}",
            PolicyId = s.PolicyId,
            HandlerId = s.HandlerId,
            ClaimType = "Collision",
            Status = s.Status,
            LossDate = s.LossDate,
            ReportedDate = s.ReportedDate,
            Description = s.Description,
            EstimatedAmount = s.Estimated,
            ApprovedAmount = s.Approved,
            PaidAmount = s.Paid,
            SettlementAmount = s.Settlement,
            AssessedByUserId = s.AssessedBy,
            ApprovedByUserId = s.ApprovedBy,
            Excess = 250m,
            RegionId = s.RegionId,
            ReopenedCount = s.ReopenedCount,
            ClosedDate = s.ClosedDate,
            CreatedAt = entered.AddDays(-1),
            UpdatedAt = entered
        };
        var history = s.History ?? new List<HistorySpec> { new() { From = null, To = s.Status, At = entered } };

        Db(db =>
        {
            db.Claims.Add(claim);
            foreach (var h in history)
                db.ClaimStatusHistory.Add(new ClaimStatusHistory
                {
                    ClaimId = id, FromStatus = h.From, ToStatus = h.To, ChangedByUserId = h.By, ChangedAt = h.At,
                    Reason = h.Reason, Amount = h.Amount
                });
            db.SaveChanges();
        });
        return id;
    }

    private static void Seed(HarbourlineDbContext db)
    {
        db.Regions.AddRange(
            new Region { Id = 1, Code = "NTH", Name = "Scotland & North" },
            new Region { Id = 2, Code = "MID", Name = "Midlands" },
            new Region { Id = 3, Code = "LSE", Name = "London & South East" },
            new Region { Id = 4, Code = "SWW", Name = "South West & Wales" });

        db.Products.AddRange(
            new Product { Id = 1, Code = "HOME-STD", Name = "Home Standard", Line = "Home", BasePremium = 520, DefaultExcess = 250, Active = true },
            new Product { Id = 2, Code = "MOT-COMP", Name = "Motor Comprehensive", Line = "Motor", BasePremium = 980, DefaultExcess = 300, Active = true });

        db.HandlerGrades.AddRange(
            new HandlerGrade { Id = 1, Code = "G1", Name = "Claims Assistant", ApprovalLimit = 2000m },
            new HandlerGrade { Id = 2, Code = "G2", Name = "Claims Handler", ApprovalLimit = 10000m },
            new HandlerGrade { Id = 3, Code = "G3", Name = "Senior Handler", ApprovalLimit = 50000m },
            new HandlerGrade { Id = 4, Code = "G4", Name = "Technical Lead", ApprovalLimit = 250000m });

        var created = new DateTime(2024, 1, 2, 9, 0, 0, DateTimeKind.Utc);
        AppUser U(int id, string email, string role, string name, bool active = true) => new()
        {
            Id = id, Email = email, PasswordHash = Ids.SecretHash, Role = role, DisplayName = name, Active = active, CreatedAt = created
        };
        db.Users.AddRange(
            U(1, "admin@harbourline.example", "Admin", "Site Administrator"),
            U(2, "priya.raman@harbourline.example", "Manager", "Priya Raman"),
            U(3, "anna@harbourline.example", "Handler", "Anna Assistant"),
            U(4, "ben@harbourline.example", "Handler", "Ben Handler"),
            U(5, "carl@harbourline.example", "Handler", "Carl Handler"),
            U(6, "dina@harbourline.example", "Handler", "Dina Senior"),
            U(7, "eve@harbourline.example", "Handler", "Eve Technical"),
            U(8, "finn@harbourline.example", "Handler", "Finn Capped"),
            U(9, "gus@harbourline.example", "Handler", "Gus Assistant"),
            U(10, "hal@harbourline.example", "Handler", "Hal Inactive Handler"),
            U(11, "ivan@harbourline.example", "Handler", "Ivan Inactive User", active: false),
            U(12, "ivy@harbourline.example", "Handler", "Ivy Handler"),
            U(15, "audit.readonly@harbourline.example", "ReadOnly", "Internal Audit"));

        Handler H(int id, string name, int grade, int region, int max, bool active = true) => new()
        {
            Id = id, UserId = id, Name = name, Email = name.Split(' ')[0].ToLowerInvariant() + "@harbourline.example",
            GradeId = grade, RegionId = region, MaxOpenClaims = max, Active = active
        };
        db.Handlers.AddRange(
            H(3, "Anna Assistant", 1, 1, 8),
            H(4, "Ben Handler", 2, 1, 8),
            H(5, "Carl Handler", 2, 1, 8),
            H(6, "Dina Senior", 3, 2, 8),
            H(7, "Eve Technical", 4, 2, 8),
            H(8, "Finn Capped", 2, 3, 1),
            H(9, "Gus Assistant", 1, 4, 8),
            H(10, "Hal Inactive Handler", 2, 1, 8, active: false),
            H(11, "Ivan Inactive User", 2, 1, 8),
            H(12, "Ivy Handler", 2, 1, 8));

        for (var i = 1; i <= 7; i++)
            db.Policyholders.Add(new Policyholder
            {
                Id = i, FullName = "Test Holder " + i, Email = $"holder{i}@example.com", Phone = "07700 900000", AddressLine = "1 High Street",
                City = "Leeds", Postcode = "LS1 1AA", DateOfBirth = new DateOnly(1980, 1, 1), RegionId = 1, CreatedAt = new DateTime(2025, 1, 1)
            });

        Policy P(int id, string number, int product, DateOnly start, DateOnly end, string status = "Active") => new()
        {
            Id = id, PolicyNumber = number, PolicyholderId = ((id - 1) % 7) + 1, ProductId = product, StartDate = start, EndDate = end,
            AnnualPremium = 500m, SumInsured = 10000m, Status = status, RegionId = 1, NoClaimsYears = 0, CreatedAt = new DateTime(2025, 1, 1)
        };
        var yearStart = new DateOnly(2026, 1, 1);
        var yearEnd = new DateOnly(2026, 12, 31);
        db.Policies.AddRange(
            P(1, "HLH-100001", 1, yearStart, yearEnd),
            P(2, "HLH-100002", 1, new DateOnly(2026, 7, 1), new DateOnly(2027, 6, 30)),
            P(3, "HLH-100003", 1, yearStart, yearEnd, "Cancelled"),
            P(4, "HLH-100004", 1, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31)),
            // Policies that the rows of docs/samples/bordereau-sample.csv refer to.
            P(5, "HLM-100045", 2, yearStart, yearEnd),
            P(6, "HLH-100012", 1, yearStart, yearEnd),
            P(7, "HLM-100101", 2, yearStart, yearEnd),
            P(8, "HLH-100005", 1, yearStart, yearEnd),
            P(9, "HLM-100009", 2, new DateOnly(2026, 9, 1), new DateOnly(2027, 8, 31)));

        db.SaveChanges();
    }
}
