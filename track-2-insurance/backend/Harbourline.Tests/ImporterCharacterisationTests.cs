using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Harbourline.Api.Data;
using Harbourline.Api.Domain;

namespace Harbourline.Tests;

/// <summary>
/// Characterisation tests for the bordereau importer (Task 6). They pin what the importer does TODAY, odd
/// behaviour included, so that a refactor can prove it changed nothing. A test here is not a statement that the
/// behaviour is right. Odd behaviour is marked ODD in the comments and is listed in docs/refactor-note.md.
/// Do not change an expected value to make a refactor pass: if one fails, the refactor changed behaviour.
/// </summary>
public class ImporterCharacterisationTests
{
    private const string Header = "ExternalRef,PolicyNumber,LossDate,ReportedDate,ClaimType,Description,EstimatedAmount,ContactEmail";

    // ---- helpers -------------------------------------------------------------------------------------------

    private static string Csv(params string[] rows) => Header + "\n" + string.Join("\n", rows) + "\n";

    /// <summary>One CSV row for the in-force policy HLH-100001 (loss 2026-05-01, estimate 500) with overrides.</summary>
    private static string Row(string extRef, string policy = "HLH-100001", string loss = "2026-05-01", string reported = "2026-05-02",
        string type = "Collision", string description = "Damaged", string amount = "500.00", string email = "") =>
        string.Join(",", extRef, policy, loss, reported, type, description, amount, email);

