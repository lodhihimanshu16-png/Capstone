# Spec: Claims workflow

Status: **Draft. Several assumptions are unconfirmed** (section 7). No stakeholder replies have been received yet, and none are recorded here.
Author: (your name)
Stakeholder: Priya Raman, Head of Claims, Harbourline Insurance
Date: 2026-10-27
Source: `docs/stakeholder-brief.md`

## 1. Overview

Today a claim is a record with a status that anyone can set to anything (`PUT /api/claims/{id}/status` accepts any of seven names). The auditors have said this is not good enough. This feature makes the system follow the way claims are handled:

New → Triaged → Assessing → Approved or Declined → Settled → Closed, with Reopen (a reason is required) from Settled or Closed.

It adds approval limits by handler grade, a rule that the person who assessed a claim never approves it, a settlement amount that must exist before a claim is settled or closed, a clear error for every illegal move, a "what can happen next" call for one claim, a handler queue, and a limits report.

**Out of scope** (and why): paying the policyholder by bank transfer or sending a text (nothing in the system can do either, see F-03); storing a card number or security code (must not be built, see F-04); logins and tokens (see F-05); automatic reassignment to a higher grade; email or SMS alerts for late claims (the queue flags them instead); the loss-ratio ticket HLI-87 (a separate task, `docs/debug-report.md`); changing the bordereau import (it must keep working, see BR-23); new screens beyond the two the brief asks for.

## 2. Users

There is no login. The person acting is identified by `changedByUserId` in the request body, as the existing status and reopen calls already do. The system trusts it (see F-05).

| User | How recognised | May do |
|---|---|---|
| Handler (grades G1 to G4) | `users.role = 'Handler'` with an active row in `handlers` | Triage, assess, decline, approve up to the limit of their grade (but never a claim they assessed), settle, close, reopen |
| Head of Claims | `users.role = 'Manager'` (Priya Raman, user 2) | Every action except Triage, with **no amount limit**. Still bound by the assessor rule. Triage is refused (422) because the triaging person becomes the owner of the claim and the Head of Claims has no handler record (A-21). |
| Admin, ReadOnly | `users.role` of `Admin` or `ReadOnly` | Read only. Every claim action is refused. |
| Anyone | no identity | Read claims, queue, next-actions and the limits report |

The seed has 12 handlers in four regions. Grades and limits come from `handler_grades.approval_limit` (G1 2,000; G2 10,000; G3 50,000; G4 250,000). The limits are read from the table, not written into code.

## 3. Business rules

**BR-01 Legal moves.** A claim moves only along these arrows. Every other move is refused.

| From | To | Action |
|---|---|---|
| New | Triaged | Triage |
| Triaged | Assessing | Assess |
| Assessing | Approved | Approve |
| New, Triaged, Assessing | Declined | Decline |
| Approved | Settled | Settle |
| Settled | Closed | Close |
| Settled, Closed | Reopened | Reopen |
| Reopened | Assessing | Assess |

**BR-02 Arrival.** A claim is created as New. Creating a claim by hand and importing a bordereau are unchanged.

**BR-03 Triage.** New → Triaged needs: (a) the policy in force on the date of loss, meaning the policy status is not `Cancelled` and the loss date is between the policy start date and end date inclusive; (b) an estimate above zero (the handler may correct it); (c) an acting handler who is active. The acting handler becomes the owner of the claim. If that would take them over `max_open_claims`, the move is refused with 409. A claim already owned by that handler does not count against them again.

**BR-04 Assess.** Triaged → Assessing, or Reopened → Assessing, records the acting user as the **assessor** of this assessment (`assessed_by_user_id`). Any active handler or the Head of Claims may assess.

**BR-05 Approve.** Assessing → Approved needs an approved amount above zero and all of: BR-06 (assessor), BR-07 (limit), BR-16 (who may act). The amount is stored on the claim, and the approver is stored (`approved_by_user_id`).

**BR-06 Assessor never approves.** The user who is the current assessor cannot approve the claim. There is no override, no exception for small regions, and the Head of Claims is bound too. This is checked **before** the limit.

**BR-07 Approval limit.** A handler may approve up to and including the limit of their grade. Above it, the move is refused and the message names the lowest grade that could approve it (for example "needs a Senior Handler, limit 50,000.00"). Above the highest grade limit (250,000) only the Head of Claims can approve. The system does not reassign the claim. Someone does that with the existing assign call.

**BR-08 Decline.** New, Triaged or Assessing → Declined needs a reason (not blank, at most 300 characters). Declined is final: no further move, and no reopen.

**BR-09 Settle.** Approved → Settled needs a settlement amount above zero and not above the approved amount. The settlement amount is stored, and `paid_amount` is set to the same value.

**BR-10 Close.** Settled → Closed needs a settlement amount already on the claim. Closing sets `closed_date`.

**BR-11 Reopen.** Only Settled or Closed claims can be reopened, with a reason (not blank, at most 300 characters). The claim keeps its claim number and its whole history. `reopened_count` goes up by one, `closed_date` is cleared, and the assessor and approver are cleared so the new assessment starts clean. The next move is Assess (BR-04). A reopened claim cannot go straight to Approved.

**BR-12 Closed means closed.** A Closed or Declined claim cannot be edited. The only move out of Closed is Reopen (BR-11).

**BR-13 No back door.** `PUT /api/claims/{id}` can no longer set `approvedAmount` or `paidAmount` (and never sets the status). Those change only through BR-01 moves.

**BR-14 History.** Every move writes one `claim_status_history` row with the from and to status, who, when, the reason and the amount involved. The status change and its history row are saved together or not at all.

**BR-15 Two people, one claim.** If two people change the same claim at the same moment, one succeeds and the other gets 409 asking them to reload. The claim carries a version number for this.

**BR-16 Who may act.** The acting user must exist and be active. Role Handler needs an active handler record. Role Manager is allowed. Role Admin and ReadOnly are refused.

**BR-17 Blocked attempts are recorded.** When an approval is refused for BR-06 or BR-07, the attempt (claim, user, grade, limit, amount, reason, time) is saved in `claim_approval_attempts`, even though the request fails. This feeds the limits report.

