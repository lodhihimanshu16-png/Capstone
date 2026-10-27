using System.Net;
using System.Text;

namespace Harbourline.Tests;

/// <summary>
/// Criteria that the in-memory provider cannot prove: AC-32 (transaction), AC-33 (row version), AC-41 (query count)
/// and AC-48 (migration backfill). They run against a throw-away MySQL database, see MySqlApiFactory.
/// Seed data comes from the real migrations: users 3..14 are the handlers (user 4 Rachel is G3, user 5 Tom is G2,
/// user 6 Sophie is G1), user 2 is Priya, policy 8 exists in region 2.
/// </summary>
public class MySqlTests
{
    private const int Rachel = 4;   // G3, limit 50,000
    private const int Tom = 5;      // G2, limit 10,000
    private const int Sophie = 6;   // G1, limit 2,000

    private static string InsertClaim(int id, string status, int? assessedBy = null) =>
        "INSERT INTO claims (id, claim_number, policy_id, handler_id, claim_type, status, loss_date, reported_date, description, " +
        "estimated_amount, excess, region_id, reopened_count, created_at, updated_at, assessed_by_user_id) VALUES " +
        $"({id}, 'HLC-26-{id:D5}', 8, 3, 'Collision', '{status}', '2026-05-01', '2026-05-02', 'MySQL test claim', 1000.00, 250.00, 2, 0, " +
        $"'2026-10-01 09:00:00', '2026-10-01 09:00:00', {(assessedBy?.ToString() ?? "NULL")})";

    private static string InsertHistory(int claimId, string? from, string to, int by, string at) =>
        "INSERT INTO claim_status_history (claim_id, from_status, to_status, changed_by_user_id, changed_at, reason) VALUES " +
        $"({claimId}, {(from is null ? "NULL" : $"'{from}'")}, '{to}', {by}, '{at}', NULL)";

    [MySqlFact]
    public async Task AC_32_StatusChangeAndHistoryAreSavedTogether()
    {
        using var f = new MySqlApiFactory();
        var c = f.StartClient();
        const int id = 9101;
        f.Execute(InsertClaim(id, "Assessing", assessedBy: Sophie));
        f.Execute(InsertHistory(id, "Triaged", "Assessing", Sophie, "2026-10-01 10:00:00"));
        // Force a failure at the moment the history row is written.
        f.Probe.FailWhen = text => text.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase) && text.Contains("claim_status_history");

        var response = await c.Move(id, "Approved", Tom, approved: 1000m);

        Assert.False(response.IsSuccessStatusCode, "the move must fail when its history row cannot be written");
        Assert.True(f.Probe.Failures >= 1, "the forced failure on the history INSERT did not happen, so the test proved nothing");
        Assert.Equal("Assessing", f.ScalarText($"SELECT status FROM claims WHERE id = {id}"));
        Assert.True(f.ScalarIsNull($"SELECT approved_by_user_id FROM claims WHERE id = {id}"));
        Assert.True(f.ScalarIsNull($"SELECT approved_amount FROM claims WHERE id = {id}"));
        Assert.Equal(1, f.Scalar($"SELECT COUNT(*) FROM claim_status_history WHERE claim_id = {id}"));
        Assert.Equal(0, f.Scalar($"SELECT row_version FROM claims WHERE id = {id}"));

