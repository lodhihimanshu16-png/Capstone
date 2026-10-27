# Onboarding brief: Harbourline Claims Platform

Written for a new engineer joining the team. Every statement here was checked against the code. File paths are relative to `track-2-insurance/`.

## 1. What the system is
An internal app for about a dozen claims handlers. It registers claims against policies, keeps notes and status history, imports monthly bordereau CSVs from delegated-authority partners, and reports loss ratio by product. A React single-page app talks to an ASP.NET Core API, which talks to MySQL.

## 2. Architecture

```
Browser (React, Vite :5173)
   |  fetch('/api/...')   (Vite dev server proxies /api -> :5102)
   v
ASP.NET Core 8 API (:5102)
   Controllers -> Services -> Repositories -> EF Core (Pomelo) -> MySQL 8.4
   FluentValidation in services; exceptions -> ProblemDetails
   DbUp applies SQL migrations at startup
```

| Layer | Location | Notes |
|---|---|---|
| Startup and wiring | `backend/Harbourline.Api/Program.cs` | Registers DbContext, validators, CORS, Swagger, exception handler, and every repository and service as scoped. Runs `DatabaseMigrator.Run` unless the environment is `Testing`. |
| Controllers | `Controllers/Controllers.cs` | One-line methods that call a service and return `Ok(...)`. No rules. |
| Services | `Services/Services.cs`, `Services/Rules.cs` | All business logic. `ClaimService` is the core. `Rules.cs` has static helpers. |
| Validators | `Validators/Validators.cs` | Input shape only (required, lengths, ranges, known status names). |
| Repositories | `Repositories/Repositories.cs` | EF LINQ behind interfaces. Includes the report queries. |
| Data | `Data/HarbourlineDbContext.cs`, `Data/DatabaseMigrator.cs` | Table names and snake_case columns. DbUp runner. |
| Migrations | `backend/db/migrations/V001..V005` | Schema plus seed data (about a year of claims, policies and handlers). |
| Importer | `Imports/BordereauImportService.cs` | CSV parse, validate, upsert claims, auto-assign handler. |
| Errors | `Infrastructure/Exceptions.cs` | 404 / 409 / 422 / 400 / 500 mapped to ProblemDetails. |
| Frontend | `frontend/src` | `pages/`, `api/`, `hooks/useApi.ts`, `components/PageState.tsx`. |
| Tests | `backend/Harbourline.Tests` | 20 tests: Moq unit tests and a few API tests on in-memory EF. |

There is no authentication. The acting user id arrives in a header (`X-User-Id`, default 1) or in a request body. There is no Redis, no background job and no cache.

## 3. One request, end to end

### 3a. What the browser does: opening a claim (`GET /api/claims/{id}`)
1. `ClaimDetailPage` reads `:id` from the URL and calls `claimsApi.get(id)` and `claimsApi.notes(id)` through `useApi`.
2. `api/http.ts` runs `fetch('/api/claims/7')`. Vite forwards it to `localhost:5102`.
3. `ClaimsController.Get` calls `ClaimService.GetAsync`.
4. `ClaimRepository.GetByIdAsync` reads the row from `claims`. `ClaimNoteRepository.LastNoteAtAsync` reads the latest `claim_notes.created_at`.
5. `ClaimMapper.ToDto` builds the `ClaimDto`, including `reserveReviewDue` from `ClaimRules.IsReserveReviewDue`.
6. The JSON returns to `useApi`, which sets state. `PageState` shows loading, error or content.
7. On an error, `http.ts` shows `problem.detail ?? problem.title` from the ProblemDetails body.

### 3b. Changing a status (`PUT /api/claims/{id}/status`)
The React app has no screen or API call for this yet. Today it is reached through Swagger or curl.
1. Body: `{ "status": "Settled", "changedByUserId": 4, "reason": "..." }`.
2. `ClaimsController.ChangeStatus` calls `ClaimService.ChangeStatusAsync`.
3. `ChangeStatusRequestValidator` checks only that the status is one of the seven names and the reason is at most 300 characters.
4. `ClaimRepository.GetByIdAsync` loads the claim (no tracking). A missing claim throws `NotFoundException` (404).
5. The service sets the new status and `UpdatedAt`. For `Settled` with an approved amount it sets `PaidAmount = max(approved - excess, 0)`. For `Closed` it sets `ClosedDate`.
6. `ClaimRepository.UpdateAsync` marks the entity modified and saves: an `UPDATE claims` of every column.
7. `ClaimRepository.AddHistoryAsync` saves a second time: `INSERT INTO claim_status_history`.
8. `GetAsync` re-reads the claim and returns 200 with the `ClaimDto`.
9. Failures pass through `ApiExceptionHandler` and come back as `application/problem+json`.

Tables: `claims` (update), `claim_status_history` (insert), `claims` and `claim_notes` (read).

What this shows: there is no transition rule, the two saves are not in one transaction, the update has no concurrency check, and the acting user is taken on trust.

## 4. Data model in one paragraph
`regions`, `products`, `handler_grades` (approval limit), `users`, `handlers` (grade, region, max open claims), `policyholders`, `policies`, `claims` (status, estimated/approved/paid amount, excess, reopened count, external ref), `claim_notes`, `claim_status_history`, `import_batches`. Statuses today: New, Assessing, Approved, Declined, Settled, Closed, Reopened.

## 5. Things I found that do not match the old CLAUDE.md
The original `CLAUDE.md` described a different system: MediatR/CQRS, Dapper and stored procedures, MongoDB, JWT with `[Authorize]`, Next.js with Redux, `double` money, local-time dates, rules in controllers and a Redis ReportJob. None of that exists. It has been rewritten from the code. The real differences are in section 2 above.

## 6. Risks I noticed while reading
- No workflow rules: any status can be set at any time; `PUT /api/claims/{id}` can write approved and paid amounts directly.
- `ClaimService.ListAsync` issues about five queries per claim (N+1).
- Claim numbers come from `MAX(id)+1`, so two simultaneous creates can collide.
- No concurrency token on `claims`.
- `GET /api/users` returns password hashes.
- `claim_notes.body` is `VARCHAR(500)` in SQL but 2000 in EF and the validator.
- `ClaimRules.IsReserveReviewDue` uses `DateTime.Now`; two tests depend on it.
- The loss-ratio query counts one row per `Settled` history entry (ticket HLI-87).
- Passwords are committed in `db-setup/01-create-users.sql` and `README.md`. `compose.yaml` and `.github/workflows/ci.yml` are broken.
- Tests run on in-memory EF, so MySQL-specific behaviour is untested.

## 7. How to run it
See `README.md`. In short: start MySQL, run `db-setup/01-create-users.sql`, then `dotnet run --project Harbourline.Api` from `backend/`, and `npm run dev` from `frontend/`.
