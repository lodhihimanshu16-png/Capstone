using System.Text.Json;

namespace Harbourline.Tests;

/// <summary>AC-34 to AC-37 (BR-18, BR-20). The clock is fixed at 2026-10-20T10:00:00Z.</summary>
public class NextActionsTests
{
    private static JsonElement ActionNamed(JsonElement response, string name) =>
        response.GetProperty("actions").Rows().Single(a => a.Str("action") == name);

    [Fact]
    public async Task AC_34_NextActionsExplainsBlockedApproval()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => { s.Status = "Assessing"; s.HandlerId = Ids.Ben; s.AssessedBy = Ids.Ben; s.Estimated = 4200m; });

        var asAssessor = await c.GetJson($"/api/claims/{id}/next-actions?actingUserId={Ids.Ben}");
        var asOther = await c.GetJson($"/api/claims/{id}/next-actions?actingUserId={Ids.Carl}");

        var approveA = ActionNamed(asAssessor, "Approve");
        Assert.False(approveA.Bool("allowed"));
        Assert.Contains("assessed", approveA.Str("blockedReason") ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.True(ActionNamed(asAssessor, "Decline").Bool("allowed"));

        var approveB = ActionNamed(asOther, "Approve");
        Assert.True(approveB.Bool("allowed"));
        Assert.True(approveB.IsNull("blockedReason"));
        Assert.True(ActionNamed(asOther, "Decline").Bool("allowed"));
    }

    [Fact]
    public async Task AC_35_NextActionsForFinalStates()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var declined = f.AddClaim(s => { s.Status = "Declined"; s.HandlerId = Ids.Ben; });
        var closed = f.AddClaim(s =>
        {
            s.Status = "Closed"; s.HandlerId = Ids.Ben; s.Approved = 5000m; s.Paid = 4750m; s.Settlement = 4750m; s.ClosedDate = new DateOnly(2026, 9, 30);
        });

        var forDeclined = await c.GetJson($"/api/claims/{declined}/next-actions");
        var forClosed = await c.GetJson($"/api/claims/{closed}/next-actions");

        Assert.Equal("Declined", forDeclined.Str("status"));
        Assert.True(forDeclined.Bool("isFinal"));
        Assert.Equal(0, forDeclined.GetProperty("actions").GetArrayLength());
        var closedActions = forClosed.GetProperty("actions").Rows();
        var only = Assert.Single(closedActions);
        Assert.Equal("Reopen", only.Str("action"));
        Assert.Equal("Reopened", only.Str("toStatus"));
    }

    [Fact]
    public async Task AC_36_NextActionsShowsDueDateAndOverdue()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        // Clock: 2026-10-20. Assessing target: 10 days.
        var late = f.AddClaim(s => { s.Status = "Assessing"; s.HandlerId = Ids.Ben; s.EnteredStatusAt = Fixed.Utc(2026, 10, 9, 15); });
        var onTime = f.AddClaim(s => { s.Status = "Assessing"; s.HandlerId = Ids.Ben; s.EnteredStatusAt = Fixed.Utc(2026, 10, 10, 15); });

        var lateResult = await c.GetJson($"/api/claims/{late}/next-actions");
        var onTimeResult = await c.GetJson($"/api/claims/{onTime}/next-actions");

        Assert.Equal(11, lateResult.Int("daysInStage"));
        Assert.Equal(10, lateResult.Int("targetDays"));
        Assert.Equal("2026-10-19", lateResult.Str("dueBy"));   // one day before today
        Assert.True(lateResult.Bool("overdue"));

        Assert.Equal(10, onTimeResult.Int("daysInStage"));
        Assert.Equal("2026-10-20", onTimeResult.Str("dueBy"));
        Assert.False(onTimeResult.Bool("overdue"));
    }

    [Fact]
    public async Task AC_37_NextActionsUnknownClaimReturns404()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();

        var response = await c.GetAsync("/api/claims/9999/next-actions");

        await response.AssertProblem(System.Net.HttpStatusCode.NotFound);
    }
}

/// <summary>AC-38 to AC-40 (BR-19). AC-41 (query count) needs MySQL and is in MySqlTests.</summary>
public class QueueTests
{
    private static async Task<(List<int> Ids, int Total, int Page, int PageSize)> ReadQueue(HttpClient c, string query)
    {
        var json = await c.GetJson("/api/claims/queue" + query);
        var ids = json.GetProperty("items").Rows().Select(i => i.Int("claimId")!.Value).ToList();
        return (ids, json.Int("total")!.Value, json.Int("page")!.Value, json.Int("pageSize")!.Value);
    }

    [Fact]
    public async Task AC_38_QueueExcludesFinalClaims()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var byStatus = new Dictionary<string, int>();
        foreach (var status in new[] { "New", "Triaged", "Assessing", "Approved", "Declined", "Settled", "Closed", "Reopened" })
            byStatus[status] = f.AddClaim(s => { s.Status = status; s.HandlerId = Ids.Ben; });