**BR-18 Time in stage and "too long".** Days in stage = whole calendar days between today (UTC date, from an injected clock) and the date the claim entered its current status (latest history row for that status, else the created date). A claim is **overdue** when days in stage is greater than the target for the stage. Targets, unconfirmed: New 0 (triage the same day), Triaged 2, Assessing 10, Approved 3, Settled 5, Reopened 2. Declined and Closed have no target.

**BR-19 Queue.** The queue lists claims that are not Declined or Closed. Order: overdue first; then by how many days over the target (most first); then the oldest entry into the stage; then claim id. It can be filtered by handler and by region, and is paged.

**BR-20 Next actions.** For one claim, the call returns: the status, whether it is final, days in stage, the target, the due date, whether it is overdue, and every legal next action with whether the acting user may do it now and, if not, why. It also gives one plain sentence on the next step.

**BR-21 Limits report.** Lists (a) refused approval attempts (BR-17) and (b) approvals that went above the approver's limit: history rows that moved a claim to Approved, where the approved amount is above the limit of the approver's grade at the time of reporting. (b) covers old data from before this feature. Each row shows the claim, the kind, the amount, the limit, how far over, who, and when. It can be filtered by date and region.

**BR-22 No card data.** The system stores no card number or security code. A note whose text contains a number of 13 to 19 digits that passes the Luhn check (spaces and hyphens ignored) is refused.

**BR-23 Importer untouched.** The bordereau import keeps its current behaviour, including its rules for existing claims. New statuses do not change it. It is pinned by tests first (Task 6).

**BR-24 No password hashes in responses.** `GET /api/users` and `GET /api/users/{id}` no longer return `passwordHash`. The handler UI needs the user list, and the hash must not be sent.

## 4. API

Errors are `application/problem+json` (existing `ApiExceptionHandler`). `detail` always says what happened and, for a refused move, what is allowed.

| Status | Used for |
|---|---|
| 400 | Request shape wrong: missing or invalid field, unknown status name, amount not a positive number with at most 2 decimals |
| 404 | Claim or user not found |
| 409 | Illegal move (state conflict), handler at capacity, concurrent change |
| 422 | A business rule refuses it: assessor, limit, policy not in force, missing reason, settlement rules, non-handler role, claim is final |

### 4.1 `PUT /api/claims/{id}/status` (extended)
Request:
```json
{ "status": "Approved", "changedByUserId": 4, "reason": null,
  "estimatedAmount": null, "approvedAmount": 1500.00, "settlementAmount": null }
```
`status` is the target. `reason` is required for Declined (BR-08). `estimatedAmount` is required for Triaged. `approvedAmount` is required for Approved. `settlementAmount` is required for Settled. Reopen stays on `POST /api/claims/{id}/reopen` with `{ "changedByUserId", "reason" }`. Old callers that send only the three original fields keep working for moves that need no extra field.
Response 200: the claim (`ClaimDto`, now also `assessedByUserId`, `approvedByUserId`, `settlementAmount`).
Errors:
- 400: unknown status; Triaged without a positive `estimatedAmount`; Approved without a positive `approvedAmount`; more than two decimals on any amount. (A blank decline reason and a missing or zero settlement amount are business rules and return 422, as AC-18 and AC-20 say.)
- 404: claim or acting user not found.
- 409: illegal move: `Cannot move claim HLC-26-00007 from New to Approved. Allowed next statuses: Triaged, Declined.`; handler at capacity; concurrent change.
- 422: BR-03 (policy not in force), BR-05 to BR-10, BR-12, BR-16.

### 4.2 `POST /api/claims/{id}/reopen` (existing, now rule-checked)
Request `{ "changedByUserId": 4, "reason": "Second repair estimate received" }`. Response 200 the claim, status Reopened. Errors: 404, 422 (reason blank, claim not Settled or Closed, Declined is final, BR-16).

### 4.3 `GET /api/claims/{id}/next-actions?actingUserId={n}`
`actingUserId` is optional. Without it, `allowed` shows what the claim itself permits and the user-specific checks are skipped.
Response 200:
```json
{ "claimId": 7, "claimNumber": "HLC-26-00007", "status": "Assessing", "isFinal": false,
  "daysInStage": 4, "targetDays": 10, "dueBy": "2026-11-04", "overdue": false,
  "actingUserApprovalLimit": 10000.00,
  "actions": [
    { "action": "Approve", "toStatus": "Approved", "allowed": false,
      "blockedReason": "You assessed this claim, so you cannot approve it.",
      "requires": ["approvedAmount"] },
    { "action": "Decline", "toStatus": "Declined", "allowed": true, "blockedReason": null,
      "requires": ["reason"] } ],
  "nextStep": "Assessment is in progress. Another handler needs to approve it (estimate 4,200.00 needs a Claims Handler or above), or it can be declined with a reason." }
```
Errors: 404 (claim or acting user).

### 4.4 `GET /api/claims/queue?handlerId=&regionId=&page=1&pageSize=25`
Response 200, paged (`items`, `total`, `page`, `pageSize`). Each item: `claimId`, `claimNumber`, `status`, `productName`, `policyholderName`, `handlerName`, `estimatedAmount`, `reportedDate`, `daysInStage`, `targetDays`, `overdue`, `daysOverTarget`. The number of database queries does not depend on `pageSize`. Page size is limited to 1 to 200.

### 4.5 `GET /api/reports/limits-breaches?from=&to=&regionId=`
Response 200: a list of `{ claimId, claimNumber, kind ("BlockedAttempt" or "ApprovedAboveLimit"), amount, limit, overBy, userId, userName, gradeCode, regionCode, occurredAt, reason }`, most recent first, at most 500 rows. `from` and `to` are dates (inclusive). Errors: 400 if `to` is before `from`.

### 4.6 Changed existing calls
- `PUT /api/claims/{id}`: 422 if `approvedAmount` or `paidAmount` is sent with a value that differs from the current one (BR-13); 422 if the claim is Closed or Declined (BR-12).
- `POST /api/claims/{id}/notes`: 422 if the body contains a card number (BR-22).
- `GET /api/users`, `GET /api/users/{id}`: no `passwordHash` (BR-24).
- `GET /api/claims` and the caseload call keep their results but must not run queries per row.

