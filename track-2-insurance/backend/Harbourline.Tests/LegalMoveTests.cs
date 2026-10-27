using System.Net;

namespace Harbourline.Tests;

/// <summary>AC-05 to AC-07 (BR-01, BR-04).</summary>
public class LegalMoveTests
{
    [Fact]
    public async Task AC_05_CannotSkipStatuses()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => s.Status = "New");

        var response = await c.Move(id, "Approved", Ids.Ben, approved: 100m);

        await response.AssertProblem(HttpStatusCode.Conflict, "Allowed next statuses: Triaged, Declined");
        await c.AssertStatus(id, "New");
    }

    [Fact]
    public async Task AC_06_UnknownStatusReturns400()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => s.Status = "New");

        var response = await c.Move(id, "Frozen", Ids.Ben);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await c.AssertStatus(id, "New");
    }

    [Fact]
    public async Task AC_07_AssessRecordsTheAssessor()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => { s.Status = "Triaged"; s.HandlerId = Ids.Ben; });

        var claim = await (await c.Move(id, "Assessing", Ids.Carl)).AssertOk();

        Assert.Equal("Assessing", claim.Str("status"));
        Assert.Equal(Ids.Carl, claim.Int("assessedByUserId"));
        Assert.Equal(Ids.Carl, (await c.Claim(id)).Int("assessedByUserId"));
        var row = Assert.Single((await c.HistoryOf(id)).Moves("Triaged", "Assessing"));
        Assert.Equal(Ids.Carl, row.Int("changedByUserId"));
    }
}