        var queue = await ReadQueue(c, "?pageSize=200");

        foreach (var status in new[] { "New", "Triaged", "Assessing", "Approved", "Settled", "Reopened" })
            Assert.Contains(byStatus[status], queue.Ids);
        Assert.DoesNotContain(byStatus["Declined"], queue.Ids);
        Assert.DoesNotContain(byStatus["Closed"], queue.Ids);
        Assert.Equal(6, queue.Total);
    }

    [Fact]
    public async Task AC_39_QueueOrdersOverdueThenMostOverdueThenOldest()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        // Clock 2026-10-20. Targets: New 0, Triaged 2, Assessing 10, Approved 3.
        // Created in a deliberately scrambled order so neither insertion order nor id order gives the answer by luck.
        int Add(string status, DateTime entered) => f.AddClaim(s => { s.Status = status; s.HandlerId = Ids.Ben; s.EnteredStatusAt = entered; });
        var g = Add("New", Fixed.Utc(2026, 10, 20, 8));          // 0 days, not overdue
        var e = Add("Assessing", Fixed.Utc(2026, 10, 15, 9));    // 5 days, not overdue
        var a = Add("Assessing", Fixed.Utc(2026, 10, 5, 9));     // 15 days, 5 over
        var d = Add("Approved", Fixed.Utc(2026, 10, 19, 9));     // 1 day, not overdue
        var ff = Add("Assessing", Fixed.Utc(2026, 10, 15, 9));   // same entry time as e: tie broken by claim id
        var cc = Add("Assessing", Fixed.Utc(2026, 10, 8, 9));    // 12 days, 2 over
        var b = Add("Triaged", Fixed.Utc(2026, 10, 10, 9));      // 10 days, 8 over (most overdue)
        Assert.True(e < ff);

        var queue = await ReadQueue(c, "?pageSize=200");

        // overdue: b (8 over), a (5), cc (2); then the rest oldest first: e, ff (tie, by id), d, g.
        Assert.Equal(new[] { b, a, cc, e, ff, d, g }, queue.Ids);
        var items = (await c.GetJson("/api/claims/queue?pageSize=200")).GetProperty("items").Rows();
        Assert.True(items[0].Bool("overdue"));
        Assert.Equal(8, items[0].Int("daysOverTarget"));
        Assert.Equal(10, items[0].Int("daysInStage"));
        Assert.Equal(2, items[0].Int("targetDays"));
        Assert.False(items[3].Bool("overdue"));
    }

    [Fact]
    public async Task AC_40_QueueFiltersByHandlerAndRegionAndPages()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        int Add(string status, int handler, int region) => f.AddClaim(s => { s.Status = status; s.HandlerId = handler; s.RegionId = region; });
        var ben = new[] { Add("Triaged", Ids.Ben, Ids.RegionNth), Add("Assessing", Ids.Ben, Ids.RegionNth), Add("Approved", Ids.Ben, Ids.RegionNth) };
        var dina = new[] { Add("Assessing", Ids.Dina, Ids.RegionMid), Add("Assessing", Ids.Dina, Ids.RegionMid) };
        var gus = Add("New", Ids.Gus, Ids.RegionSww);

        var byHandler = await ReadQueue(c, $"?handlerId={Ids.Ben}");
        Assert.Equal(ben.OrderBy(x => x), byHandler.Ids.OrderBy(x => x));
        Assert.Equal(3, byHandler.Total);

        var byRegion = await ReadQueue(c, $"?regionId={Ids.RegionMid}");
        Assert.Equal(dina.OrderBy(x => x), byRegion.Ids.OrderBy(x => x));
        Assert.Equal(2, byRegion.Total);

        var firstPage = await ReadQueue(c, "?page=1&pageSize=2");
        Assert.Equal(2, firstPage.Ids.Count);
        Assert.Equal(6, firstPage.Total);
        Assert.Equal(2, firstPage.PageSize);
        var lastPage = await ReadQueue(c, "?page=3&pageSize=2");
        Assert.Equal(2, lastPage.Ids.Count);
        Assert.Equal(6, lastPage.Total);
        Assert.Empty((await ReadQueue(c, "?page=4&pageSize=2")).Ids);

        var filteredPage1 = await ReadQueue(c, $"?handlerId={Ids.Ben}&page=1&pageSize=2");
        var filteredPage2 = await ReadQueue(c, $"?handlerId={Ids.Ben}&page=2&pageSize=2");
        Assert.Equal(2, filteredPage1.Ids.Count);
        Assert.Single(filteredPage2.Ids);
        Assert.Equal(3, filteredPage2.Total);
        Assert.Equal(ben.OrderBy(x => x), filteredPage1.Ids.Concat(filteredPage2.Ids).OrderBy(x => x));
        Assert.DoesNotContain(gus, byHandler.Ids);
    }
}