### 4.7 Data changes (new migration `V006__claims_workflow.sql`; never edit old migrations)
- `claims`: `assessed_by_user_id` INT NULL, `approved_by_user_id` INT NULL (both foreign keys to `users`), `settlement_amount` DECIMAL(12,2) NULL, `row_version` INT NOT NULL DEFAULT 0. Index on `(handler_id, status)`.
- `claim_status_history`: `amount` DECIMAL(12,2) NULL. Index on `(claim_id, to_status, changed_at)`.
- New table `claim_approval_attempts` (BR-17).
- Backfill: `assessed_by_user_id` for claims now in Assessing, from the latest history row that moved them to Assessing; `approved_by_user_id` for claims now Approved, Settled or Closed, from the latest history row that moved them to Approved; `settlement_amount` for claims already Settled or Closed, from `paid_amount`.
- The EF model and the SQL must match (types, lengths, nullability, indexes).

## 5. UI

Follows the existing pages (`PageState`, `useApi`, one `api/*.ts` client per area, `StatusBadge`). Every screen has loading, empty and error states.

| Screen | Shows | Loading | Empty | Error |
|---|---|---|---|---|
| **Handler queue** `/queue` | The queue (4.4). Filters: handler, region. Overdue rows marked. Each row links to the claim. Paged. | "Loading the queue..." | "No open claims for this selection." | The API's `detail` text, with a retry |
| **Limits report** `/reports/limits-breaches` | The report (4.5). Filters: from, to, region. Columns as in 4.5. | "Loading the report..." | "No approvals above authority in this period." | The API's `detail` text, with a retry |
| **Claim detail** (existing page) | A **Next steps** panel from 4.3: the plain sentence, due date, overdue flag, and one button per allowed action (blocked actions are shown disabled with the reason). A form per action. | as the page | "No further actions. This claim is final." | The API's `detail` text, shown beside the form that failed |

An **Acting as** selector on the claim detail page chooses the user (active handlers and the Head of Claims), because there is no login. Its choice is sent as `changedByUserId` / `actingUserId`.

Form checks match the API: reason required and at most 300 characters (Decline, Reopen); amounts above zero with at most 2 decimals; settlement amount not above the approved amount; estimate above zero (Triage). A refusal from the server is shown in full.

## 6. Acceptance criteria

Each criterion has the name of the test that proves it. Tests that need real MySQL are marked **(MySQL)**. The others run on the in-memory database or with Moq. Fixed dates and a fixed clock are used, so none depends on today's date.

**Triage**
- **AC-01** `AC_01_TriageMovesNewClaimToTriagedAndTakesOwnership`
  - Given a New claim on a policy in force on the loss date, and an active handler with room for more claims
  - When the handler triages it with an estimate of 1,200.00
  - Then the claim is Triaged, the handler owns it, the estimate is 1,200.00 and one history row records New → Triaged by that user
- **AC-02** `AC_02_TriageRefusedWhenPolicyNotInForceOnLossDate`
  - Given a New claim whose loss date is before the policy start date, after its end date, or whose policy is Cancelled
  - When a handler triages it
  - Then the request returns 422 saying the policy was not in force on the loss date, and the claim stays New with no history row added
- **AC-03** `AC_03_TriageRequiresPositiveEstimate`
  - Given a New claim
  - When a handler triages it with no estimate, 0 or a negative amount
  - Then the request returns 400 and the claim stays New
- **AC-04** `AC_04_TriageRefusedWhenHandlerIsAtOpenClaimLimit`
  - Given a handler already holding `max_open_claims` open claims and a different New claim
  - When that handler triages it
  - Then the request returns 409 naming the limit, and the claim stays New

**Legal moves**
- **AC-05** `AC_05_CannotSkipStatuses`
  - Given a New claim
  - When someone moves it straight to Approved
  - Then the request returns 409 and the message says "Allowed next statuses: Triaged, Declined"
- **AC-06** `AC_06_UnknownStatusReturns400`
  - Given any claim
  - When the target status is not one of the eight names
  - Then the request returns 400
- **AC-07** `AC_07_AssessRecordsTheAssessor`
  - Given a Triaged claim
  - When handler A assesses it
  - Then the claim is Assessing, `assessedByUserId` is handler A's user id and the history records the move

**Approval rules**
- **AC-08** `AC_08_ApproveWithinLimitByAnotherHandler`
  - Given an Assessing claim assessed by handler A, and handler B (Claims Handler, limit 10,000)
  - When B approves it for 9,000.00
  - Then the claim is Approved, `approvedByUserId` is B, the approved amount is 9,000.00 and the history row has amount 9,000.00
- **AC-09** `AC_09_AssessorCannotApprove`
  - Given an Assessing claim assessed by handler A, and the amount is within A's limit
  - When A tries to approve it
  - Then the request returns 422 saying the assessor cannot approve, the claim stays Assessing, and one blocked attempt is recorded
- **AC-10** `AC_10_HeadOfClaimsWhoAssessedCannotApprove`
  - Given an Assessing claim assessed by the Head of Claims
  - When the Head of Claims tries to approve it
  - Then the request returns 422 and the claim stays Assessing
- **AC-11** `AC_11_ApproveAboveGradeLimitIsRefused`
  - Given an Assessing claim and a Claims Assistant (limit 2,000)
  - When the assistant approves 2,000.01
  - Then the request returns 422 whose message names the limit 2,000.00 and the grade that is needed (Claims Handler, limit 10,000.00); the claim stays Assessing and a blocked attempt is recorded
- **AC-12** `AC_12_ApproveExactlyAtLimitIsAllowed`
  - Given an Assessing claim and a Claims Handler (limit 10,000)
  - When a different handler approves exactly 10,000.00
  - Then the claim is Approved
- **AC-13** `AC_13_AboveTopGradeLimitNeedsHeadOfClaims`
  - Given an Assessing claim and a Technical Lead (limit 250,000)
  - When the Technical Lead approves 250,000.01
  - Then the request returns 422 saying only the Head of Claims can approve this amount
