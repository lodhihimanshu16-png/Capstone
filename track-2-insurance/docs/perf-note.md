# Performance and concurrency note

Problems found in the inherited code that the claims workflow touches, what was done, and the evidence. "Before" is the original commit (`540db4c`) checked out into a separate folder and run against the same MySQL database. "After" is the current code. Nothing here is estimated unless it says so.

How it was measured: both builds ran against the same local MySQL 8.0.46 with the seed data (160 claims). EF Core's command log was switched on (`--Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Information`) and `Executed DbCommand` lines were counted for each single request. Time is `curl` total time for one request, three runs.

## 1. N+1 queries when listing claims (fixed, measured)

`ClaimService.ListAsync` fetched the page of claims and then, for **every row**, looked up the policy, the policyholder, the product, the handler and the note count with separate queries. The handler queue would have inherited this.

| Request | Before: SQL statements | After: SQL statements |
|---|---|---|
| `GET /api/claims?pageSize=5` | 26 | 2 |
| `GET /api/claims?pageSize=25` | 122 | 2 |
| `GET /api/claims?pageSize=100` | 491 | 2 |
| `GET /api/handlers/3/caseload` (6 open claims) | 39 | 11 |
| `GET /api/claims/queue?pageSize=5, 25, 100` (new) | n/a | 2, 2, 2 |

| Request | Before: time | After: time |
|---|---|---|
| `GET /api/claims?pageSize=100` | 0.425 s, 0.399 s, 0.427 s | 0.010 s, 0.010 s, 0.011 s |

Fix: one joined, projected query for the page plus one count query (`ListItemsAsync`). The new queue endpoint is built the same way. Test: `AC_41_QueueQueryCountDoesNotGrowWithPageSize` runs on MySQL, creates 100 open claims and asserts the same number of statements for page size 5 and 100.

Not fully fixed: the caseload call still loops over the five open statuses, so it runs 11 statements, not 2. That is a fixed number (it does not grow with the number of claims), and it was left alone because the brief does not touch it.

## 2. Two people changing one claim (fixed, measured)

Before: `ChangeStatusAsync` read the claim, set the status and wrote the whole row back with no version check. *This is from reading the code, not from a measured run:* two people changing the same claim at the same moment would both succeed, the later write would win silently, and two history rows would be written.

After:
- `claims.row_version` is an optimistic concurrency token. The update is `WHERE id = @id AND row_version = <the version that was read>`. If no row matches, the caller gets 409 "This claim was changed by someone else while you were working on it. Reload the claim and try again."
- The status change and the history row are saved in one `SaveChanges`, so they succeed or fail together. Test: `AC_32_StatusChangeAndHistoryAreSavedTogether` (MySQL) forces a failure on the history insert and checks that neither the status nor a history row was saved.
- **A second problem showed up only on real MySQL.** With two truly simultaneous approvals, MySQL aborted one transaction with "Deadlock found when trying to get lock", and the API answered **500**. The in-memory provider has no locks, so the original 20 tests and every in-memory test could never see it.

| `AC_33_ConcurrentApprovalsOnlyOneSucceeds` (two simultaneous approvals, MySQL) | Result |
|---|---|
| Before the deadlock fix | failed 10 of 10 runs: one request 200, the other 500 |
| After the deadlock fix | passed 10 of 10 runs: one 200, one 409, exactly one Approved history row, `row_version` up by exactly 1 |

Fix: a MySQL deadlock or lock-wait timeout during this save is reported as the same 409 as a version conflict. The aborted transaction rolls back completely, so nothing is half written. This treats the symptom. A further step would be to take the claim row lock first so the deadlock does not happen at all. It was not needed to meet the criterion.

## 3. Schema and model agreement (fixed, partly proven)

- `claim_notes.body` is `VARCHAR(500)` in `V001`, but the EF model and `CreateNoteRequestValidator` allowed 2,000 characters, so a long note would have failed inside MySQL. The model and the validator now say 500. **Not proven by a test.** The in-memory provider does not enforce column length, and I did not add a MySQL test for it.
- New columns, the new table and both new indexes are in `V006__claims_workflow.sql` and match the EF model. The migration ran cleanly on the seeded database, and `AC_48_LegacyAssessorIsBackfilledFromHistory` runs V001 to V005, inserts legacy-shaped rows, then lets the app apply V006 and checks the backfill.
- Indexes `claims(handler_id, status)` and `claim_status_history(claim_id, to_status, changed_at)` were added for the queue and report queries. **Not proven.** With 160 claims MySQL would not show a difference, and I did not run `EXPLAIN` on a larger set.

## 4. Test reliability (fixed)

- `ClaimRules.IsReserveReviewDue` used `DateTime.Now`, so its two tests depended on the day they ran. It now takes the current time as an argument and the tests use fixed dates.
- The workflow reads time from an injected `TimeProvider`. Tests use a fixed clock (2026-10-20 10:00 UTC), so no test depends on today's date.
- `Renewal_discount_is_capped_at_20_percent` asserted `<= 0.30` while claiming 20 percent, so it passed for any value up to 30 percent. It is now `..._30_percent` and asserts exactly `0.30m` (9 years) and `0.25m` (5 years).

## 5. Found but not fixed (the feature does not touch them)

- `ClaimRepository.NextSequenceAsync` uses `MAX(id) + 1`, so two simultaneous claim creations can get the same claim number. The unique index then turns the second into a 500. Claim creation is not part of this feature. Fix when touched: a sequence table or `AUTO_INCREMENT` based number.
- `ReportService` hard-codes the default report dates, and the importer hard-codes `2026-10-01` as "today" for the future-date check.
- Unsalted SHA-256 password hashes.
