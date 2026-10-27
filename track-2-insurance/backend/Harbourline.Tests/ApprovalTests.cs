using System.Net;

namespace Harbourline.Tests;

/// <summary>AC-08 to AC-17 (BR-05, BR-06, BR-07, BR-16, BR-17).</summary>
public class ApprovalTests
{
    private static int AssessingClaim(ApiFactory f, int assessedBy, decimal estimate = 1000m) =>
        f.AddClaim(s => { s.Status = "Assessing"; s.HandlerId = Ids.Ben; s.AssessedBy = assessedBy; s.Estimated = estimate; });

    [Fact]
    public async Task AC_08_ApproveWithinLimitByAnotherHandler()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = AssessingClaim(f, assessedBy: Ids.Ben);

        var claim = await (await c.Move(id, "Approved", Ids.Carl, approved: 9000.00m)).AssertOk();

        Assert.Equal("Approved", claim.Str("status"));
        Assert.Equal(Ids.Carl, claim.Int("approvedByUserId"));
        Assert.Equal(9000.00m, claim.Dec("approvedAmount"));
        var row = Assert.Single((await c.HistoryOf(id)).Moves("Assessing", "Approved"));
        Assert.Equal(9000.00m, row.Dec("amount"));
        Assert.Equal(Ids.Carl, row.Int("changedByUserId"));
    }

    [Fact]
    public async Task AC_09_AssessorCannotApprove()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = AssessingClaim(f, assessedBy: Ids.Ben);   // 1,500.00 is within Ben's limit of 10,000.00

        var response = await c.Move(id, "Approved", Ids.Ben, approved: 1500m);

        await response.AssertProblem(HttpStatusCode.UnprocessableEntity, "assessor");
        await c.AssertStatus(id, "Assessing");
        Assert.True((await c.Claim(id)).IsNull("approvedByUserId"));
        // Assumption: a refusal for BR-06 is listed in the limits report as a BlockedAttempt (BR-17).
        Assert.Single(await c.Breaches(id, "BlockedAttempt"));
    }

    [Fact]
    public async Task AC_10_HeadOfClaimsWhoAssessedCannotApprove()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = AssessingClaim(f, assessedBy: Ids.Priya);

        var response = await c.Move(id, "Approved", Ids.Priya, approved: 5000m);

        await response.AssertProblem(HttpStatusCode.UnprocessableEntity, "assessor");
        await c.AssertStatus(id, "Assessing");
    }

    [Fact]
    public async Task AC_11_ApproveAboveGradeLimitIsRefused()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = AssessingClaim(f, assessedBy: Ids.Ben);

        var response = await c.Move(id, "Approved", Ids.Anna, approved: 2000.01m);

        // Assumption: amounts in messages use the format 2,000.00 (invariant culture), as in the spec's examples.
        await response.AssertProblem(HttpStatusCode.UnprocessableEntity, "2,000.00", "Claims Handler", "10,000.00");
        await c.AssertStatus(id, "Assessing");
        Assert.Single(await c.Breaches(id, "BlockedAttempt"));
    }

    [Fact]
    public async Task AC_12_ApproveExactlyAtLimitIsAllowed()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = AssessingClaim(f, assessedBy: Ids.Ben);

        var claim = await (await c.Move(id, "Approved", Ids.Carl, approved: 10000.00m)).AssertOk();

        Assert.Equal("Approved", claim.Str("status"));
        Assert.Equal(10000.00m, claim.Dec("approvedAmount"));
    }

    [Fact]
    public async Task AC_13_AboveTopGradeLimitNeedsHeadOfClaims()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = AssessingClaim(f, assessedBy: Ids.Ben);

        var response = await c.Move(id, "Approved", Ids.Eve, approved: 250000.01m);

        await response.AssertProblem(HttpStatusCode.UnprocessableEntity, "Head of Claims");
        await c.AssertStatus(id, "Assessing");
    }

    [Fact]
    public async Task AC_14_HeadOfClaimsCanApproveAboveTopLimit()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = AssessingClaim(f, assessedBy: Ids.Ben);

        var claim = await (await c.Move(id, "Approved", Ids.Priya, approved: 400000.00m)).AssertOk();

        Assert.Equal("Approved", claim.Str("status"));
        Assert.Equal(400000.00m, claim.Dec("approvedAmount"));
        Assert.Equal(Ids.Priya, claim.Int("approvedByUserId"));
    }

    [Fact]
    public async Task AC_15_NonHandlerRolesCannotActOnClaims()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();

        foreach (var user in new[] { Ids.Admin, Ids.ReadOnly })
            foreach (var action in ClaimActions.All)
            {
                var id = f.AddClaim(action.Setup);
                var before = await c.Claim(id);
                var historyBefore = await c.HistoryCount(id);

                var response = await action.Call(c, id, user);

                await response.AssertProblem(HttpStatusCode.UnprocessableEntity);
                var after = await c.Claim(id);
                Assert.True(before.Str("status") == after.Str("status"), $"{action.Name} by user {user} changed the status");
                Assert.Equal(before.GetRawText(), after.GetRawText());
                Assert.Equal(historyBefore, await c.HistoryCount(id));
            }
    }

    [Fact]
    public async Task AC_16_InactiveHandlerCannotAct()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();

        // Hal: user active but handler record inactive. Ivan: handler record active but the user is inactive.
        foreach (var user in new[] { Ids.HalInactiveHandler, Ids.IvanInactiveUser })
            foreach (var action in ClaimActions.All)
            {
                var id = f.AddClaim(action.Setup);
                var before = await c.Claim(id);
                var historyBefore = await c.HistoryCount(id);

                var response = await action.Call(c, id, user);

                await response.AssertProblem(HttpStatusCode.UnprocessableEntity);
                var after = await c.Claim(id);
                Assert.True(before.Str("status") == after.Str("status"), $"{action.Name} by user {user} changed the status");
                Assert.Equal(before.GetRawText(), after.GetRawText());
                Assert.Equal(historyBefore, await c.HistoryCount(id));
            }
    }

    [Fact]
    public async Task AC_17_ApprovalRequiresPositiveAmount()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = AssessingClaim(f, assessedBy: Ids.Ben);

        foreach (decimal? amount in new decimal?[] { null, 0m })
        {
            var response = await c.Move(id, "Approved", Ids.Carl, approved: amount);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"amount {amount}: got {(int)response.StatusCode}");
            await c.AssertStatus(id, "Assessing", $"amount {amount}");
        }
    }
}