- **AC-14** `AC_14_HeadOfClaimsCanApproveAboveTopLimit`
  - Given an Assessing claim assessed by a handler, and the Head of Claims
  - When the Head of Claims approves 400,000.00
  - Then the claim is Approved
- **AC-15** `AC_15_NonHandlerRolesCannotActOnClaims`
  - Given a claim in a state where the action would otherwise be legal
  - When an Admin or a ReadOnly user tries any claim action
  - Then the request returns 422 and nothing changes
- **AC-16** `AC_16_InactiveHandlerCannotAct`
  - Given a handler or user marked inactive
  - When they try any claim action
  - Then the request returns 422 and nothing changes
- **AC-17** `AC_17_ApprovalRequiresPositiveAmount`
  - Given an Assessing claim
  - When someone approves with no amount or 0
  - Then the request returns 400 and the claim stays Assessing

**Decline**
- **AC-18** `AC_18_DeclineRequiresReason`
  - Given a claim in New, Triaged or Assessing
  - When it is declined with a blank or missing reason
  - Then the request returns 422 and the claim keeps its status; with a reason it becomes Declined
- **AC-19** `AC_19_DeclinedClaimIsFinal`
  - Given a Declined claim
  - When anyone tries any move, including Reopen
  - Then the request is refused (409 for a move, 422 for Reopen), and the claim stays Declined

**Settlement and closing**
- **AC-20** `AC_20_CannotSettleWithoutSettlementAmount`
  - Given an Approved claim
  - When it is settled with no settlement amount or 0
  - Then the request returns 422 and the claim stays Approved
- **AC-21** `AC_21_SettlementCannotExceedApprovedAmount`
  - Given an Approved claim with approved amount 5,000.00
  - When it is settled for 5,000.01
  - Then the request returns 422 naming the approved amount, and the claim stays Approved
- **AC-22** `AC_22_SettleRecordsSettlementAndPaidAmount`
  - Given an Approved claim with approved amount 5,000.00
  - When it is settled for 4,750.00
  - Then the claim is Settled, `settlementAmount` and `paidAmount` are both 4,750.00 and the history row has amount 4,750.00
- **AC-23** `AC_23_CloseSettledClaimSetsClosedDate`
  - Given a Settled claim with a settlement amount
  - When it is closed
  - Then it is Closed and `closedDate` is today's date from the clock
- **AC-24** `AC_24_CannotCloseWithoutSettlementAmount`
  - Given a Settled claim with no settlement amount (old data)
  - When it is closed
  - Then the request returns 422 and the claim stays Settled
- **AC-25** `AC_25_ClosedClaimCannotBeChanged`
  - Given a Closed claim
  - When someone edits it with `PUT /api/claims/{id}` or moves it to any status other than Reopened
  - Then the edit returns 422, the move returns 409, and the claim is unchanged

**Reopen**
- **AC-26** `AC_26_ReopenKeepsClaimNumberAndHistory`
  - Given a Closed claim with 5 history rows and `reopenedCount` 1
  - When it is reopened with a reason
  - Then the status is Reopened, the claim number is unchanged, `reopenedCount` is 2, `closedDate` is empty and the 5 old rows are still there plus a new one with the reason
- **AC-27** `AC_27_ReopenFromSettledIsAllowed`
  - Given a Settled claim
  - When it is reopened with a reason
  - Then the status is Reopened
- **AC-28** `AC_28_ReopenWithoutReasonIsRefused`
  - Given a Settled or Closed claim
  - When it is reopened with no reason, an empty reason or only spaces
  - Then the request returns 400 or 422 and the claim keeps its status
- **AC-29** `AC_29_ReopenRefusedUnlessSettledOrClosed`
  - Given claims in New, Triaged, Assessing, Approved and Reopened
  - When each is reopened
  - Then each request returns 422 saying only Settled or Closed claims can be reopened
- **AC-30** `AC_30_ReopenedClaimMustBeReassessedBeforeApproval`
  - Given a Reopened claim
  - When someone moves it to Approved, the request returns 409 with "Allowed next statuses: Assessing". After handler C assesses it, handler C cannot approve it but handler D can.

**Closing the back door**
- **AC-31** `AC_31_UpdateCannotSetApprovedOrPaidAmount`
  - Given an Assessing claim
  - When `PUT /api/claims/{id}` sends an approved amount or paid amount different from the current one
  - Then the request returns 422 and the amounts are unchanged
- **AC-32** `AC_32_StatusChangeAndHistoryAreSavedTogether` **(MySQL)**
  - Given a claim, and a failure forced while the history row is being written
  - When a move is attempted
  - Then neither the new status nor the history row is saved
- **AC-33** `AC_33_ConcurrentApprovalsOnlyOneSucceeds` **(MySQL)**
  - Given an Assessing claim and two approvers sending an approval at the same moment
  - When both requests run
  - Then exactly one returns 200 and the other returns 409; there is one Approved history row and the version number rose by one

**Next actions**
- **AC-34** `AC_34_NextActionsExplainsBlockedApproval`
  - Given an Assessing claim assessed by handler A
  - When next-actions is asked with A as the acting user, then with another handler B
  - Then for A "Approve" is not allowed with the reason that A assessed it; for B it is allowed; "Decline" is allowed for both
- **AC-35** `AC_35_NextActionsForFinalStates`
  - Given a Declined claim and a Closed claim
  - When next-actions is asked for each
  - Then the Declined claim has `isFinal` true and no actions, and the Closed claim lists only Reopen
- **AC-36** `AC_36_NextActionsShowsDueDateAndOverdue`
  - Given an Assessing claim that entered the status 11 days ago on a fixed clock (target 10)
  - When next-actions is asked
  - Then `daysInStage` is 11, `dueBy` is one day before today, and `overdue` is true; for a claim 10 days in the stage `overdue` is false
- **AC-37** `AC_37_NextActionsUnknownClaimReturns404`
  - Given no claim 9999
  - When next-actions is asked for it
  - Then the request returns 404 problem details