    private static async Task<JsonElement> Import(ApiFactory f, string csv, string fileName = "t.csv", string? source = "Broker X", int? userId = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(source ?? ""), "source");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/imports/bordereau") { Content = form };
        if (userId.HasValue) request.Headers.Add("X-User-Id", userId.Value.ToString());
        return await (await f.CreateClient().SendAsync(request)).AssertOk();
    }

    private static List<(int Row, string Message)> Errors(JsonElement result) =>
        result.GetProperty("errors").Rows().Select(e => (e.Int("row")!.Value, e.Str("message")!)).ToList();

    private static T Read<T>(ApiFactory f, Func<HarbourlineDbContext, T> read)
    {
        T value = default!;
        f.Db(db => value = read(db));
        return value;
    }

    private static Claim ClaimByRef(ApiFactory f, string extRef) => Read(f, db => db.Claims.Single(c => c.ExternalRef == extRef));

    private static string FindSample()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "docs", "samples", "bordereau-sample.csv");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("docs/samples/bordereau-sample.csv was not found above " + AppContext.BaseDirectory);
    }

    private static void Counts(JsonElement r, int total, int created, int updated, int rejected)
    {
        Assert.Equal(total, r.Int("totalRows"));
        Assert.Equal(created, r.Int("created"));
        Assert.Equal(updated, r.Int("updated"));
        Assert.Equal(rejected, r.Int("rejected"));
    }

    // ---- the sample file -----------------------------------------------------------------------------------

    [Fact]
    public async Task Characterise_SampleFile_CountsAndEveryRejectionMessage()
    {
        using var f = new ApiFactory();
        var csv = await File.ReadAllTextAsync(FindSample());

        var r = await Import(f, csv, "bordereau-sample.csv", "Test broker");

        Counts(r, total: 10, created: 4, updated: 0, rejected: 6);
        Assert.Equal(new List<(int, string)>
        {
            (5, "Policy HLH-999999 was not found."),
            (6, "LossDate '2026-13-45' is not a recognised date."),
            (7, "ReportedDate is before LossDate."),
            (8, "EstimatedAmount '-500' is not a positive amount."),
            (10, "Duplicate ExternalRef in file."),
            (11, "LossDate 2026-08-20 is outside the period of policy HLM-100009."),
        }, Errors(r));
    }

    [Fact]
    public async Task Characterise_CreatedClaims_AreNewWithNumbersHistoryAndBatch()
    {
        using var f = new ApiFactory();
        var r = await Import(f, Csv(Row("R1"), Row("R2")), "file-a.csv", "Broker X", userId: 4);

        var claims = Read(f, db => db.Claims.OrderBy(c => c.Id).ToList());
        Assert.Equal(new[] { "HLC-26-00001", "HLC-26-00002" }, claims.Select(c => c.ClaimNumber));
        Assert.All(claims, c => Assert.Equal("New", c.Status));
        Assert.Equal("R1", claims[0].ExternalRef);
        var history = Read(f, db => db.ClaimStatusHistory.Where(h => h.ClaimId == claims[0].Id).ToList());
        var h = Assert.Single(history);
        Assert.Equal((null, "New", 4, "Imported from Broker X"), (h.FromStatus, h.ToStatus, h.ChangedByUserId, h.Reason));

        var batch = Read(f, db => db.ImportBatches.Single());
        Assert.Equal(("file-a.csv", "Broker X", 2, 2, 0, 0, 4),
            (batch.FileName, batch.Source, batch.TotalRows, batch.CreatedRows, batch.UpdatedRows, batch.RejectedRows, batch.CreatedByUserId));
        Assert.Equal(batch.Id, r.Int("batchId"));
    }

    [Fact]
    public async Task Characterise_WithoutAnXUserIdHeader_TheBatchIsRecordedForUserOne()
    {
        using var f = new ApiFactory();
        await Import(f, Csv(Row("R1")));

        Assert.Equal(1, Read(f, db => db.ImportBatches.Single()).CreatedByUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Characterise_ODD_ABlankSource_IsRefusedBeforeTheImporterRuns_SoItsUnknownFallbackIsUnreachable(string source)
    {
        // The importer service and the controller both contain `string.IsNullOrWhiteSpace(source) ? "Unknown" : source`,
        // but the non-nullable `source` form field is required by model validation, so a blank one never gets that far.
        using var f = new ApiFactory();
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(Csv(Row("R1")))), "file", "t.csv" },
            { new StringContent(source), "source" },
        };

        var response = await f.CreateClient().PostAsync("/api/imports/bordereau", form);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("The source field is required.", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, Read(f, db => db.Claims.Count()));
        Assert.Equal(0, Read(f, db => db.ImportBatches.Count()));
    }

    // ---- ODD: re-import and updates ------------------------------------------------------------------------

    [Fact]
    public async Task Characterise_ODD_ReImportingUnchangedRows_CountsThemAsUpdated()
    {
        using var f = new ApiFactory();
        var csv = Csv(Row("R1"), Row("R2"));
        await Import(f, csv);

        var again = await Import(f, csv);

        Counts(again, total: 2, created: 0, updated: 2, rejected: 0); // nothing changed, yet both are "updated"
        Assert.Equal(2, Read(f, db => db.Claims.Count()));
    }

    [Fact]
    public async Task Characterise_ReImportWithNewAmountAndDescription_ChangesTheExistingClaim()
    {
        using var f = new ApiFactory();
        await Import(f, Csv(Row("R1", amount: "500.00", description: "First text")));

        var r = await Import(f, Csv(Row("R1", amount: "750.00", description: "Second text")));

        Counts(r, 1, 0, 1, 0);
        var c = ClaimByRef(f, "R1");
        Assert.Equal((750.00m, "Second text"), (c.EstimatedAmount, c.Description));
    }

    [Fact]
    public async Task Characterise_ReImportWithEmptyDescription_KeepsTheOldDescription()
    {
        using var f = new ApiFactory();
        await Import(f, Csv(Row("R1", description: "Original")), "one.csv");

        await Import(f, Csv(Row("R1", description: "")), "two.csv"); // empty becomes "Imported from bordereau two.csv"

        Assert.Equal("Original", ClaimByRef(f, "R1").Description);
    }

    [Fact]
    public async Task Characterise_ODD_ImportCanStillChangeTheEstimateOfAnApprovedClaim()
    {
        using var f = new ApiFactory();
        await Import(f, Csv(Row("R1", amount: "500.00")));
        f.Db(db => { var c = db.Claims.Single(); c.Status = "Approved"; c.ApprovedAmount = 500m; db.SaveChanges(); });

        var r = await Import(f, Csv(Row("R1", amount: "900.00")));

        Counts(r, 1, 0, 1, 0);
        Assert.Equal(("Approved", 900.00m), (ClaimByRef(f, "R1").Status, ClaimByRef(f, "R1").EstimatedAmount));
    }

    [Theory]
    [InlineData("Closed")]
    [InlineData("Declined")]
    public async Task Characterise_ReImportOfAClosedOrDeclinedClaim_IsRejected(string status)
    {
        using var f = new ApiFactory();
        await Import(f, Csv(Row("R1")));
        f.Db(db => { db.Claims.Single().Status = status; db.SaveChanges(); });

        var r = await Import(f, Csv(Row("R1", amount: "900.00")));

        Counts(r, 1, 0, 0, 1);
        Assert.Equal(new List<(int, string)> { (2, $"Claim HLC-26-00001 is {status} and cannot be updated by import.") }, Errors(r));
        Assert.Equal(500.00m, ClaimByRef(f, "R1").EstimatedAmount);
    }

    [Fact]
    public async Task Characterise_ExternalRefOnAnotherPolicy_IsRejected()
    {
        using var f = new ApiFactory();
        await Import(f, Csv(Row("R1", policy: "HLH-100001")));

        var r = await Import(f, Csv(Row("R1", policy: "HLM-100045")));

        Counts(r, 1, 0, 0, 1);
        Assert.Equal("ExternalRef R1 already belongs to a different policy.", Errors(r).Single().Message);
    }

    // ---- ODD: the policyholder email is overwritten before the row is checked fully --------------------------

    [Fact]
    public async Task Characterise_ContactEmail_OverwritesThePolicyholderEmail()
    {
        using var f = new ApiFactory();

        await Import(f, Csv(Row("R1", policy: "HLH-100001", email: "new.person@example.com")));

        // Policy 1 belongs to policyholder 1.
        Assert.Equal("new.person@example.com", Read(f, db => db.Policyholders.Single(p => p.Id == 1).Email));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("HOLDER1@EXAMPLE.COM")] // same address, different case
    public async Task Characterise_ContactEmail_IsIgnoredWhenInvalidOrTheSame(string email)
    {
        using var f = new ApiFactory();

        await Import(f, Csv(Row("R1", policy: "HLH-100001", email: email)));

        Assert.Equal("holder1@example.com", Read(f, db => db.Policyholders.Single(p => p.Id == 1).Email));
    }

    [Fact]
    public async Task Characterise_ODD_EmailIsOverwritten_EvenWhenTheRowIsThenRejected()
    {
        using var f = new ApiFactory();
        await Import(f, Csv(Row("R1")));
        f.Db(db => { db.Claims.Single().Status = "Closed"; db.SaveChanges(); });

        var r = await Import(f, Csv(Row("R1", email: "side.effect@example.com"))); // rejected: claim is Closed

        Counts(r, 1, 0, 0, 1);
        Assert.Equal("side.effect@example.com", Read(f, db => db.Policyholders.Single(p => p.Id == 1).Email));
    }

    // ---- handler auto-assignment ---------------------------------------------------------------------------

    [Fact]
    public async Task Characterise_HandlerAssignment_SpreadsRowsAcrossActiveHandlersOfTheRegionByName()
    {
        using var f = new ApiFactory();

        // Region 1 handlers by name: Anna, Ben, Carl, Ivan, Ivy (Hal is inactive). Each starts with no open claims.
        await Import(f, Csv(Row("R1", amount: "100"), Row("R2", amount: "100"), Row("R3", amount: "100"),
            Row("R4", amount: "100"), Row("R5", amount: "100"), Row("R6", amount: "100")));

        var owners = Read(f, db => db.Claims.OrderBy(c => c.Id).Select(c => c.HandlerId).ToList());
        Assert.Equal(new int?[] { Ids.Anna, Ids.Ben, Ids.Carl, 11, Ids.Ivy, Ids.Anna }, owners);
    }

    [Fact]
    public async Task Characterise_HandlerAssignment_SkipsHandlersWhoseLimitIsBelowTheAmount_WhenAnotherCanCover()
    {
        using var f = new ApiFactory();

        await Import(f, Csv(Row("SMALL", amount: "1850"), Row("BIG", amount: "5000")));

        // Anna (limit 2,000) takes the 1,850 claim; for 5,000 she is skipped and Ben (10,000) is next.
        Assert.Equal(Ids.Anna, ClaimByRef(f, "SMALL").HandlerId);
        Assert.Equal(Ids.Ben, ClaimByRef(f, "BIG").HandlerId);
    }

    [Fact]
    public async Task Characterise_HandlerAssignment_IgnoresLimitsWhenNobodyInTheRegionCanCoverTheAmount()
    {
        using var f = new ApiFactory();

        await Import(f, Csv(Row("HUGE", amount: "300000")));

        Assert.Equal(Ids.Anna, ClaimByRef(f, "HUGE").HandlerId); // first by name, although her limit is 2,000
    }

    [Fact]
    public async Task Characterise_HandlerAssignment_LeavesTheClaimUnassignedWhenEveryHandlerIsFull()
    {
        using var f = new ApiFactory();
        // Region 3 has one handler (Finn, max 1 open claim).
        f.Db(db =>
        {
            db.Policies.Add(new Policy
            {
                Id = 50, PolicyNumber = "HLH-300050", PolicyholderId = 1, ProductId = 1, StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 12, 31), AnnualPremium = 500m, SumInsured = 10000m, Status = "Active", RegionId = Ids.RegionLse,
                NoClaimsYears = 0, CreatedAt = Fixed.Utc(2026, 1, 1)
            });
            db.SaveChanges();
        });

        await Import(f, Csv(Row("R1", policy: "HLH-300050", amount: "100"), Row("R2", policy: "HLH-300050", amount: "100")));

        Assert.Equal(Ids.Finn, ClaimByRef(f, "R1").HandlerId);
        Assert.Null(ClaimByRef(f, "R2").HandlerId);
    }

    // ---- ODD: the "future" date is hard-coded --------------------------------------------------------------

    [Fact]
    public async Task Characterise_ODD_LossDateAfter_2026_10_01_IsCalledFuture_WhateverTheClockSays()
    {
        using var f = new ApiFactory(); // clock is 2026-10-20, so 2026-10-02 is in the past
        var r = await Import(f, Csv(Row("OK", loss: "2026-10-01", reported: "2026-10-01"), Row("LATE", loss: "2026-10-02", reported: "2026-10-02")));

        Counts(r, 2, 1, 0, 1);
        Assert.Equal(new List<(int, string)> { (3, "LossDate is in the future.") }, Errors(r));
    }

    // ---- dates, amounts and text -----------------------------------------------------------------------------

    [Theory]
    [InlineData("2026-03-04", true)]
    [InlineData("04/03/2026", true)]
    [InlineData("4 Mar 2026", true)]
    [InlineData("04-03-2026", true)]
    [InlineData("2026/03/04", false)]
    [InlineData("03/04/2026 ", true)] // cells are trimmed; read as dd/MM so this is 3 April
    [InlineData("March 4 2026", false)]
    public async Task Characterise_AcceptedLossDateFormats(string loss, bool accepted)
    {
        using var f = new ApiFactory();

        var r = await Import(f, Csv(Row("R1", loss: loss, reported: "")));

        Counts(r, 1, accepted ? 1 : 0, 0, accepted ? 0 : 1);
        if (!accepted) Assert.StartsWith("LossDate '", Errors(r).Single().Message);
    }

    [Fact]
    public async Task Characterise_DateIsReadDayFirst()
    {
        using var f = new ApiFactory();
        await Import(f, Csv(Row("R1", loss: "03/04/2026", reported: "")));

        Assert.Equal(new DateOnly(2026, 4, 3), ClaimByRef(f, "R1").LossDate);
    }

    [Fact]
    public async Task Characterise_MissingReportedDate_DefaultsToTheLossDate()
    {
        using var f = new ApiFactory();
        await Import(f, Csv(Row("R1", loss: "2026-05-01", reported: "")));

        Assert.Equal(new DateOnly(2026, 5, 1), ClaimByRef(f, "R1").ReportedDate);
    }

    [Fact]
    public async Task Characterise_BadReportedDate_IsRejectedWithItsOwnMessage()
    {
        using var f = new ApiFactory();

        var r = await Import(f, Csv(Row("R1", reported: "yesterday")));

        Assert.Equal("ReportedDate 'yesterday' is not a recognised date.", Errors(r).Single().Message);
    }

    [Theory]
    [InlineData("\"£1,234.50\"", 1234.50)]
    [InlineData("\"1 500\"", 1500)]
    [InlineData("500000", 500000)]       // exactly the ceiling is allowed
    [InlineData("0.01", 0.01)]
    public async Task Characterise_AcceptedAmounts(string amount, decimal expected)
    {
        using var f = new ApiFactory();

        var r = await Import(f, Csv(Row("R1", amount: amount)));

        Counts(r, 1, 1, 0, 0);
        Assert.Equal(expected, ClaimByRef(f, "R1").EstimatedAmount);
    }

    [Theory]
    [InlineData("0", "EstimatedAmount '0' is not a positive amount.")]
    [InlineData("-1", "EstimatedAmount '-1' is not a positive amount.")]
    [InlineData("abc", "EstimatedAmount 'abc' is not a positive amount.")]
    [InlineData("", "EstimatedAmount '' is not a positive amount.")]
    [InlineData("500000.01", "EstimatedAmount exceeds the bordereau ceiling.")]
    public async Task Characterise_RejectedAmounts(string amount, string message)
    {
        using var f = new ApiFactory();

        var r = await Import(f, Csv(Row("R1", amount: amount)));

        Counts(r, 1, 0, 0, 1);
        Assert.Equal(message, Errors(r).Single().Message);
    }

    [Fact]
    public async Task Characterise_DefaultsAndTruncation_OfTypeAndDescription()
    {
        using var f = new ApiFactory();
        var longType = new string('T', 50);
        var longText = new string('D', 1200);

        await Import(f, Csv(Row("DEFAULTS", type: "", description: ""), Row("LONG", type: longType, description: longText)), "my-file.csv");

        var defaults = ClaimByRef(f, "DEFAULTS");
        Assert.Equal(("Unspecified", "Imported from bordereau my-file.csv"), (defaults.ClaimType, defaults.Description));
        var truncated = ClaimByRef(f, "LONG");
        Assert.Equal((40, 1000), (truncated.ClaimType.Length, truncated.Description.Length));
    }

    [Fact]
    public async Task Characterise_QuotedFields_KeepCommasAndDoubledQuotes()
    {
        using var f = new ApiFactory();

        await Import(f, Csv(Row("R1", description: "\"He said \"\"hi\"\", then left\"")));

        Assert.Equal("He said \"hi\", then left", ClaimByRef(f, "R1").Description);
    }

    // ---- policy rules ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Characterise_PolicyNumberIsUpperCased_AndCancelledPoliciesAreRejected()
    {
        using var f = new ApiFactory();

        var r = await Import(f, Csv(Row("R1", policy: "hlh-100001"), Row("R2", policy: "HLH-100003")));

        Counts(r, 2, 1, 0, 1);
        Assert.Equal(new List<(int, string)> { (3, "Policy HLH-100003 is cancelled.") }, Errors(r));
    }

    [Fact]
    public async Task Characterise_LossDateOutsideThePolicyPeriod_IsRejectedOnBothSides()
    {
        using var f = new ApiFactory();

        var r = await Import(f, Csv(Row("BEFORE", loss: "2025-12-31", reported: "2025-12-31"), Row("AFTER", loss: "2027-01-01", reported: "2027-01-01")));

        Counts(r, 2, 0, 0, 2);
        // The second row is also later than the hard-coded "future" date, which is checked first.
        Assert.Equal(new List<(int, string)>
        {
            (2, "LossDate 2025-12-31 is outside the period of policy HLH-100001."),
            (3, "LossDate is in the future."),
        }, Errors(r));
    }

    // ---- file shape: headers, blank lines, duplicates ------------------------------------------------------

    [Fact]
    public async Task Characterise_MissingRequiredHeader_RejectsEveryRowAndRecordsABatch()
    {
        using var f = new ApiFactory();
        var csv = "ExternalRef,PolicyNumber,LossDate\nR1,HLH-100001,2026-05-01\nR2,HLH-100001,2026-05-01\n";

        var r = await Import(f, csv);

        Counts(r, total: 2, created: 0, updated: 0, rejected: 2);
        Assert.Equal(new List<(int, string)> { (1, "Header must contain ExternalRef, PolicyNumber, LossDate and EstimatedAmount.") }, Errors(r));
        Assert.Equal(0, Read(f, db => db.Claims.Count()));
        Assert.Equal((2, 0, 2), Read(f, db => { var b = db.ImportBatches.Single(); return (b.TotalRows, b.CreatedRows, b.RejectedRows); }));
    }

    [Fact]
    public async Task Characterise_HeaderIsCaseInsensitive_AnyOrder_WithAnExtraColumn_AndABom()
    {
        using var f = new ApiFactory();
        var csv = "﻿estimatedamount,POLICYNUMBER,Notes,lossdate,externalref\n250.00,HLH-100001,ignored,2026-05-01,R1\n";

        var r = await Import(f, csv);

        Counts(r, 1, 1, 0, 0);
        var c = ClaimByRef(f, "R1");
        Assert.Equal((250.00m, "Unspecified"), (c.EstimatedAmount, c.ClaimType));
    }

    [Fact]
    public async Task Characterise_ODD_RowNumbersCountOnlyNonBlankLines()
    {
        using var f = new ApiFactory();
        // File lines: 1 header, 2 blank, 3 good, 4 blank, 5 bad. The error says row 3, not 5.
        var csv = Header + "\n\n" + Row("R1") + "\n\n" + Row("R2", amount: "0") + "\n";

        var r = await Import(f, csv);

        Counts(r, 2, 1, 0, 1);
        Assert.Equal(new List<(int, string)> { (3, "EstimatedAmount '0' is not a positive amount.") }, Errors(r));
    }

    [Fact]
    public async Task Characterise_MissingExternalRefOrPolicyNumber_IsRejected()
    {
        using var f = new ApiFactory();

        var r = await Import(f, Csv(Row(""), Row("R2", policy: "")));

        Assert.Equal(new List<(int, string)> { (2, "ExternalRef is missing."), (3, "PolicyNumber is missing.") }, Errors(r));
    }

    [Fact]
    public async Task Characterise_ODD_ADuplicateRef_IsRejected_EvenWhenTheFirstRowWasRejectedLater()
    {
        using var f = new ApiFactory();
        // Row 1 fails on its amount, but its ExternalRef was already recorded as "seen", so row 2 (valid) is a "duplicate".
        var r = await Import(f, Csv(Row("R1", amount: "0"), Row("R1", amount: "100")));

        Counts(r, 2, 0, 0, 2);
        Assert.Equal(new List<(int, string)>
        {
            (2, "EstimatedAmount '0' is not a positive amount."),
            (3, "Duplicate ExternalRef in file."),
        }, Errors(r));
    }

    [Fact]
    public async Task Characterise_RowsThatSucceedStay_WhenLaterRowsFail_NoTransaction()
    {
        using var f = new ApiFactory();

        await Import(f, Csv(Row("GOOD"), Row("BAD", amount: "x")));

        Assert.Equal(new[] { "GOOD" }, Read(f, db => db.Claims.Select(c => c.ExternalRef!).ToList()));
    }

    [Fact]
    public async Task Characterise_EmptyFile_Gives400_WhileAWhitespaceOnlyFileGivesAnEmptyBatch()
    {
        using var f = new ApiFactory();
        using var form = new MultipartFormDataContent { { new ByteArrayContent(Array.Empty<byte>()), "file", "empty.csv" }, { new StringContent("S"), "source" } };
        var empty = await f.CreateClient().PostAsync("/api/imports/bordereau", form);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, empty.StatusCode);
        // ODD: the message is in the problem "title", not in "detail" as every other error in this API.
        Assert.Contains("\"title\":\"A CSV file is required.\"", await empty.Content.ReadAsStringAsync());

        var r = await Import(f, "\n\n");

        Counts(r, 0, 0, 0, 0);
        Assert.Equal(new List<(int, string)> { (0, "File is empty.") }, Errors(r));
        Assert.Equal(1, Read(f, db => db.ImportBatches.Count()));
    }

    [Fact]
    public async Task Characterise_ImportBatchesAreListed()
    {
        using var f = new ApiFactory();
        await Import(f, Csv(Row("R1")), "listed.csv", "Broker Y");

        var list = await f.CreateClient().GetJson("/api/imports");

        var b = Assert.Single(list.Rows());
        Assert.Equal(("listed.csv", "Broker Y", 1), (b.Str("fileName"), b.Str("source"), b.Int("createdRows")));
    }
}
