# MCP server: permission design

The `harbourline-db` MCP server (in `mcp/`) lets Claude Code read the Harbourline database. This note says what each control is, what it stops, and what it cannot stop. The rule behind the design: **the database is the real boundary, and the server is a second line of defence.** Anything the server refuses must also be refused by MySQL when the server is bypassed. The tests check this.

## 1. The pieces

| Piece | Where |
|---|---|
| Server (stdio) and its five tools | `mcp/src/` (`server.ts`, `tools.ts`, `guard.ts`, `db.ts`) |
| Views and audit table | `db-setup/02-mcp-views.sql` |
| User and grants | `db-setup/03-create-mcp-user.sql` (run second) |
| Registration | `.mcp.json` (the connection string is `${HARBOURLINE_MCP_DSN}`, read from the environment) |
| Shell and file hooks, deny rules | `.claude/settings.json`, `.claude/hooks/` |
| Agent that may use only these tools | `.claude/agents/db-analyst.md` |
| Negative tests | `mcp/tests/guard.test.ts` (no database), `mcp/tests/negative.test.ts` (real MySQL) |

Tools: `claims_over_authority`, `ageing_by_stage`, `loss_ratio_by_product`, `reopened_claims`, and `run_readonly_query`. The four business tools run fixed, parameterised SQL. Only `run_readonly_query` takes SQL from the client, and that goes through the guard.

## 2. Controls: what each stops and what it cannot

| # | Control | Where | Stops | Cannot stop |
|---|---|---|---|---|
| 1 | The MCP user has `SELECT` on five `mcp_*` views and nothing else. No grant on any base table. | `03-create-mcp-user.sql` | Writes (INSERT, UPDATE, DELETE), DDL, reading any base table, reading `users` (emails and password hashes), `FILE` operations such as `INTO OUTFILE`. Refused by MySQL even if the guard is bypassed. | Anything a view exposes. A mistake in a view is a leak. |
| 2 | Personal data masked or left out **in the view SQL** | `02-mcp-views.sql` | Reading names, emails, phones, addresses, dates of birth, claim descriptions. Masked columns have different names (`email_masked`), so `SELECT email` fails with "unknown column". | Re-identification by combining what is left: age band, postcode area, product, dates and amounts together can point to one person. Free text in `claim_status_history.reason` is shown and could hold personal data. |
| 3 | Row scope: every view filters on `region_id = 2` (Midlands) | `02-mcp-views.sql` | Reading other regions through any view. The tests ask for NTH, LSE and SWW and get zero rows. | Cross-region handlers: a handler from another region who changed an in-scope claim appears as an id and grade. The scope is fixed in the view, so a different region needs a re-run of the script and a separate user. |
| 4 | Audit table `mcp_audit_log`: `INSERT` only | `02`, `03` | The MCP user reading, editing or deleting its own audit trail. | An administrator (root) editing it. Arguments are cut at 4,000 characters. Someone holding the password can write junk rows, bounded by control 6. |
| 5 | The server writes an audit row for **every** call, including refused ones. If the row cannot be written, the call is not answered (fails closed). | `tools.ts` `callTool` | Unrecorded use through the server. | Use of the MCP password directly, without the server. Such use appears in the MySQL logs, not here. |
| 6 | Account limits: 5,000 queries per hour, 3 simultaneous connections | `03-create-mcp-user.sql` | Floods and runaway loops. MySQL enforces them and the user cannot change them. | A slow trickle below the limit. |
| 7 | Timeout (`max_execution_time` 5 s) and row cap (`sql_select_limit` 501, result cut at 500) | Set by the server on every database session, and checked again in the server (driver timeout, result truncated, `truncated: true` returned) | A query that runs for minutes, and a response with thousands of rows. | MySQL has **no per-user** setting for these, so the database only enforces them on sessions the server opens. A client that uses the MCP password directly could remove them with `SET SESSION`. An explicit `LIMIT` larger than the cap beats `sql_select_limit`, so the server trims the result afterwards. This is fine for these small views and would need streaming for large ones. |
| 8 | Guard: one statement, must start with `SELECT` or `WITH`; no comments, variables, backslashes, `;` inside, `INTO`, `FOR UPDATE`, `SLEEP`, `LOAD_FILE`, or the `information_schema`, `mysql`, `performance_schema` and `sys` schemas; at most 4,000 characters | `guard.ts` | Writes, DDL, stacked statements, comment tricks, file writes, locking reads and sleep or benchmark abuse sent through `run_readonly_query`. | It is a word filter, not a SQL parser. It also refuses harmless uses of words such as `REPLACE()`, `INSERT()` and `TRUNCATE()`. It is meant to be too strict, not too loose. It does not decide what data is visible. Controls 1 to 3 do. |
| 9 | Driver: `multipleStatements: false` and a read-only session (`SET SESSION TRANSACTION READ ONLY`) | `db.ts` | A second statement slipping past the guard. A write attempted inside the query session. | Nothing beyond what the driver and MySQL enforce. |
| 10 | The connection string comes from `HARBOURLINE_MCP_DSN`. `.mcp.json` holds only `${HARBOURLINE_MCP_DSN}`. The password is a placeholder in the SQL (`${MCP_DB_PASSWORD}`) and is filled in when the script is run. | `.mcp.json`, `03-create-mcp-user.sql`, `mcp/scripts/run-sql.mjs` | The password being committed or submitted. | Anyone who can read the environment of the Claude Code process or the server process. |
| 11 | `permissions.deny` for `mysql`, `mysqldump`, `mysqlsh`, `mariadb`, plus a PreToolUse hook on Bash that blocks write or DDL SQL and command-line passwords sent to those clients | `.claude/settings.json`, `block-write-sql.sh` | Claude running a write through the MySQL command-line client by mistake or after a poor prompt. | A determined route round it. The hook looks only for the MySQL clients by name, so a script that uses a driver (such as Node) is not caught. Claude can still run such a script if credentials are in its environment. It is a guard against accidents, not a security boundary. The boundary is control 1. |
| 12 | A PreToolUse hook on Edit and Write that blocks passwords, connection strings with passwords, and private keys in text being written to files | `block-secrets.sh` | Typing a secret into a file in the repo. | A secret in an unusual format, or one passed in another way. |
| 13 | The `db-analyst` agent is limited to the five MCP tools. It has no shell and no file tools. | `.claude/agents/db-analyst.md` | The analyst agent changing anything or reading files. | Other agents and the main session have their own tools. Only the controls above limit them. |