**Queue**
- **AC-38** `AC_38_QueueExcludesFinalClaims`
  - Given claims in every status
  - When the queue is read
  - Then Declined and Closed claims are absent and every other status is present
- **AC-39** `AC_39_QueueOrdersOverdueThenMostOverdueThenOldest`
  - Given claims with different time in stage and different targets on a fixed clock
  - When the queue is read
  - Then overdue claims come first, the most days over target first among them, then the rest oldest first, with ties broken by claim id
- **AC-40** `AC_40_QueueFiltersByHandlerAndRegionAndPages`
  - Given claims owned by different handlers in different regions
  - When the queue is read with `handlerId`, then `regionId`, then `pageSize=2`
  - Then only matching claims come back, `total` is the full match count, and the page has at most 2 items
- **AC-41** `AC_41_QueueQueryCountDoesNotGrowWithPageSize` **(MySQL)**
  - Given 100 open claims
  - When the queue is read with page size 5 and again with page size 100
  - Then the number of SQL statements run is the same both times

**Limits report**
- **AC-42** `AC_42_LimitsReportListsBlockedAttempts`
  - Given an approval of 12,000.00 refused for a Claims Handler (limit 10,000)
  - When the report is read
  - Then it has a `BlockedAttempt` row with amount 12,000.00, limit 10,000.00, overBy 2,000.00 and the user and time
- **AC-43** `AC_43_LimitsReportListsApprovalsAboveAuthority`
  - Given old history showing a claim approved for 30,000.00 by a Claims Handler
  - When the report is read
  - Then it has an `ApprovedAboveLimit` row with overBy 20,000.00, and no row for approvals at or under the limit
- **AC-44** `AC_44_LimitsReportFiltersByDateAndRegionAndHasEmptyResult`
  - Given breaches in two regions and two months
  - When the report is read with `regionId` and with `from` and `to`
  - Then only matching rows come back; a period with none returns an empty list with 200; `to` before `from` returns 400

**Other rules in the brief**
- **AC-45** `AC_45_NoteWithCardNumberIsRefused`
  - Given any claim
  - When a note contains 4111 1111 1111 1111 (Luhn valid), then a note contains an order number with 16 digits that fails the check
  - Then the first returns 422 and is not saved; the second is saved
- **AC-46** `AC_46_UsersEndpointDoesNotReturnPasswordHash`
  - Given seeded users
  - When `GET /api/users` and `GET /api/users/{id}` are called
  - Then no response contains `passwordHash`
- **AC-47** `AC_47_BordereauImportStillWorks`
  - Given `docs/samples/bordereau-sample.csv`
  - When it is imported
  - Then the created, updated and rejected counts equal those recorded by the characterisation tests, and every created claim is New
- **AC-48** `AC_48_LegacyAssessorIsBackfilledFromHistory` **(MySQL)**
  - Given a claim that was Assessing before this feature, with a history row showing who moved it there
  - When migration `V006` has run
  - Then `assessedByUserId` is that user, so the assessor rule applies to old claims too

## 7. Assumptions & Open Questions

Nothing in this section has been confirmed by the stakeholder. The mentor (playing Priya) has not yet replied. Every assumption is **unconfirmed**. If the answer differs, the "If different" column says what changes.

### 7.1 What I found in the brief

For each: what the brief says, what I found in the code or data, what I recommend, and what I need Priya to decide.

| # | The brief says | I found | I recommend | Decision needed |
|---|---|---|---|---|
| F-01 | The assessor must never approve, "no exceptions". Then: in South West and Wales "let them override it". | These two sentences contradict each other. In the seed data South West & Wales has two handlers, **both Claims Assistants (G1)**, not an Assistant and a Handler. The Assistant's limit is 2,000, so one of them could not approve most claims anyway. | No self-approval override at all (BR-06). For a short-staffed region, allow the claim to be approved by a handler from another region, or by the Head of Claims. | Which of the two rules wins, and who approves when a region has one person off. Also whether the second handler in the region is meant to be a G2. |
| F-02 | "Once a claim is closed it is closed, nobody can change it, not even me." Later: "a settled or closed claim can be reopened". | Also contradictory. | Closed cannot be edited, but Reopen with a written reason is the one permitted move (BR-11, BR-12). Declined is final with no reopen. | Is reopening a Closed claim really wanted, and should Declined ever reopen? |
| F-03 | "When a claim is settled, pay the policyholder straight away by bank transfer and text them." | The system holds no bank details and has no payment or SMS integration. The policyholder table has no account number. Money movement from an internal tool is also a control question for Finance. | Do not build. Record the settlement amount and the Settled status only, and let the existing payment process pay. Show "payment due" in the queue (Settled stage). | Whether a payment and text service is wanted as a separate project, and who owns it. |
| F-04 | "Let the handler put the full card number and the security code on the claim." | This **must not be built.** Storing a security code is prohibited by card-scheme rules (PCI DSS), and storing full card numbers puts the whole system in scope. | Refuse it (BR-22 stops a card number going into a note). Use a payment link or take the excess through the payment team. | Priya to agree. Raise with Security or Compliance. |
| F-05 | "A handler must not be able to approve something above their limit, however they try." | There is no login. The acting user is whatever the caller sends in `changedByUserId`. Anyone who can call the API could name a senior handler. The rule is right in the code but not enforceable against a determined person. | Build the rules (BR-05 to BR-07) and say plainly in the review that identity is not authenticated. A real fix is login with tokens, which is a separate piece of work. | Is the audit expectation met by rules plus a log, or is login a prerequisite? |
| F-06 | "Anything above that [250,000] comes to me personally." | Priya is a user with role Manager, not a handler with a grade. Nothing in the data gives her a limit. | The Head of Claims (role Manager) has no limit (BR-07, BR-16). Others are refused above 250,000. | Confirm Manager is the right role, and whether anyone else may approve above 250,000. |
| F-07 | "If a claim is above your limit it goes up a grade." | Nothing says whether the system should move it. | Refuse the approval with a message naming the grade needed, and log it (BR-07, BR-17). Do not reassign automatically. | Should the system reassign? To whom? |
| F-08 | A triage step: "checks the policy was in force on the date of loss, sets the estimate and takes ownership." | There is no Triaged status. A policy has a start date, an end date and a status but no cancellation date, so "in force on the date of loss" cannot be checked exactly for a cancelled policy. | Add Triaged (BR-01). A Cancelled policy never counts as in force (BR-03). | Is a cancelled policy ever in force for a loss before it was cancelled? If so, a cancellation date is needed. |
| F-09 | "Triage ideally the same day." "Claims that sit around too long at any stage need to be flagged to me." | No time limits are given and nothing tells the system what "too long" is. There is no way to send an alert. | Targets per stage as in BR-18, shown as an overdue flag in the queue and next-actions. No email. | The real targets for each stage, and whether she wants an alert as well as a flag. |
| F-10 | "A claim cannot be marked Settled unless there is a settlement amount." Closed also needs one. | The table has `approved_amount` and `paid_amount` but no settlement amount. Today `paid_amount` is set to approved amount minus excess automatically. | A new `settlement_amount`; Settle requires it, above zero and not above the approved amount; `paid_amount` takes the same value (BR-09, BR-10). | Is the settlement amount net of the excess? Can it ever be more than the approved amount? |
| F-11 | "Oldest and most urgent first." | "Urgent" is not defined. | Overdue first, then most days over target, then oldest (BR-19). Amount is not used. | What makes a claim urgent (vulnerable customer, size, type)? |
| F-12 | "My team's claims." | The data has handlers and regions but no team. | Filter by handler or by region. | Is a team a region? |
| F-13 | "A limits report showing where approvals went above a handler's authority." | The old system recorded neither the approver's amount nor refused attempts. Once this feature is in, no approval can go above a limit, so the report would be empty except for old data and blocked attempts. | Report both blocked attempts and old approvals above the limit (BR-21). | Which does she mean, or both? |
| F-14 | "Reopened claims should keep their original claim number and their full history." | Already true (the row is kept). | Keep it and prove it with a test (AC-26). | None. |
| F-15 | "Please do not break [the bordereau import]." | The importer is one long method that updates existing claims regardless of workflow state, and has no tests. | Leave behaviour unchanged. Pin it with tests before any refactor (BR-23, AC-47). | Should an import be allowed to change the estimate of a claim that is already Approved or Settled? (Today it can.) |
| F-16 | "The loss ratio report for the board does not feel right." | A separate ticket (HLI-87) with its own investigation. | Not part of this feature. | None. |

