# Agent map

Who built what for the claims workflow (Task 4), and what had to be corrected. The behaviour is defined by `docs/specs/claims-workflow.md` (draft, assumptions unconfirmed).

## 1. How the work was split

Three agents ran in parallel, each with its own files, so none could overwrite another. They agreed the interfaces in section 2 **before** starting. The build-after-edit hook was paused (`.claude/.hooks-paused`) during this phase so that three agents were not building the same projects at once. It was switched back on afterwards.

| Agent | Owns | Must not touch |
|---|---|---|
| `backend` | `backend/Harbourline.Api/**`, `backend/db/migrations/V006__claims_workflow.sql` | `backend/Harbourline.Tests/**`, `frontend/**` |
| `test` | `backend/Harbourline.Tests/**` | `backend/Harbourline.Api/**`, `frontend/**` |
| `frontend` | `frontend/src/**` | `backend/**` |

Wave 1 (parallel): backend writes the feature, test writes the tests against the contract without compiling them (the production code does not exist yet), frontend builds the screens against the contract.
Wave 2 (after wave 1): the tests are compiled against the real code, failures are triaged into "test wrong" and "code wrong", and performance evidence is gathered.

## 2. Interface contract (agreed before the build)

Field names in JSON are camelCase versions of these C# names. Money is `decimal`. Dates are `DateOnly`, timestamps `DateTime` (UTC).

```csharp
// Dtos
public record ChangeStatusRequest(string Status, int ChangedByUserId, string? Reason,
    decimal? EstimatedAmount = null, decimal? ApprovedAmount = null, decimal? SettlementAmount = null);
// ClaimDto: all existing fields, then appended in this order:
//   int? AssessedByUserId, int? ApprovedByUserId, decimal? SettlementAmount
// ClaimHistoryDto: all existing fields, then appended: decimal? Amount
public record NextActionDto(string Action, string ToStatus, bool Allowed, string? BlockedReason, string[] Requires);
public record NextActionsDto(int ClaimId, string ClaimNumber, string Status, bool IsFinal, int DaysInStage,
    int? TargetDays, DateOnly? DueBy, bool Overdue, decimal? ActingUserApprovalLimit,
    List<NextActionDto> Actions, string NextStep);
public record QueueItemDto(int ClaimId, string ClaimNumber, string Status, string ProductName, string PolicyholderName,
    string? HandlerName, decimal EstimatedAmount, DateOnly ReportedDate, int DaysInStage, int TargetDays,
    bool Overdue, int DaysOverTarget);
public record LimitBreachDto(int ClaimId, string ClaimNumber, string Kind, decimal Amount, decimal? Limit, decimal OverBy,
    int UserId, string UserName, string? GradeCode, string RegionCode, DateTime OccurredAt, string? Reason);
```

- `Action` values: `Triage`, `Assess`, `Approve`, `Decline`, `Settle`, `Close`, `Reopen`. `Requires` values: `estimatedAmount`, `approvedAmount`, `settlementAmount`, `reason`.
- `Kind` values: `BlockedAttempt`, `ApprovedAboveLimit`.
- Status names: `New`, `Triaged`, `Assessing`, `Approved`, `Declined`, `Settled`, `Closed`, `Reopened`.

HTTP (all errors are `application/problem+json`, with the message in `detail`):

| Call | Success | Errors |
|---|---|---|
| `PUT /api/claims/{id}/status` body `ChangeStatusRequest` | 200 `ClaimDto` | 400 shape, 404 claim or user, 409 illegal move or capacity or concurrent change, 422 business rule |
| `POST /api/claims/{id}/reopen` body `{changedByUserId, reason}` | 200 `ClaimDto` | 400, 404, 422 |
| `GET /api/claims/{id}/next-actions?actingUserId=` | 200 `NextActionsDto` | 404 |
| `GET /api/claims/queue?handlerId=&regionId=&page=&pageSize=` | 200 `PagedResult<QueueItemDto>` | none |
| `GET /api/reports/limits-breaches?from=&to=&regionId=` | 200 `List<LimitBreachDto>` | 400 when `to` is before `from` |

Clock: services take a `TimeProvider` (registered as `TimeProvider.System` in `Program.cs`). Tests replace it with their own fixed clock.

Test seams: `Harbourline.Tests` builds the API through `WebApplicationFactory` (in-memory) and, for the MySQL-only criteria, a second factory pointed at a throw-away MySQL database (env `HARBOURLINE_TEST_MYSQL_ADMIN`; the tests skip with a clear message when it is absent, and fail when `REQUIRE_MYSQL_TESTS=1`).

## 3. What each agent built

