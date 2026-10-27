using System.Net;
using System.Net.Http.Json;

namespace Harbourline.Tests;

/// <summary>AC-18, AC-19 (BR-08, BR-12).</summary>
public class DeclineTests
{
    [Fact]
    public async Task AC_18_DeclineRequiresReason()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();

        foreach (var status in new[] { "New", "Triaged", "Assessing" })
        {
            var id = f.AddClaim(s => { s.Status = status; s.HandlerId = Ids.Ben; s.AssessedBy = status == "Assessing" ? Ids.Ben : null; });
            var historyBefore = await c.HistoryCount(id);

            foreach (var reason in new string?[] { null, "", "   " })
            {
                var refused = await c.Move(id, "Declined", Ids.Carl, reason: reason);

                Assert.True(refused.StatusCode == HttpStatusCode.UnprocessableEntity, $"{status}, reason '{reason}': got {(int)refused.StatusCode}");
                await c.AssertStatus(id, status, $"reason '{reason}'");
            }
            Assert.Equal(historyBefore, await c.HistoryCount(id));

            var declined = await (await c.Move(id, "Declined", Ids.Carl, reason: "Not covered by the policy")).AssertOk();

            Assert.Equal("Declined", declined.Str("status"));
            var row = Assert.Single((await c.HistoryOf(id)).Moves(status, "Declined"));
            Assert.Equal("Not covered by the policy", row.Str("reason"));
        }
    }

    [Fact]
    public async Task AC_19_DeclinedClaimIsFinal()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => { s.Status = "Declined"; s.HandlerId = Ids.Ben; });
        var historyBefore = await c.HistoryCount(id);

        // Every target name except Reopened (Reopen has its own call, tested below). All required fields are supplied,
        // so the refusal can only come from the claim being final.
        foreach (var target in new[] { "New", "Triaged", "Assessing", "Approved", "Declined", "Settled", "Closed" })
        {
            var response = await c.Move(id, target, Ids.Carl, reason: "Trying again", estimate: 500m, approved: 500m, settlement: 500m);

            Assert.True(response.StatusCode == HttpStatusCode.Conflict, $"move to {target}: got {(int)response.StatusCode}");
            await c.AssertStatus(id, "Declined", $"move to {target}");
        }

        var reopen = await c.Reopen(id, Ids.Carl, "Customer appealed");
        await reopen.AssertProblem(HttpStatusCode.UnprocessableEntity);
        await c.AssertStatus(id, "Declined");
        Assert.Equal(historyBefore, await c.HistoryCount(id));
    }
}

/// <summary>AC-20 to AC-25 and AC-31 (BR-09, BR-10, BR-12, BR-13).</summary>
public class SettlementTests
{
    private static int ApprovedClaim(ApiFactory f) =>
        f.AddClaim(s => { s.Status = "Approved"; s.HandlerId = Ids.Ben; s.AssessedBy = Ids.Ben; s.ApprovedBy = Ids.Carl; s.Approved = 5000.00m; });

    [Fact]
    public async Task AC_20_CannotSettleWithoutSettlementAmount()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = ApprovedClaim(f);