### 7.2 Questions to ask, and replies

Write each question to the stakeholder in plain words, then record the answer here. **None has been asked or answered yet.**

| # | Question (plain words) | Asked | Reply |
|---|---|---|---|
| Q-01 | When South West and Wales are short-staffed, who should be allowed to approve a claim that the one remaining person assessed? Someone from another region, or only you? (F-01) | not yet | none |
| Q-02 | You said a closed claim can never be changed, and also that you can reopen one with a reason. Is reopening a closed claim what you want? And should a declined claim ever be reopened? (F-02) | not yet | none |
| Q-03 | How many days is too long for a claim to sit at each stage: New, Triaged, Assessing, Approved, Settled and Reopened? (F-09) | not yet | none |
| Q-04 | When a claim is above someone's limit, should the system just stop them and tell them who can do it, or should it hand the claim to someone of the next grade automatically? (F-07) | not yet | none |
| Q-05 | For the limits report, do you want to see the times someone tried to approve above their limit and was stopped, the old approvals that went over, or both? (F-13) | not yet | none |
| Q-06 | We cannot take card details, send bank payments or send texts from this system. Is it enough for now that the claim shows the settlement amount and a "payment due" status? (F-03, F-04) | not yet | none |
| Q-07 | Is the settlement amount the amount after the excess is taken off? Can it ever be higher than the amount you approved? (F-10) | not yet | none |
| Q-08 | Is a cancelled policy ever still valid for an accident before it was cancelled? (F-08) | not yet | none |
| Q-09 | Is "your team" a region? And what makes a claim urgent apart from being late? (F-11, F-12) | not yet | none |
| Q-10 | In South West and Wales, both handlers are Claims Assistants in our data. Is that right? (F-01) | not yet | none |

### 7.3 Assumptions (all unconfirmed)

| # | Assumption | Confirm with | If different |
|---|---|---|---|
| A-01 | No override of the assessor rule for anyone (BR-06). | Priya (Q-01) | A logged cross-region or manager sign-off would be a new, separately named action. The rule itself would not change. |
| A-02 | Reopen is allowed from Settled and Closed only; Declined is final (BR-11, BR-08). | Priya (Q-02) | Remove Closed from the reopen list, or add Declined, in the transition table only. |
| A-03 | Targets in days: New 0, Triaged 2, Assessing 10, Approved 3, Settled 5, Reopened 2 (BR-18). | Priya (Q-03) | The targets are configuration values, so only their numbers change. |
| A-04 | Above-limit approvals are refused, not reassigned (BR-07). | Priya (Q-04) | Reassignment would be an extra step and a new action. |
| A-05 | The limits report shows both blocked attempts and old approvals above the limit (BR-21). | Priya (Q-05) | Drop one of the two kinds from the report query. |
| A-06 | No payment, bank transfer, SMS or card data is built (F-03, F-04). | Priya (Q-06) and Security | Payment would be a separate spec. Card data stays refused. |
| A-07 | Settlement amount is entered by the handler, above zero and not above the approved amount; it sets `paid_amount` (BR-09). | Priya and Finance (Q-07) | Add an automatic excess deduction, or allow a higher amount. |
| A-08 | A Cancelled policy never counts as in force (BR-03). | Priya (Q-08) | A cancellation date column is needed and BR-03 changes to compare the loss date with it. |
| A-09 | A team is a region (BR-19). | Priya (Q-09) | Add a team table. Not built now. |
| A-10 | The acting user is the `changedByUserId` sent by the caller, trusted without login (F-05). | Priya and the platform owner | Replace with token login. The rules stay, only where the user id comes from changes. |
| A-11 | The Head of Claims is role Manager and has no limit; Admin and ReadOnly cannot act (BR-16). | Priya | Change the role mapping only. |
| A-12 | Any active handler can assess; the assessor is who performed the move into Assessing (BR-04). | Priya | Restrict assessment by grade or role. |
| A-13 | Decline is allowed from New, Triaged and Assessing, so a claim failing the policy check can be refused (BR-08). | Priya | Remove New or Triaged from the decline list. |
| A-14 | A reopened claim keeps its `paid_amount` and `settlement_amount` until it is settled again, and the new settlement amount is the new total, not an extra payment. | Priya and Finance | Add a payment-ledger table. Not built now. |
| A-15 | Notes can still be added to a Closed or Declined claim (they are not "the claim"). | Priya | Refuse notes on final claims. |
| A-16 | An import may still update the estimate of an existing claim, as it does today (BR-23). | Priya (F-15) | Refuse updates for claims past Assessing, after the characterisation tests exist. |
| A-17 | The approver's limit at report time is used for old approvals, and the claim's current approved amount stands in for the amount at the time (old history rows have no amount). | Priya | Accept as an approximation, or leave old data out of the report. |
| A-18 | Dates are calendar dates in UTC using an injected clock. A claim entering a stage late in the evening UK time may count as the next day in summer. | Priya | Use Europe/London dates everywhere. |
| A-19 | The Luhn check is a reasonable test for "a card number" in a note (BR-22). | Security | Wider or narrower patterns. |
| A-21 | The Head of Claims cannot triage, because triage assigns ownership to a handler and she is not one (BR-03). Found while building: the first draft of section 2 said she could do "the same actions". | Priya | Give the Head of Claims a handler record, or let triage leave the owner unchanged when a Manager does it. |
| A-20 | `GET /api/users` is changed to stop returning password hashes (BR-24). It is a security fix needed by the UI, not a brief item. | Priya | Leave it for the review step only. |

