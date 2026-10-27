# Prompt journal

Written as I work. Each entry: the prompt, the Claude Code mode, what came back, and what I did with it.

## 1. Explore and trace (Task 1)
- **Prompt:** "Explore this repo without editing anything. Describe the architecture layer by layer, with file paths. Then trace `POST /api/claims/{id}/status` from the React page to the MySQL tables and back. List every place where CLAUDE.md disagrees with the code."
- **Mode:** plan
- **Came back:** a layer table, two traces, a 14-row table of CLAUDE.md errors and extra risks (password hashes in `/api/users`, a note-length mismatch).
- **Decision:** accepted, with one correction. The prompt said `POST`, but the endpoint is `PUT /api/claims/{id}/status`. The React app has no status-change call at all, so the brief traces two paths: the read the browser does, and the write through the API. I also confirmed the unverified parts (React 19 in `package.json`, test count of 20) before using them.

## 2. Rewrite CLAUDE.md (Task 1)
- **Prompt:** "Rewrite CLAUDE.md so every line is true for this repo: stack, commands I can run, conventions you can see in the code, and known traps. Remove anything you cannot prove from a file."
- **Mode:** normal
- **Came back:** a new `CLAUDE.md` built from the code. It dropped MediatR, Dapper, MongoDB, JWT, Next.js, Redis and the `double` and local-time rules.
- **Decision:** accepted. I kept the "ask Priya before adding NuGet packages" line because I cannot prove it false, and marked it for the mentor to confirm. I checked the `ReserveReviewDue` DTO field and the seed-data range before keeping the statements about them.

## 3. Rules, skills, agents and first hook (Task 1)
- **Prompt:** "Create path-scoped rules for migrations and tests, two skills (`add-migration`, `ac-test`), four agents (backend, frontend, test, reviewer) and a PreToolUse hook that blocks write SQL and secrets, all specific to this repo."
- **Mode:** normal
- **Came back:** `.claude/rules/migrations.md` and `tests.md`, two skills, four agents, `hooks/block-write-sql.sh` and `settings.json` with deny rules.
- **Decision:** accepted for now. The db-analyst agent is deferred until the MCP server exists, so it can name the real tools.
- **Verification:** my first attempt to test the hook was blocked by the hook itself, because the test command contained `mysql` and `DROP`. I moved the cases into `.claude/hooks/test-block-write-sql.sh`, which passes all 11 (7 blocked, 4 allowed). I also confirmed the `mysql` deny rule works, because it denied one of my own commands.

## 4. Fix a hook that blocked normal work (Task 1 and 2)
- **Prompt:** "Create the mcp project and install its dependencies" (a plain `npm install`), and later, appending notes to this journal with a heredoc.
- **Mode:** normal
- **Came back:** my own `block-write-sql.sh` blocked both. First it searched the whole tool-call JSON, so the word "Create" in the description counted as DDL, and `mysql` matched inside the npm package `mysql2`. After a first fix, it still blocked a heredoc whose prose mentioned MySQL and "Create".
- **Decision:** rejected word-matching. I rewrote the hook (`block-write-sql.mjs`) to parse the shell command and look for a MySQL client only where a command runs: at the start of a command, after `docker exec`, `sudo` or `env`, or inside `bash -c`. Heredoc bodies and quoted prose are ignored. The self-test now has 24 cases (13 blocked, 11 allowed), and every false positive I hit is a permanent case. The first two versions were wrong, and the self-test script is what exposed them.

## 5. Design the MCP views and user (Task 2)
- **Prompt:** "Write `db-setup/02-mcp-views.sql` and `03-create-mcp-user.sql`: five views scoped to one region, personal data masked in the view SQL, an INSERT-only audit table, a user with SELECT on the views only, and no password in the file."
- **Mode:** normal
- **Came back:** five views (claims, policies, policyholders, handlers, claim history) scoped to Midlands (region 2), the audit table, and a user script that fills the password from `${MCP_DB_PASSWORD}`.
- **Decision:** accepted. I chose Midlands because it has the most claims (52) and seven claims that were settled twice. The brief does not name a region. I left `description` and `external_ref` out of the claims view because free text may hold personal data. MySQL has no per-user timeout or row cap, so the server sets them as session variables. I wrote that limit into `docs/mcp-security.md` rather than claiming the database enforces it for the account.