        foreach (decimal? amount in new decimal?[] { null, 0m })
        {
            var response = await c.Move(id, "Settled", Ids.Ben, settlement: amount);

            // The acceptance criterion says 422 (section 4.1 would suggest 400 for a missing field; the criterion wins).
            await response.AssertProblem(HttpStatusCode.UnprocessableEntity);
            await c.AssertStatus(id, "Approved", $"amount {amount}");
        }
    }

    [Fact]
    public async Task AC_21_SettlementCannotExceedApprovedAmount()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = ApprovedClaim(f);

        var response = await c.Move(id, "Settled", Ids.Ben, settlement: 5000.01m);

        await response.AssertProblem(HttpStatusCode.UnprocessableEntity, "5,000.00");
        await c.AssertStatus(id, "Approved");
        Assert.True((await c.Claim(id)).IsNull("settlementAmount"));
    }

    [Fact]
    public async Task AC_22_SettleRecordsSettlementAndPaidAmount()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = ApprovedClaim(f);

        var claim = await (await c.Move(id, "Settled", Ids.Ben, settlement: 4750.00m)).AssertOk();

        Assert.Equal("Settled", claim.Str("status"));
        Assert.Equal(4750.00m, claim.Dec("settlementAmount"));
        Assert.Equal(4750.00m, claim.Dec("paidAmount"));
        var row = Assert.Single((await c.HistoryOf(id)).Moves("Approved", "Settled"));
        Assert.Equal(4750.00m, row.Dec("amount"));
    }

    [Fact]
    public async Task AC_23_CloseSettledClaimSetsClosedDate()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => { s.Status = "Settled"; s.HandlerId = Ids.Ben; s.Approved = 5000m; s.Paid = 4750m; s.Settlement = 4750m; });

        var claim = await (await c.Move(id, "Closed", Ids.Ben)).AssertOk();

        Assert.Equal("Closed", claim.Str("status"));
        Assert.Equal("2026-10-20", claim.Str("closedDate"));   // the fake clock's date
        Assert.Equal("2026-10-20", (await c.Claim(id)).Str("closedDate"));
    }

    [Fact]
    public async Task AC_24_CannotCloseWithoutSettlementAmount()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => { s.Status = "Settled"; s.HandlerId = Ids.Ben; s.Approved = 5000m; s.Paid = 4750m; s.Settlement = null; });

        var response = await c.Move(id, "Closed", Ids.Ben);

        await response.AssertProblem(HttpStatusCode.UnprocessableEntity);
        await c.AssertStatus(id, "Settled");
        Assert.True((await c.Claim(id)).IsNull("closedDate"));
    }

    [Fact]
    public async Task AC_25_ClosedClaimCannotBeChanged()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s =>
        {
            s.Status = "Closed"; s.HandlerId = Ids.Ben; s.Approved = 5000m; s.Paid = 4750m; s.Settlement = 4750m;
            s.ClosedDate = new DateOnly(2026, 9, 30); s.Description = "Original description";
        });
        var before = (await c.Claim(id)).GetRawText();
        var historyBefore = await c.HistoryCount(id);

        // The edit keeps approved and paid unchanged, so BR-13 is not the reason: only BR-12 can refuse it.
        var edit = await c.PutAsJsonAsync($"/api/claims/{id}", new
        {
            claimType = "Collision", description = "Edited after closing", estimatedAmount = 1000m, approvedAmount = 5000m, paidAmount = 4750m
        });
        await edit.AssertProblem(HttpStatusCode.UnprocessableEntity);

        foreach (var target in new[] { "New", "Triaged", "Assessing", "Approved", "Declined", "Settled", "Closed" })
        {
            var move = await c.Move(id, target, Ids.Carl, reason: "Trying", estimate: 500m, approved: 500m, settlement: 500m);
            Assert.True(move.StatusCode == HttpStatusCode.Conflict, $"move to {target}: got {(int)move.StatusCode}");
        }

        Assert.Equal(before, (await c.Claim(id)).GetRawText());
        Assert.Equal(historyBefore, await c.HistoryCount(id));
    }

    [Fact]
    public async Task AC_31_UpdateCannotSetApprovedOrPaidAmount()
    {
        using var f = new ApiFactory();
        var c = f.CreateClient();
        var id = f.AddClaim(s => { s.Status = "Assessing"; s.HandlerId = Ids.Ben; s.AssessedBy = Ids.Ben; s.Approved = null; s.Paid = null; });

        var setApproved = await c.PutAsJsonAsync($"/api/claims/{id}", new
        {
            claimType = "Collision", description = "Test claim", estimatedAmount = 1000m, approvedAmount = 5000m, paidAmount = (decimal?)null
        });
        await setApproved.AssertProblem(HttpStatusCode.UnprocessableEntity);

        var setPaid = await c.PutAsJsonAsync($"/api/claims/{id}", new
        {
            claimType = "Collision", description = "Test claim", estimatedAmount = 1000m, approvedAmount = (decimal?)null, paidAmount = 100m
        });
        await setPaid.AssertProblem(HttpStatusCode.UnprocessableEntity);

        var claim = await c.Claim(id);
        Assert.True(claim.IsNull("approvedAmount"));
        Assert.True(claim.IsNull("paidAmount"));
        Assert.Equal("Assessing", claim.Str("status"));
    }
}
