# Harbourline Claims Platform

Internal claims system for Harbourline Insurance (UK home and motor). Inherited, no owner. Stakeholder: Priya Raman, Head of Claims.

## Stack (as it really is)
- API: ASP.NET Core 8 Web API in `backend/Harbourline.Api`, one project, layered: Controllers -> Services -> Repositories.
- Data access: EF Core 8 with Pomelo MySQL (`HarbourlineDbContext`). Column names are snake_case by convention in `OnModelCreating`. No Dapper, no stored procedures.
- Database: MySQL 8.4, database `harbourline`, 11 tables (see `backend/db/migrations/V001__create_schema.sql`).
- Migrations: plain SQL `backend/db/migrations/V00x__name.sql`, embedded in the assembly and applied by DbUp in `DatabaseMigrator.Run` when the API starts. There are no EF migrations.
- Validation: FluentValidation validators in `Validators/Validators.cs`, called from services with `ValidateAndThrowAsync`.
- Errors: services throw `NotFoundException` (404), `ConflictException` (409), `BusinessRuleException` (422) or FluentValidation's `ValidationException` (400). `ApiExceptionHandler` turns them into ProblemDetails.
- Auth: none. `/api/auth/login` returns a user record but issues no token, and no controller has `[Authorize]`. The acting user comes from the `X-User-Id` header (default 1) on claim create and import, and from `ChangedByUserId` in the body on status change and reopen.
- UI: React 19 + TypeScript + Vite + react-router in `frontend/src`. State is plain React hooks (`hooks/useApi.ts`), no Redux. Vite proxies `/api` to `http://localhost:5102`.
- Tests: xUnit + Moq in `backend/Harbourline.Tests`. Most API tests use in-memory EF (`ApiFactory`, environment `Testing`, a fixed `FakeClock`); four use a throw-away MySQL database (`MySqlApiFactory`).

## Commands
- Start MySQL and the app user: see `README.md` (needs `db-setup/01-create-users.sql`).
- Run API (from `backend/`): `dotnet run --project Harbourline.Api` -> http://localhost:5102, Swagger at `/swagger`.
- Run tests (from `backend/`): `dotnet test`
- Run UI (from `frontend/`): `npm install && npm run dev` -> http://localhost:5173
- Build UI: `npm run build` (runs `tsc -b && vite build`). There is no lint script.
- Override the database: set `ConnectionStrings__Harbourline` (the committed value has no password).

## Where things live
- Claim rules: `Services/Services.cs` (`ClaimService`) and `Services/Rules.cs` (`ClaimRules`, `PolicyPricing`, `LossRatioCalculator`).
- Statuses: `ClaimStatuses` in `Domain/Entities.cs`.
- Reports: computed per request in `ReportRepository` (`Repositories/Repositories.cs`). There is no ReportJob and no Redis.
- Bordereau CSV importer: `Imports/BordereauImportService.cs`. Do not change its behaviour without tests.
- Ticket and review material: `docs/` (stakeholder brief, `bug-HLI-87.md`, `review-checklist.md`, `ci-failing-run.log`).

## Conventions seen in the code
- Controllers are one-line pass-throughs to a service. Business rules go in services, not controllers.
- Money is `decimal` in C# and `DECIMAL(12,2)` in SQL. Never use `double`.
- Timestamps are UTC, taken from the injected `TimeProvider` in new code. Dates without a time are `DateOnly`. Do not use `DateTime.Now`.
- Every schema change is a new migration file with the next version number. Never edit an old migration. Keep EF lengths and types equal to the SQL.
- Repositories read with `AsNoTracking` and write with `Entry(...).State = Modified`.
- Frontend: one `api/*.ts` client per area over `api/http.ts`, `useApi` for loading, `PageState` for loading/empty/error, `StatusBadge` for statuses.
- Do not add NuGet packages without asking Priya (carried over from the original file; confirm with your mentor).