## 6. Build the MCP server and its guard (Task 2)
- **Prompt:** "Build `mcp/`: the four business tools and `run_readonly_query` with a guard (one statement, SELECT or WITH only, no comments, no INTO, no variables), connection string from `HARBOURLINE_MCP_DSN`, an audit row for every call that fails closed."
- **Mode:** agent-style (written in one pass, then checked)
- **Came back:** `guard.ts`, `db.ts`, `tools.ts`, `server.ts`. The first build had one type error in `server.ts`, fixed with a typed wrapper.
- **Decision:** accepted after checking. I drove the built server over real stdio with the SDK client (`mcp/scripts/call-tool.mjs`): the five tools were listed, `loss_ratio_by_product` returned Midlands rows, and a `DELETE` was refused.

## 7. Negative tests (Task 2)
- **Prompt:** "Write negative tests that prove a write, DDL, multiple statements, a personal-data column, an out-of-scope region and INTO OUTFILE are refused, both by the guard and by MySQL with the guard bypassed."
- **Mode:** normal
- **Came back:** 47 guard unit tests and 39 MySQL tests. One failed on the first run: I expected `ER_ACCESS_DENIED_ERROR` for `INTO OUTFILE`, but MySQL returns `ER_SPECIFIC_ACCESS_DENIED_ERROR` ("you need the FILE privilege").
- **Decision:** the control was right and my expectation was wrong. I fixed the test and made it stricter (exact code plus the word FILE in the message). All 86 pass. I also made the suite fail instead of skip when CI sets `MCP_REQUIRE_DB`, so a missing database cannot pass silently.

## 8. Secrets hook and the other two hooks (Task 1 and 2)
- **Prompt:** "Add a hook that blocks secrets in file writes, one that builds and tests after edits, and one that refreshes CLAUDE.md after a commit."
- **Mode:** normal
- **Came back:** `block-secrets.sh`, `build-test.sh`, `update-claude-md.sh`, all wired in `settings.json`. The secrets hook then blocked my own documentation, because `Password=` matched inside `MYSQL_ADMIN_PASSWORD=...`. It also blocked a test fixture holding a fake password flag.
- **Decision:** tightened the pattern so `Password=` must stand alone (12 cases pass). For the fixture I built the fake flag from two string pieces, and did not weaken the hook. The build-and-test and CLAUDE.md hooks have not yet run on a real edit and commit, so I treat them as unverified until they do.

## Note: a refused action
While checking the HLI-87 numbers I ran an exploratory SQL query as the database administrator, and Claude Code's permission classifier denied it. I did not work round it. The numbers will come from the restricted MCP user, which is how the task intends it.

## 9. Draft the spec with unconfirmed assumptions (Task 3)
- **Prompt:** "Draft the spec with my recommended assumptions marked unconfirmed." (I had no stakeholder replies yet.)
- **Mode:** plan, then normal
- **Came back:** `docs/specs/claims-workflow.md`: 24 business rules, six endpoints with error codes, two screens plus a claim-detail panel, 48 acceptance criteria each with a named test, 16 findings on the brief, 10 questions, 20 assumptions, a traceability table and an AC-to-test index.
- **Decision:** accepted as a draft. Before writing I read the seed users and handlers, which showed that South West & Wales has two Claims Assistants (the brief says an Assistant and a Handler), and that the role names are Admin, Manager, Handler and ReadOnly. I left every stakeholder reply blank rather than invent one. The traceability table first abbreviated test names with "…", so I generated a full AC-to-test index from the spec and checked it: 48 criteria, no duplicates.
- **Still open:** the spec is not approved. Answers to Q-01 to Q-10 may change BR-06, BR-11 and BR-18, so the build should wait for them or proceed knowing those three may move.

## 10. Plan the parallel build and fix the contract first (Task 4)
- **Prompt:** "Build it." Followed by my own briefs to three agents: backend, test and frontend, each told which files it owns and which it must not touch, with the interface contract written into `docs/agent-map.md` first.
- **Mode:** agent (three subagents in parallel)
- **Came back:** a backend with migration V006, the workflow rules and the new endpoints; 48 AC-named tests; two new screens and a next-steps panel.
- **Decision:** accepted the work, not the claims. Before dispatch I paused the build-after-edit hook (`.claude/.hooks-paused`), because three agents building the same projects at once would collide, and I turned it back on afterwards. I also told the test agent not to compile, so it wrote the tests against the contract only. That made the first compile the real check.

