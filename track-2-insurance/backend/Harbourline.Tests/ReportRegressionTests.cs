using Harbourline.Api.Domain;

namespace Harbourline.Tests;

/// <summary>
/// Regression tests for ticket HLI-87 (docs/bug-HLI-87.md): the loss-ratio report overstated the paid amount and the
/// settlement count because it counted one row per "Settled" history entry, so a claim that was settled, reopened
/// and settled again was counted (and its paid amount added) once for each Settled entry.
/// </summary>
public class ReportRegressionTests
{
    private const int MotorCompProductId = 2; // MOT-COMP in the seed
    private const int PolicyId = 900;

    private static void AddMotorPolicy(ApiFactory f) => f.Db(db =>
    {
        db.Policies.Add(new Policy
        {
            Id = PolicyId, PolicyNumber = "HLM-REG-900", PolicyholderId = 1, ProductId = MotorCompProductId,
            StartDate = new DateOnly(2026, 3, 1), EndDate = new DateOnly(2027, 2, 28), AnnualPremium = 1000m, SumInsured = 20000m,
            Status = "Active", RegionId = Ids.RegionNth, NoClaimsYears = 0, CreatedAt = Fixed.Utc(2026, 3, 1)
        });
        db.SaveChanges();
    });

    private static HistorySpec Settled(int year, int month, int day) =>
        new() { From = "Approved", To = "Settled", By = Ids.Ben, At = Fixed.Utc(year, month, day) };

    [Fact]
    public async Task HLI_87_ClaimSettledTwiceIsCountedOnceInLossRatio()
    {
        using var f = new ApiFactory();
        AddMotorPolicy(f);

        // A: settled once, paid 100.00.
        f.AddClaim(c => { c.PolicyId = PolicyId; c.Status = "Settled"; c.Approved = 400m; c.Paid = 100m; c.Settlement = 100m;
            c.History = new() { Settled(2026, 3, 10) }; });
        // B: settled, reopened, settled again. Two Settled history rows, paid 400.00 (the total).
        f.AddClaim(c => { c.PolicyId = PolicyId; c.Status = "Settled"; c.Approved = 600m; c.Paid = 400m; c.Settlement = 400m; c.ReopenedCount = 1;
            c.History = new()
            {
                Settled(2026, 4, 1),
                new HistorySpec { From = "Settled", To = "Reopened", By = Ids.Ben, At = Fixed.Utc(2026, 5, 1), Reason = "New evidence" },
                Settled(2026, 6, 1)
            }; });
        // C: control. Its only Settled entry is before the period, so it must not be counted at all.
        f.AddClaim(c => { c.PolicyId = PolicyId; c.Status = "Settled"; c.Approved = 999m; c.Paid = 999m; c.Settlement = 999m;
            c.History = new() { Settled(2025, 6, 1) }; });

        var report = await f.CreateClient().GetJson("/api/reports/loss-ratio?from=2026-01-01&to=2026-12-31");
        var motor = report.EnumerateArray().Single(r => r.GetProperty("productCode").GetString() == "MOT-COMP");

        // Each claim once: 100.00 + 400.00 = 500.00 over 2 claims. The old query gave 900.00 over 3 settlements.
        Assert.Equal(2, motor.GetProperty("claimCount").GetInt32());
        Assert.Equal(500.00m, motor.GetProperty("paidClaims").GetDecimal());
    }

    [Fact]
    public async Task HLI_87_ClaimSettledOnceIsStillCountedOnce()
    {
        using var f = new ApiFactory();
        AddMotorPolicy(f);
        f.AddClaim(c => { c.PolicyId = PolicyId; c.Status = "Settled"; c.Approved = 400m; c.Paid = 250.50m; c.Settlement = 250.50m;
            c.History = new() { Settled(2026, 3, 10) }; });

        var report = await f.CreateClient().GetJson("/api/reports/loss-ratio?from=2026-01-01&to=2026-12-31");
        var motor = report.EnumerateArray().Single(r => r.GetProperty("productCode").GetString() == "MOT-COMP");

        Assert.Equal(1, motor.GetProperty("claimCount").GetInt32());
        Assert.Equal(250.50m, motor.GetProperty("paidClaims").GetDecimal());
    }
}