## Claims workflow (spec: `docs/specs/claims-workflow.md`, still a draft with unconfirmed assumptions)
- Statuses: New -> Triaged -> Assessing -> Approved or Declined -> Settled -> Closed; Reopened (reason required) from Settled or Closed, then Assessing. The legal moves are in `Services/ClaimWorkflow.cs`. Declined is final.
- Rules: the assessor never approves; approval is limited by the grade limit in `handler_grades` (the Head of Claims, role Manager, has none); Settled and Closed need a settlement amount. Illegal moves return 409, business-rule refusals 422, bad input 400, all with a message saying what is allowed.
- New calls: `PUT /api/claims/{id}/status` (the move), `POST /api/claims/{id}/reopen`, `GET /api/claims/{id}/next-actions`, `GET /api/claims/queue`, `GET /api/reports/limits-breaches`. UI: `/queue`, `/reports/limits-breaches`, and a Next steps panel on the claim page.
- The acting user is `changedByUserId` in the request, trusted without login. Refused approvals are saved in `claim_approval_attempts`.
- Time comes from an injected `TimeProvider`; never `DateTime.Now`. Claims carry `row_version`; a lost race gives 409.
- Tests: `dotnet test` in `backend/` runs 69 tests, 48 named `AC_nn_...` after the spec. Four need MySQL: set `HARBOURLINE_TEST_MYSQL_ADMIN` to an admin connection string (never commit it); `REQUIRE_MYSQL_TESTS=1` makes a missing one fail instead of skip.

## Known traps (do not rely on these being right)
- `ClaimRepository.NextSequenceAsync` uses `MAX(id)+1`, which can produce duplicate claim numbers under concurrent creates (not fixed: claim creation is outside the feature).
- The loss-ratio report counts one row per `Settled` history entry (ticket HLI-87, not yet fixed).
- `db-setup/01-create-users.sql` and `README.md` contain passwords. `compose.yaml` and `.github/workflows/ci.yml` are still broken.
- The bordereau importer has no tests yet and updates existing claims whatever their workflow state.
- In-memory tests cannot show MySQL behaviour (locks, deadlocks, column lengths, query counts); those need the MySQL tests.
- `ReportService` hard-codes its default report dates; the importer hard-codes 2026-10-01 as today.

## Database access for Claude (MCP)
- `.mcp.json` registers the `harbourline-db` stdio server (`mcp/`, build with `cd mcp && npm ci && npm run build`). It reads `HARBOURLINE_MCP_DSN` from the environment. Never commit that value.
- It reads five views (`mcp_claims`, `mcp_policies`, `mcp_policyholders`, `mcp_handlers`, `mcp_claim_history`), Midlands only, personal data masked. Tools: `claims_over_authority`, `ageing_by_stage`, `loss_ratio_by_product`, `reopened_claims`, `run_readonly_query`.
- Set up with `node mcp/scripts/run-sql.mjs db-setup/02-mcp-views.sql` then `.../03-create-mcp-user.sql` (needs `MYSQL_ADMIN_PASSWORD` and `MCP_DB_PASSWORD` in the environment). Design and limits: `docs/mcp-security.md`.
- For data questions use the `db-analyst` agent. Do not run SQL through the Bash tool.
- MCP tests: `cd mcp && npm test` (the MySQL part needs `HARBOURLINE_MCP_DSN`; CI sets `MCP_REQUIRE_DB=1`).

## Hooks (`.claude/settings.json`)
- Before Bash: `block-write-sql.sh` blocks write or DDL SQL and command-line passwords sent to a MySQL client. `mysql*` commands are also denied.
- Before Edit/Write: `block-secrets.sh` blocks passwords, URLs with passwords and private keys.
- After Edit/Write: `build-test.sh` builds and tests the part you changed (backend `.cs`, `mcp` `.ts`, frontend `.ts/.tsx`).
- After `git commit`: `update-claude-md.sh` refreshes the block below. Hook self-tests: `bash .claude/hooks/test-block-write-sql.sh` and `test-block-secrets.sh`.

## Recent changes
<!-- recent-commits:start -->
(filled in after each commit)
<!-- recent-commits:end -->
