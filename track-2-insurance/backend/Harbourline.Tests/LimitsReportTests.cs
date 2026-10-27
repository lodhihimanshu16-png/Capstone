using System.Net;

namespace Harbourline.Tests;

/// <summary>AC-42 to AC-44 (BR-17, BR-21). Old approvals are built with fixed history timestamps.</summary>
public class LimitsReportTests
{
    /// <summary>
    /// A claim approved before this feature existed: Approved status, approved amount on the claim, a history row
    /// to Approved by the approver that carries no amount (old rows have none, see A-17).
    /// </summary>
    private static int OldApproval(ApiFactory f, int approver, int region, decimal amount, DateTime at) =>
        f.AddClaim(s =>
        {
            s.Status = "Approved"; s.HandlerId = approver; s.RegionId = region; s.Approved = amount; s.ApprovedBy = approver; s.AssessedBy = Ids.Ben;
            s.History = new List<HistorySpec>
            {
                new() { From = null, To = "New", By = Ids.Ben, At = at.AddDays(-10) },
                new() { From = "Assessing", To = "Approved", By = approver, At = at, Amount = null }
            };
        });

    [Fact]
    public async Task AC_42_LimitsReportListsBlockedAttempts()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => { s.Status = "Assessing"; s.HandlerId = Ids.Ben; s.AssessedBy = Ids.Ben; s.Estimated = 12000m; });

        var refused = await c.Move(id, "Approved", Ids.Carl, approved: 12000.00m);
        await refused.AssertProblem(HttpStatusCode.UnprocessableEntity);

        var row = Assert.Single(await c.Breaches(id, "BlockedAttempt"));
        Assert.Equal(12000.00m, row.Dec("amount"));
        Assert.Equal(10000.00m, row.Dec("limit"));
        Assert.Equal(2000.00m, row.Dec("overBy"));
        Assert.Equal(Ids.Carl, row.Int("userId"));
        Assert.Equal("Carl Handler", row.Str("userName"));
        Assert.Equal("G2", row.Str("gradeCode"));
        // The time comes from the injected clock (BR-18 and the TimeProvider contract).
        Assert.Equal(Fixed.Now, row.Utc("occurredAt"));
    }

    [Fact]
    public async Task AC_43_LimitsReportListsApprovalsAboveAuthority()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var above = OldApproval(f, Ids.Carl, Ids.RegionNth, 30000m, Fixed.Utc(2026, 9, 15));
        var atLimit = OldApproval(f, Ids.Carl, Ids.RegionNth, 10000m, Fixed.Utc(2026, 9, 16));      // exactly the G2 limit
        var underSeniorLimit = OldApproval(f, Ids.Dina, Ids.RegionMid, 30000m, Fixed.Utc(2026, 9, 17));  // G3 limit is 50,000

        var report = (await c.GetJson("/api/reports/limits-breaches")).Rows();

        var row = Assert.Single(report, r => r.Int("claimId") == above);
        Assert.Equal("ApprovedAboveLimit", row.Str("kind"));
        Assert.Equal(30000m, row.Dec("amount"));
        Assert.Equal(10000m, row.Dec("limit"));
        Assert.Equal(20000m, row.Dec("overBy"));
        Assert.Equal(Ids.Carl, row.Int("userId"));
        Assert.Equal("G2", row.Str("gradeCode"));
        Assert.Equal("NTH", row.Str("regionCode"));
        Assert.Equal(Fixed.Utc(2026, 9, 15), row.Utc("occurredAt"));
        Assert.DoesNotContain(report, r => r.Int("claimId") == atLimit);
        Assert.DoesNotContain(report, r => r.Int("claimId") == underSeniorLimit);
        Assert.Single(report);
    }

    [Fact]
    public async Task AC_44_LimitsReportFiltersByDateAndRegionAndHasEmptyResult()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var x = OldApproval(f, Ids.Carl, Ids.RegionNth, 30000m, Fixed.Utc(2026, 8, 10, 9));   // August, NTH
        var y = OldApproval(f, Ids.Carl, Ids.RegionNth, 20000m, Fixed.Utc(2026, 9, 12, 15));  // September, NTH
        var z = OldApproval(f, Ids.Dina, Ids.RegionMid, 60000m, Fixed.Utc(2026, 9, 20, 11));  // September, MID

        async Task<List<int>> Ids_(string query) =>
            (await c.GetJson("/api/reports/limits-breaches" + query)).Rows().Select(r => r.Int("claimId")!.Value).ToList();

        Assert.Equal(new[] { z, y, x }, await Ids_(""));                                       // most recent first
        Assert.Equal(new[] { z }, await Ids_($"?regionId={Ids.RegionMid}"));
        Assert.Equal(new[] { y, x }, await Ids_($"?regionId={Ids.RegionNth}"));
        Assert.Equal(new[] { z, y }, await Ids_("?from=2026-09-01&to=2026-09-30"));
        Assert.Equal(new[] { y }, await Ids_("?from=2026-09-12&to=2026-09-12"));               // both ends inclusive
        Assert.Equal(new[] { y }, await Ids_($"?from=2026-09-01&to=2026-09-30&regionId={Ids.RegionNth}"));

        var empty = await c.GetAsync("/api/reports/limits-breaches?from=2026-01-01&to=2026-01-31");
        var emptyBody = await empty.AssertOk();
        Assert.Equal(0, emptyBody.GetArrayLength());

        var backwards = await c.GetAsync("/api/reports/limits-breaches?from=2026-09-30&to=2026-09-01");
        await backwards.AssertProblem(HttpStatusCode.BadRequest);
    }
}