        // Control: without the forced failure the same request succeeds, so the failure was the only obstacle.
        f.Probe.FailWhen = null;
        var retry = await c.Move(id, "Approved", Tom, approved: 1000m);
        await retry.AssertOk();
        Assert.Equal("Approved", f.ScalarText($"SELECT status FROM claims WHERE id = {id}"));
        Assert.Equal(2, f.Scalar($"SELECT COUNT(*) FROM claim_status_history WHERE claim_id = {id}"));
    }

    [MySqlFact]
    public async Task AC_33_ConcurrentApprovalsOnlyOneSucceeds()
    {
        using var f = new MySqlApiFactory();
        var c = f.StartClient();
        const int id = 9102;
        f.Execute(InsertClaim(id, "Assessing", assessedBy: Sophie));
        f.Execute(InsertHistory(id, "Triaged", "Assessing", Sophie, "2026-10-01 10:00:00"));
        var versionBefore = f.Scalar($"SELECT row_version FROM claims WHERE id = {id}");

        // Hold both requests at their first write until both have read the claim, so they really overlap
        // (without this, the second request would simply see Approved and get a plain illegal-move 409).
        var meeting = new Rendezvous(2);
        f.Probe.BeforeWrite = () => meeting.ArriveAsync(TimeSpan.FromSeconds(10));

        var first = c.Move(id, "Approved", Tom, approved: 1000m);
        var second = c.Move(id, "Approved", Rachel, approved: 1000m);
        var responses = await Task.WhenAll(first, second);

        var codes = responses.Select(r => r.StatusCode).OrderBy(x => (int)x).ToList();
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, codes);
        Assert.True(meeting.Arrived >= 2, "both requests must have reached their first write, otherwise they did not overlap");
        Assert.Equal(1, f.Scalar($"SELECT COUNT(*) FROM claim_status_history WHERE claim_id = {id} AND to_status = 'Approved'"));
        Assert.Equal("Approved", f.ScalarText($"SELECT status FROM claims WHERE id = {id}"));
        Assert.Equal(versionBefore + 1, f.Scalar($"SELECT row_version FROM claims WHERE id = {id}"));
    }

    [MySqlFact]
    public async Task AC_41_QueueQueryCountDoesNotGrowWithPageSize()
    {
        using var f = new MySqlApiFactory();
        var c = f.StartClient();
        var statuses = new[] { "New", "Triaged", "Assessing", "Approved", "Settled", "Reopened" };
        var sql = new StringBuilder();
        for (var i = 0; i < 100; i++)
        {
            var id = 9200 + i;
            var handler = (i % 12) + 1;
            sql.Append(i == 0 ? "INSERT INTO claims (id, claim_number, policy_id, handler_id, claim_type, status, loss_date, reported_date, description, estimated_amount, excess, region_id, reopened_count, created_at, updated_at) VALUES " : ", ");
            sql.Append($"({id}, 'HLC-26-{id:D5}', 8, {handler}, 'Collision', '{statuses[i % statuses.Length]}', '2026-05-01', '2026-05-02', 'Queue test claim', 1000.00, 250.00, 2, 0, '2026-10-01 09:00:00', '2026-10-01 09:00:00')");
        }
        f.Execute(sql.ToString());

        await c.GetJson("/api/claims/queue?pageSize=5");   // warm-up: one-off work must not be counted
        f.Probe.Reset();
        var small = await c.GetJson("/api/claims/queue?page=1&pageSize=5");
        var smallStatements = f.Probe.Commands.ToList();
        f.Probe.Reset();
        var large = await c.GetJson("/api/claims/queue?page=1&pageSize=100");
        var largeStatements = f.Probe.Commands.ToList();

        Assert.Equal(5, small.GetProperty("items").GetArrayLength());
        Assert.Equal(100, large.GetProperty("items").GetArrayLength());
        Assert.True(smallStatements.Count >= 1, "the queue ran no SQL at all, so the count proves nothing");
        Assert.True(smallStatements.Count == largeStatements.Count,
            $"page size 5 ran {smallStatements.Count} statements but page size 100 ran {largeStatements.Count}:\n" +
            string.Join("\n", largeStatements));
    }

    // How this test is built. DbUp keeps a journal of the scripts it has run, keyed by script name. The factory
    // first runs ONLY V001 to V005 (the schema as it was before this feature), the legacy claim is inserted with
    // plain SQL in that old shape (there is no assessed_by_user_id column yet), and only then is the application
    // started: its normal DbUp run sees V001 to V005 in the journal and applies V006 for real. The backfill is
    // therefore proven by the shipped migration, not by repeating its SQL in the test.
    [MySqlFact]
    public async Task AC_48_LegacyAssessorIsBackfilledFromHistory()
    {
        using var f = new MySqlApiFactory();
        f.ApplyMigrationsUpTo(5);
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() " +
                                 "AND table_name = 'claims' AND column_name = 'assessed_by_user_id'"));   // V006 has not run yet

        // Legacy claim 9001: Assessing. Two moves into Assessing: an old one by user 7, and the latest one by Sophie (6).
        f.Execute("INSERT INTO claims (id, claim_number, policy_id, handler_id, claim_type, status, loss_date, reported_date, description, " +
                  "estimated_amount, approved_amount, paid_amount, excess, region_id, reopened_count, created_at, updated_at) VALUES " +
                  "(9001, 'HLC-26-90001', 8, 4, 'Collision', 'Assessing', '2026-05-01', '2026-05-02', 'Legacy assessing claim', 3000.00, NULL, NULL, 250.00, 2, 0, '2026-05-02 09:00:00', '2026-06-01 10:00:00')");
        f.Execute(InsertHistory(9001, "New", "Assessing", 7, "2026-05-10 10:00:00"));
        f.Execute(InsertHistory(9001, "New", "Assessing", Sophie, "2026-06-01 10:00:00"));
        // Legacy claim 9002: Approved, approved by Tom (5).
        f.Execute("INSERT INTO claims (id, claim_number, policy_id, handler_id, claim_type, status, loss_date, reported_date, description, " +
                  "estimated_amount, approved_amount, paid_amount, excess, region_id, reopened_count, created_at, updated_at) VALUES " +
                  "(9002, 'HLC-26-90002', 8, 4, 'Collision', 'Approved', '2026-05-01', '2026-05-02', 'Legacy approved claim', 3000.00, 2500.00, NULL, 250.00, 2, 0, '2026-05-02 09:00:00', '2026-06-01 10:00:00')");
        f.Execute(InsertHistory(9002, "Assessing", "Approved", Tom, "2026-06-02 10:00:00"));

        var c = f.StartClient();   // runs V006

        var assessing = await c.Claim(9001);
        Assert.Equal(Sophie, assessing.Int("assessedByUserId"));   // the LATEST move to Assessing, not the older one by user 7
        var approved = await c.Claim(9002);
        Assert.Equal(Tom, approved.Int("approvedByUserId"));
        // The assessor rule now applies to the old claim: Sophie assessed it, so Sophie cannot approve it.
        var refused = await c.Move(9001, "Approved", Sophie, approved: 1000m);
        await refused.AssertProblem(HttpStatusCode.UnprocessableEntity, "assessor");
        Assert.Equal((long)Sophie, f.Scalar("SELECT assessed_by_user_id FROM claims WHERE id = 9001"));
    }
}