## 11. First compile and test run (Task 4 and 8)
- **Prompt:** (mine) "Compile everything together and run the whole suite."
- **Mode:** normal
- **Came back:** a clean build, then 44 of 69 tests failing with one error: EF Core's "more than twenty service providers" guard, caused by one factory and database per test.
- **Decision:** the fault was in the test setup, not the feature. I added that warning to the ignored list in `ApiFactory` with a comment saying why. Result: 65 pass, 4 skipped (the MySQL tests). I do not count "clean build, nothing failing" as proof, because the tests had never run against the code.

## 12. Real MySQL found a real bug; mutation checks (Task 4 and 8)
- **Prompt:** (mine) "Run the four MySQL tests against the real server with REQUIRE_MYSQL_TESTS=1."
- **Mode:** normal
- **Came back:** three pass, but `AC_33_ConcurrentApprovalsOnlyOneSucceeds` failed with a 500 on one of two simultaneous approvals. It passed once by luck, then failed 10 of 10. The cause was a MySQL deadlock ("Deadlock found when trying to get lock"), which the in-memory tests cannot show.
- **Decision:** rejected "retry until green". I treat a deadlock or lock timeout on this save as the same 409 as a version conflict (nothing is half written). It now passes 10 of 10. Then, because the tests were written blind, I broke three rules one at a time (assessor rule off; limit made exclusive; overdue made inclusive). The matching tests failed each time (`AC_09/10/30`, `AC_12`, `AC_36`) and the files were restored byte for byte.
- **Also corrected:** the backend agent flagged that Triaged claims were invisible in caseload, workload and ageing. That came from my own brief ("leave `ClaimStatuses.Open` unchanged"), so I fixed `Open` and recorded the mistake in `docs/agent-map.md`.
- **Measured:** the list endpoint went from 491 SQL statements to 2 at 100 rows (0.41 s to 0.010 s). See `docs/perf-note.md`.

## 13. Investigate and fix HLI-87 (Task 5)
- **Prompt:** (mine) "Using only the MCP tools, show the wrong numbers for HLI-87. Test the explanation you would try first. Write a regression test that fails on the original code, then fix it, and compare with the Finance table."
- **Mode:** plan, then normal
- **Came back:** seven claims settled more than once in the Midlands, found through `run_readonly_query`. My first hypothesis was that the paid-amount formula was wrong. It looked half true (18 of 25 claims matched approved minus excess), so I did not accept or reject it from the summary. I listed the 7 that did not match, and they were exactly the 7 re-settled claims. For every claim settled once the formula holds, and an amount formula cannot explain a count of 11 against 8, so I discarded it.
- **Decision:** accepted the root cause (the report joined on history rows) after reproducing the ticket's numbers exactly from the running API. My first regression test failed on the original code for the wrong reason: a helper never called `SaveChanges`, so even the single-settled control counted 0. I caught it because the control failed too. After fixing the test it failed with the right numbers (expected 2 claims, got 3), then passed after the fix. Whole book now 62,946.35 over 84 claims at 34.9%, matching Finance; all four ticket products match.
- **Unexplained:** one full run had all four MySQL tests fail together and I did not capture why. They pass in seven later runs. Recorded as open in `docs/debug-report.md`, not as fixed.
- **Hook:** the build hook ran on my edits. It failed once because the API I had started was locking `Harbourline.Api.exe`, which was my own mistake, and it also confirmed the hook fails on a broken build.

## 14. Characterisation tests, then refactor the importer (Task 6)
- **Prompt:** (mine) "Write characterisation tests for the bordereau importer that pin what it does today, including odd behaviour. Then refactor it without changing a test."
- **Mode:** normal
- **Came back:** 52 tests. Two failed on the first run because my assumptions were wrong, not the code: a blank `source` never reaches the importer (model validation returns 400 first), and the empty-file message is in `title`, not `detail`. I changed the tests to describe what the code really does and recorded both as odd behaviour.
- **Decision:** accepted. Before refactoring I recorded the test file's SHA-256, then split the 235-line method into a CSV reader, a pure row validator and a slimmer service. All 52 passed on the original and on the refactor with the identical file. I then broke the new code twice (moved the hard-coded date; stopped counting unchanged re-imports) and the right tests failed both times.
- **Disclosed:** the importer now takes a `TimeProvider`, a small deliberate change the tests do not prove (they do not assert exact timestamps).
