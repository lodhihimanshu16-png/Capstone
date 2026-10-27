using System.Net;

namespace Harbourline.Tests;

/// <summary>AC-01 to AC-04 (BR-03). Each test builds its own factory, so nothing is shared.</summary>
public class TriageTests
{
    [Fact]
    public async Task AC_01_TriageMovesNewClaimToTriagedAndTakesOwnership()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => { s.Status = "New"; s.PolicyId = Ids.PolicyInForce; s.LossDate = new DateOnly(2026, 5, 1); });

        var claim = await (await c.Move(id, "Triaged", Ids.Ben, estimate: 1200.00m)).AssertOk();

        Assert.Equal("Triaged", claim.Str("status"));
        Assert.Equal(Ids.Ben, claim.Int("handlerId"));
        Assert.Equal(1200.00m, claim.Dec("estimatedAmount"));
        var stored = await c.Claim(id);
        Assert.Equal("Triaged", stored.Str("status"));
        Assert.Equal(Ids.Ben, stored.Int("handlerId"));
        var rows = (await c.HistoryOf(id)).Moves("New", "Triaged");
        var row = Assert.Single(rows);
        Assert.Equal(Ids.Ben, row.Int("changedByUserId"));
    }

    [Fact]
    public async Task AC_02_TriageRefusedWhenPolicyNotInForceOnLossDate()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        // Loss on 2026-05-01: before policy 2 starts (2026-07-01), after policy 4 ended (2025-12-31), and policy 3 is Cancelled.
        var cases = new[] { Ids.PolicyStartsLater, Ids.PolicyExpired, Ids.PolicyCancelled };

        foreach (var policyId in cases)
        {
            var id = f.AddClaim(s => { s.Status = "New"; s.PolicyId = policyId; s.LossDate = new DateOnly(2026, 5, 1); });
            var historyBefore = await c.HistoryCount(id);

            var response = await c.Move(id, "Triaged", Ids.Ben, estimate: 1200m);

            await response.AssertProblem(HttpStatusCode.UnprocessableEntity, "in force");
            await c.AssertStatus(id, "New", $"policy {policyId}");
            Assert.Equal(historyBefore, await c.HistoryCount(id));
        }
    }

    [Fact]
    public async Task AC_03_TriageRequiresPositiveEstimate()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();

        foreach (decimal? estimate in new decimal?[] { null, 0m, -5m })
        {
            var id = f.AddClaim(s => s.Status = "New");
            var historyBefore = await c.HistoryCount(id);

            var response = await c.Move(id, "Triaged", Ids.Ben, estimate: estimate);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"estimate {estimate}: got {(int)response.StatusCode}");
            await c.AssertStatus(id, "New", $"estimate {estimate}");
            Assert.Equal(historyBefore, await c.HistoryCount(id));
        }
    }

    [Fact]
    public async Task AC_04_TriageRefusedWhenHandlerIsAtOpenClaimLimit()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        // Finn (MaxOpenClaims = 1) already holds one open claim.
        f.AddClaim(s => { s.Status = "Assessing"; s.HandlerId = Ids.Finn; s.AssessedBy = Ids.Ben; });
        var other = f.AddClaim(s => { s.Status = "New"; s.HandlerId = null; });

        var response = await c.Move(other, "Triaged", Ids.Finn, estimate: 800m);

        // Assumption: the 409 message names the limit value (1); the exact wording is not in the spec.
        var detail = await response.AssertProblem(HttpStatusCode.Conflict);
        Assert.Matches(@"\b1\b", detail);
        await c.AssertStatus(other, "New");
        Assert.True((await c.Claim(other)).IsNull("handlerId"), "the claim must not be given to the handler who is at the limit");
    }
}