**backend** (about 10 minutes, 72 tool calls)
- `V006__claims_workflow.sql`: new claim columns, `claim_status_history.amount`, table `claim_approval_attempts`, two indexes, three backfills.
- `ClaimWorkflow.cs`: the pure rules (transition table, stage targets, message helpers, policy-in-force check).
- `ClaimService` rewritten for triage, assess, approve, decline, settle, close and reopen. Also `GetNextActionsAsync` and `QueueAsync`; `ReportService.LimitsBreachesAsync`.
- A fixed-count joined query for the claim list (the N+1 fix), the optimistic version check, a card-number check on notes, no password hash in `UserDto`, a `TimeProvider` for every "now".
- Applied V006 to the local MySQL and checked the read endpoints by hand.

**test** (about 11 minutes, 47 tool calls)
- `ApiFactory` with a seeded dataset, a fixed clock and a `ClaimBuilder`; `MySqlApiFactory` with a throw-away database per run and a SQL-counting interceptor.
- All 48 AC-named tests, in nine classes, with names identical to the spec index.
- Fixed three weak existing tests (the 20 percent cap, the two `DateTime.Now` tests, the constructor-bound service tests).

**frontend** (about 2.5 minutes, 28 tool calls)
- `QueuePage`, `LimitsReportPage`, a "Next steps" panel with an "Acting as" selector and one form per action on `ClaimDetailPage`, and `components/validation.ts` for the form checks. Types and API clients per the contract. `PageState` gained an optional retry button.

## 4. What I had to correct

| What went wrong | Who | How it was found | What I did |
|---|---|---|---|
| 44 of 69 tests failed on the first run with `ManyServiceProvidersCreatedWarning`: each test builds its own factory, and EF throws after 20 internal service providers. | test | First compile and run | Added the warning to the ignored list in `ApiFactory`, with a comment. A test infrastructure fault, not a feature fault. |
| Under two truly simultaneous approvals MySQL aborted one transaction with a deadlock and the API returned 500, not the 409 the spec requires. The in-memory tests could not show it. | backend (code), test (found it) | `AC_33`, failing 10 of 10 on MySQL | A deadlock or lock-wait timeout on this save is now reported as the same 409 as a version conflict. 10 of 10 pass afterwards. See `docs/perf-note.md` section 2. |
| Triaged claims were invisible in caseload, handler workload, ageing and the capacity check, because `ClaimStatuses.Open` still listed only the old statuses. | me (my brief told the backend agent to leave `Open` unchanged) | The backend agent flagged it in its report | `Open` now includes `Triaged`. My instruction was wrong. |
| The spec said the Head of Claims can do "the same actions", but triage needs a handler record and she has none. | spec, found by backend | Backend report | Spec section 2 and a new assumption A-21: the Head of Claims cannot triage. |
| Spec section 4.1 said 400 for any missing required field, but AC-18 and AC-20 say 422 for a blank decline reason and a missing settlement amount. | spec, found by backend | Backend report | Section 4.1 corrected to match the criteria. |
| The frontend agent chose a default report period (2026-01-01 to 2026-12-31) that nothing in the spec states. | frontend | Frontend report | Left in place. It is only a screen default. Worth a question to Priya. |
| Tests were written without compiling. | test | By design (to avoid build clashes) | They compiled first time. I did not trust that, so I broke three rules on purpose (below). |

Things I did not change: the backend agent's choice to accept a status move to `Reopened` through `PUT /status` as well as `POST /reopen`; a null `approvedAmount` on `PUT /claims/{id}` treated as "not sent".

## 5. Evidence

- `dotnet test` in `backend/`: **69 passed, 0 failed, 0 skipped**, with `REQUIRE_MYSQL_TESTS=1` so the four MySQL tests really ran. 48 of the 69 are the AC-named tests.
- The build is clean (0 errors, 0 warnings). `npm run build` in `frontend/` succeeds.
- **The tests can fail.** I broke three rules one at a time, ran the relevant tests, and restored the file each time (byte-identical to the original):

| Deliberate bug | Tests that failed |
|---|---|
| Assessor rule switched off | `AC_09`, `AC_10`, `AC_30` |
| Limit check made exclusive (`<=` became `<`) | `AC_12_ApproveExactlyAtLimitIsAllowed` |
| "Overdue" made inclusive (`>` became `>=`) | `AC_36_NextActionsShowsDueDateAndOverdue` |

- Performance and concurrency numbers (query counts before and after, timings, deadlock runs): `docs/perf-note.md`.

Not verified: the screens were type-checked and built, but not driven in a browser or against a running API, so the real response shapes, error messages and the three error codes are untested from the UI side.
