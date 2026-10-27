using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Harbourline.Tests;

/// <summary>
/// Black-box helpers. Requests are anonymous objects (camelCase JSON), responses are read as JsonElement, so the
/// tests depend on the HTTP contract (docs/agent-map.md section 2) and not on internal class names.
/// </summary>
public static class ApiExt
{
    public static Task<HttpResponseMessage> Move(this HttpClient c, int claimId, string status, int by, string? reason = null,
        decimal? estimate = null, decimal? approved = null, decimal? settlement = null) =>
        c.PutAsJsonAsync($"/api/claims/{claimId}/status", new
        {
            status,
            changedByUserId = by,
            reason,
            estimatedAmount = estimate,
            approvedAmount = approved,
            settlementAmount = settlement
        });

    public static Task<HttpResponseMessage> Reopen(this HttpClient c, int claimId, int by, string? reason) =>
        c.PostAsJsonAsync($"/api/claims/{claimId}/reopen", new { changedByUserId = by, reason });

    public static async Task<JsonElement> GetJson(this HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"GET {url} returned {(int)r.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    public static Task<JsonElement> Claim(this HttpClient c, int id) => c.GetJson($"/api/claims/{id}");

    public static Task<JsonElement> HistoryOf(this HttpClient c, int id) => c.GetJson($"/api/claims/{id}/history");

    public static async Task<int> HistoryCount(this HttpClient c, int id) => (await c.HistoryOf(id)).GetArrayLength();

    /// <summary>Asserts the claim has this status.</summary>
    public static async Task AssertStatus(this HttpClient c, int id, string expected, string? because = null)
    {
        var claim = await c.Claim(id);
        Assert.True(expected == claim.Str("status"), $"Claim {id}: expected status {expected} but was {claim.Str("status")}. {because}");
    }

    /// <summary>Limits report rows for one claim and kind.</summary>
    public static async Task<List<JsonElement>> Breaches(this HttpClient c, int claimId, string kind)
    {
        var rows = (await c.GetJson("/api/reports/limits-breaches")).Rows();
        return rows.Where(r => r.Int("claimId") == claimId && r.Str("kind") == kind).ToList();
    }

    public static async Task<JsonElement> AssertOk(this HttpResponseMessage r)
    {
        var body = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{r.RequestMessage?.Method} {r.RequestMessage?.RequestUri?.PathAndQuery} returned {(int)r.StatusCode}: {body}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Asserts the status code and an application/problem+json body, and that its "detail" contains every text
    /// (ignoring case). Returns the detail.
    /// </summary>
    public static async Task<string> AssertProblem(this HttpResponseMessage r, HttpStatusCode expected, params string[] detailContains)
    {
        var body = await r.Content.ReadAsStringAsync();
        var where = $"{r.RequestMessage?.Method} {r.RequestMessage?.RequestUri?.PathAndQuery}";
        Assert.True(r.StatusCode == expected, $"{where}: expected {(int)expected} but got {(int)r.StatusCode}: {body}");
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(body);
        var detail = doc.RootElement.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : "";
        foreach (var text in detailContains)
            Assert.True(detail.Contains(text, StringComparison.OrdinalIgnoreCase), $"{where}: detail '{detail}' should contain '{text}'");
        return detail;
    }

    public static string? Str(this JsonElement e, string name)
    {
        var v = e.GetProperty(name);
        return v.ValueKind == JsonValueKind.Null ? null : v.GetString();
    }

    public static int? Int(this JsonElement e, string name)
    {
        var v = e.GetProperty(name);
        return v.ValueKind == JsonValueKind.Null ? null : v.GetInt32();
    }

    public static decimal? Dec(this JsonElement e, string name)
    {
        var v = e.GetProperty(name);
        return v.ValueKind == JsonValueKind.Null ? null : v.GetDecimal();
    }

    public static bool Bool(this JsonElement e, string name) => e.GetProperty(name).GetBoolean();

    public static bool IsNull(this JsonElement e, string name) => !e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null;

    public static List<JsonElement> Rows(this JsonElement array) => array.EnumerateArray().ToList();

    /// <summary>Reads a timestamp as UTC whatever the machine time zone is.</summary>
    public static DateTime Utc(this JsonElement e, string name) =>
        DateTime.Parse(e.GetProperty(name).GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    /// <summary>History rows that moved the claim from one status to another.</summary>
    public static List<JsonElement> Moves(this JsonElement history, string? from, string to) =>
        history.Rows().Where(r => r.Str("fromStatus") == from && r.Str("toStatus") == to).ToList();
}

/// <summary>One claim action: how to build a claim where it is legal, and how to attempt it as a given user.</summary>
public sealed record ClaimAction(string Name, Action<ClaimSpec> Setup, Func<HttpClient, int, int, Task<HttpResponseMessage>> Call);

public static class ClaimActions
{
    /// <summary>All seven actions, each on a claim in a state where the action would otherwise be legal.</summary>
    public static readonly IReadOnlyList<ClaimAction> All = new List<ClaimAction>
    {
        new("Triage", s => s.Status = "New",
            (c, id, u) => c.Move(id, "Triaged", u, estimate: 1200m)),
        new("Assess", s => s.Status = "Triaged",
            (c, id, u) => c.Move(id, "Assessing", u)),
        new("Approve", s => { s.Status = "Assessing"; s.AssessedBy = Ids.Ben; },
            (c, id, u) => c.Move(id, "Approved", u, approved: 1000m)),
        new("Decline", s => s.Status = "New",
            (c, id, u) => c.Move(id, "Declined", u, reason: "Not covered by the policy")),
        new("Settle", s => { s.Status = "Approved"; s.Approved = 5000m; s.ApprovedBy = Ids.Carl; s.AssessedBy = Ids.Ben; },
            (c, id, u) => c.Move(id, "Settled", u, settlement: 4000m)),
        new("Close", s => { s.Status = "Settled"; s.Approved = 5000m; s.Paid = 4000m; s.Settlement = 4000m; s.ApprovedBy = Ids.Carl; },
            (c, id, u) => c.Move(id, "Closed", u)),
        new("Reopen", s => { s.Status = "Closed"; s.Approved = 5000m; s.Paid = 4000m; s.Settlement = 4000m; s.ClosedDate = new DateOnly(2026, 9, 30); },
            (c, id, u) => c.Reopen(id, u, "New evidence received")),
    };
}