## 8. Traceability

| Brief statement | Business rule | AC | API / UI element | Test |
|---|---|---|---|---|
| "Every claim arrives as New" | BR-02 | AC-47 | Create claim, bordereau import | `AC_47_BordereauImportStillWorks` |
| "A handler then triages it: checks the policy was in force on the date of loss, sets the estimate and takes ownership" | BR-03 | AC-01, AC-02, AC-03, AC-04 | `PUT /status` (Triaged); Claim detail form | `AC_01_…`, `AC_02_…`, `AC_03_…`, `AC_04_…` |
| "Then it is assessed" | BR-04 | AC-07 | `PUT /status` (Assessing); Claim detail form | `AC_07_AssessRecordsTheAssessor` |
| "Then somebody approves or declines it" | BR-01, BR-05, BR-08 | AC-05, AC-06, AC-08, AC-17, AC-18 | `PUT /status`; 409 and 400 errors | `AC_05_…`, `AC_06_…`, `AC_08_…`, `AC_17_…`, `AC_18_…` |
| "An approved claim is settled, and once the final payment has gone out it is closed" | BR-09, BR-10 | AC-22, AC-23 | `PUT /status` (Settled, Closed) | `AC_22_…`, `AC_23_…` |
| "Triage ideally the same day… too long at any stage needs flagging" | BR-18 | AC-36 | next-actions `overdue`, queue `overdue` | `AC_36_NextActionsShowsDueDateAndOverdue` |
| Grades and limits: Assistant 2,000 … Technical Lead 250,000 | BR-07 | AC-11, AC-12, AC-13 | `PUT /status` (Approved) | `AC_11_…`, `AC_12_…`, `AC_13_…` |
| "Above that comes to me personally" | BR-07, BR-16 | AC-13, AC-14 | `PUT /status` (Approved) | `AC_13_…`, `AC_14_…` |
| "A handler must not be able to approve something above their limit, however they try" | BR-07, BR-13, BR-14, BR-17 | AC-11, AC-31, AC-32 | `PUT /claims/{id}` closed to amounts | `AC_11_…`, `AC_31_…`, `AC_32_…` |
| "The person who assessed must never be the person who approves it. No exceptions." | BR-06 | AC-09, AC-10, AC-30 | `PUT /status` (Approved) | `AC_09_…`, `AC_10_…`, `AC_30_…` |
| "South West and Wales… let them override it" | none (refused, F-01) | AC-09 | not built | `AC_09_AssessorCannotApprove` |
| "Cannot be marked Settled unless there is a settlement amount" | BR-09, BR-10 | AC-20, AC-21, AC-24 | `PUT /status` (Settled, Closed) | `AC_20_…`, `AC_21_…`, `AC_24_…` |
| "Declined and Closed are final… nobody can change it" | BR-08, BR-12 | AC-19, AC-25 | `PUT /status`, `PUT /claims/{id}` | `AC_19_…`, `AC_25_…` |
| "A settled or closed claim can be reopened, but only with a reason" | BR-11 | AC-26, AC-27, AC-28, AC-29, AC-30 | `POST /reopen`; Claim detail form | `AC_26_…` to `AC_30_…` |
| "Reopened claims keep their original claim number and full history" | BR-11, BR-14 | AC-26 | `POST /reopen`, history | `AC_26_ReopenKeepsClaimNumberAndHistory` |
| "I want to see how often and why [we reopen]" | BR-11, BR-14 | AC-26 | history reason and `reopenedCount` (MCP tool `reopened_claims`) | `AC_26_…` |
| "Pay the policyholder straight away by bank transfer and text them" | none (not built, F-03) | none | not built | none |
| "Let the handler put the full card number and the security code on the claim" | BR-22 (refused, F-04) | AC-45 | note validation | `AC_45_NoteWithCardNumberIsRefused` |
| "A handler queue: oldest and most urgent first" | BR-18, BR-19 | AC-38, AC-39, AC-40, AC-41 | `GET /claims/queue`; Handler queue screen | `AC_38_…` to `AC_41_…` |
| "For any claim, one place to ask what can happen now and what is the next step" | BR-20 | AC-34, AC-35, AC-36, AC-37 | `GET /claims/{id}/next-actions`; Claim detail panel | `AC_34_…` to `AC_37_…` |
| "A limits report where approvals went above authority" | BR-17, BR-21 | AC-42, AC-43, AC-44 | `GET /reports/limits-breaches`; Limits report screen | `AC_42_…`, `AC_43_…`, `AC_44_…` |
| Two people change one claim | BR-15 | AC-33 | `PUT /status` 409 | `AC_33_ConcurrentApprovalsOnlyOneSucceeds` |
| Old claims must obey the assessor rule | BR-06 | AC-48 | migration V006 | `AC_48_LegacyAssessorIsBackfilledFromHistory` |
| Hidden requirement: users list must not leak hashes (UI needs it) | BR-24 | AC-46 | `GET /api/users` | `AC_46_UsersEndpointDoesNotReturnPasswordHash` |
| "Please do not break the bordereau import" | BR-23 | AC-47 | `POST /api/imports/bordereau` | `AC_47_BordereauImportStillWorks` |
| "I would rather have the rules right than lots of screens" | all BR | all AC | two screens plus a panel | n/a |