## 3. What is proven, and how

`mcp/tests/negative.test.ts` runs against a real MySQL as the MCP user. For each attack it checks the server's answer **and**, with the guard bypassed, MySQL's own refusal.

| Must be refused | Server guard | MySQL itself |
|---|---|---|
| A write | `INSERT`, `UPDATE`, `DELETE` refused | `ER_TABLEACCESS_DENIED_ERROR` |
| DDL | `CREATE`, `DROP`, `ALTER`, `TRUNCATE` refused | Access denied |
| Multiple statements | `;` refused | The driver gives a parse error |
| A personal-data column | not applicable (a column filter is not the guard's job) | `full_name`, `email`, `phone`, `address_line`, `date_of_birth`, `postcode` do not exist in the view. The base table is refused. Masked values contain `***`. |
| An out-of-scope region | not applicable | Every view returns only `MID`. NTH, LSE and SWW return zero rows. The base `claims` table is refused. |
| `INTO OUTFILE` and `INTO DUMPFILE` | refused | `ER_SPECIFIC_ACCESS_DENIED_ERROR` (needs the `FILE` privilege) |
| Reading the users table | refused | `ER_TABLEACCESS_DENIED_ERROR` |
| Reading, editing or deleting the audit log | not applicable | Refused. Insert works. |
| A runaway query and a huge result | cap and timeout in the server | Session variables are set on the server's connection |

`mcp/tests/guard.test.ts` has 47 further cases for the guard alone (allowed and refused). Run `cd mcp && npm test`. The MySQL part needs `HARBOURLINE_MCP_DSN`. CI sets `MCP_REQUIRE_DB=1`, so a missing database fails the job instead of skipping the tests.

## 4. Known limits, plainly

- The views are the privacy boundary. They were written by hand and reviewed once. A new view or column needs the same review.
- Region scope is one region per MCP user. Whole-book figures (such as the Finance comparison in ticket HLI-87) cannot be reproduced through this server. They need an administrator.
- Timeout and row cap depend on the server being the only client of the MCP account. If the password leaks, those two controls can be removed by the holder. Controls 1 to 4 and 6 still hold.
- Deny rules and hooks are advisory for anyone who can run arbitrary code in the session.
- The administrator password was used on this machine only to run `db-setup/` and to prepare tests. It is not in any file in the repo. The MCP password is generated, kept in `~/.harbourline/mcp.env` outside the repo, and never printed.

## 5. How to set it up

```bash
# once, as a database administrator (the admin password is in the environment, not in a file)
export MYSQL_ADMIN_PASSWORD=...        # the admin password
export MCP_DB_PASSWORD=...             # a new password for harbourline_mcp
node mcp/scripts/run-sql.mjs db-setup/02-mcp-views.sql
node mcp/scripts/run-sql.mjs db-setup/03-create-mcp-user.sql
# then, for Claude Code
export HARBOURLINE_MCP_DSN="mysql://harbourline_mcp:<password>@127.0.0.1:3306/harbourline"
cd mcp && npm ci && npm run build
```
Start Claude Code from the track folder so it reads `.mcp.json`.