## 9. AC to test index

Every acceptance criterion and the test that proves it (generated from section 6).

| AC | Test | Runs on |
|---|---|---|
| AC-01 | `AC_01_TriageMovesNewClaimToTriagedAndTakesOwnership` | in-memory / Moq |
| AC-02 | `AC_02_TriageRefusedWhenPolicyNotInForceOnLossDate` | in-memory / Moq |
| AC-03 | `AC_03_TriageRequiresPositiveEstimate` | in-memory / Moq |
| AC-04 | `AC_04_TriageRefusedWhenHandlerIsAtOpenClaimLimit` | in-memory / Moq |
| AC-05 | `AC_05_CannotSkipStatuses` | in-memory / Moq |
| AC-06 | `AC_06_UnknownStatusReturns400` | in-memory / Moq |
| AC-07 | `AC_07_AssessRecordsTheAssessor` | in-memory / Moq |
| AC-08 | `AC_08_ApproveWithinLimitByAnotherHandler` | in-memory / Moq |
| AC-09 | `AC_09_AssessorCannotApprove` | in-memory / Moq |
| AC-10 | `AC_10_HeadOfClaimsWhoAssessedCannotApprove` | in-memory / Moq |
| AC-11 | `AC_11_ApproveAboveGradeLimitIsRefused` | in-memory / Moq |
| AC-12 | `AC_12_ApproveExactlyAtLimitIsAllowed` | in-memory / Moq |
| AC-13 | `AC_13_AboveTopGradeLimitNeedsHeadOfClaims` | in-memory / Moq |
| AC-14 | `AC_14_HeadOfClaimsCanApproveAboveTopLimit` | in-memory / Moq |
| AC-15 | `AC_15_NonHandlerRolesCannotActOnClaims` | in-memory / Moq |
| AC-16 | `AC_16_InactiveHandlerCannotAct` | in-memory / Moq |
| AC-17 | `AC_17_ApprovalRequiresPositiveAmount` | in-memory / Moq |
| AC-18 | `AC_18_DeclineRequiresReason` | in-memory / Moq |
| AC-19 | `AC_19_DeclinedClaimIsFinal` | in-memory / Moq |
| AC-20 | `AC_20_CannotSettleWithoutSettlementAmount` | in-memory / Moq |
| AC-21 | `AC_21_SettlementCannotExceedApprovedAmount` | in-memory / Moq |
| AC-22 | `AC_22_SettleRecordsSettlementAndPaidAmount` | in-memory / Moq |
| AC-23 | `AC_23_CloseSettledClaimSetsClosedDate` | in-memory / Moq |
| AC-24 | `AC_24_CannotCloseWithoutSettlementAmount` | in-memory / Moq |
| AC-25 | `AC_25_ClosedClaimCannotBeChanged` | in-memory / Moq |
| AC-26 | `AC_26_ReopenKeepsClaimNumberAndHistory` | in-memory / Moq |
| AC-27 | `AC_27_ReopenFromSettledIsAllowed` | in-memory / Moq |
| AC-28 | `AC_28_ReopenWithoutReasonIsRefused` | in-memory / Moq |
| AC-29 | `AC_29_ReopenRefusedUnlessSettledOrClosed` | in-memory / Moq |
| AC-30 | `AC_30_ReopenedClaimMustBeReassessedBeforeApproval` | in-memory / Moq |
| AC-31 | `AC_31_UpdateCannotSetApprovedOrPaidAmount` | in-memory / Moq |
| AC-32 | `AC_32_StatusChangeAndHistoryAreSavedTogether` | MySQL |
| AC-33 | `AC_33_ConcurrentApprovalsOnlyOneSucceeds` | MySQL |
| AC-34 | `AC_34_NextActionsExplainsBlockedApproval` | in-memory / Moq |
| AC-35 | `AC_35_NextActionsForFinalStates` | in-memory / Moq |
| AC-36 | `AC_36_NextActionsShowsDueDateAndOverdue` | in-memory / Moq |
| AC-37 | `AC_37_NextActionsUnknownClaimReturns404` | in-memory / Moq |
| AC-38 | `AC_38_QueueExcludesFinalClaims` | in-memory / Moq |
| AC-39 | `AC_39_QueueOrdersOverdueThenMostOverdueThenOldest` | in-memory / Moq |
| AC-40 | `AC_40_QueueFiltersByHandlerAndRegionAndPages` | in-memory / Moq |
| AC-41 | `AC_41_QueueQueryCountDoesNotGrowWithPageSize` | MySQL |
| AC-42 | `AC_42_LimitsReportListsBlockedAttempts` | in-memory / Moq |
| AC-43 | `AC_43_LimitsReportListsApprovalsAboveAuthority` | in-memory / Moq |
| AC-44 | `AC_44_LimitsReportFiltersByDateAndRegionAndHasEmptyResult` | in-memory / Moq |
| AC-45 | `AC_45_NoteWithCardNumberIsRefused` | in-memory / Moq |
| AC-46 | `AC_46_UsersEndpointDoesNotReturnPasswordHash` | in-memory / Moq |
| AC-47 | `AC_47_BordereauImportStillWorks` | in-memory / Moq |
| AC-48 | `AC_48_LegacyAssessorIsBackfilledFromHistory` | MySQL |
