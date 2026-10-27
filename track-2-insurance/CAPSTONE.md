# Capstone: Inherit It, Specify It, Build It, Ship It

Time allowed: 2 days. Five tracks, one shared brief. Pick your track (or be given one), then read this file from top to bottom before you open any code.

## 1. The situation

You have joined a company as the new engineer. It runs an internal application that works. It builds, it starts, its tests pass and people use it every day. The person who wrote it has left, nobody else owns it, and nobody has looked after it for a while.

A senior person in the business has now asked for a new feature. They have written down what they want in their own words. They are not an engineer, so their note is not a specification.

Your track's `README.md` tells you which company you are working for, who your stakeholder is, and what the system does. Everything below applies to all five tracks.

## 2. What you are inheriting

The technical stack is the same in every track:

- **Backend:** ASP.NET Core 8 Web API, in layers (controllers, services, repositories), with EF Core and MySQL.
- **Database changes:** plain SQL migration files applied by DbUp when the API starts.
- **Backend tests:** xUnit and Moq. The test suite is thin on purpose. It does not protect you much.
- **Frontend:** React 19 with TypeScript and Vite.
- **Data:** the data is loaded by the migrations, so you get a realistic database as soon as the API has started once.

The code works, but it also carries the things a codebase with no owner tends to carry. Finding those is part of the work. They are not all labelled.

## 3. What you must build

The feature in your track's `docs/stakeholder-brief.md`. Read it as a stakeholder's note, not as a spec: it will not be complete, and it will not always agree with itself. Your job is to decide what is sensible, ask about what is not clear, and build only what you can defend.

Also in your track folder:

- `DELIVERABLES.md` lists every file you must hand in, at its exact path.
- `docs/` holds the stakeholder brief, a ticket about a wrong number in a report, a spec template, a review checklist, a CI log and a sample import file.

## 4. How you must work

**Spec first.** You write a spec before you write feature code, and the spec is assessed on its own. Use `docs/specs/_template.md`. It must contain:

- Acceptance criteria numbered `AC-01`, `AC-02`, and so on, each written as Given / When / Then.
- An **Assumptions & Open Questions** section. If it is empty, the spec fails.
- A **Traceability** table that links every acceptance criterion to the test that proves it.

**Read the brief critically.** It has gaps. It contains statements that contradict each other. It asks for something that cannot be built in this codebase as it stands. It asks for something that must not be built at all. Your spec has to say so, plainly, for each one: what you found, what you recommend, and what you need the stakeholder to decide.

**Your mentor plays the stakeholder.** You may ask questions at any time, as you would ask a real stakeholder. Ask in plain business words. Write down the question and the reply in your spec. Expect the stakeholder to be busy, to give short replies and to not know the technical detail.

**Keep a prompt journal as you go.** Write `docs/prompt-journal.md` while you work, not at the end. You need at least 15 entries. Each entry has:

- the prompt you gave Claude Code;
- the Claude Code mode you used: plan, normal, auto-accept, or agent;
- what came back;
- whether you accepted it, rejected it or rewrote it, and why.

**Do not build more than the brief asks for.** There are no marks for extra features.

## 5. Getting started

Follow your track's `README.md`.

## 6. The tasks

Each task is assessed on its own. Do them in this order. Some work from one task is used by the next.

### Task 1: Onboard and set up Claude Code

- Write `docs/onboarding-brief.md`: the architecture in your own words, plus one request traced from the browser to the database and back.
- Make `CLAUDE.md` true. It must describe this repo, as it really is.
- Add at least one path-scoped rule in `.claude/rules/`, or a nested `CLAUDE.md`.
- Add at least 2 skills, at least 5 agents (backend, frontend, test, reviewer, db-analyst) and a first hook.
- Everything you add must be specific to this repo, not copied from a template.

### Task 2: Build an MCP server to MySQL (mandatory)

Build a stdio MCP server in `mcp/` that lets Claude Code read your database safely. The point of this task is the permission design. A server that works but can read everything fails.

The database side:

- Create a **dedicated MySQL user** for it, in `db-setup/03-create-mcp-user.sql`. The scripts must run in order: `db-setup/02-mcp-views.sql` first (the views), then `db-setup/03-create-mcp-user.sql` (the user and its grants).
- The user has **SELECT on views only**. No grants on base tables.
- **Personal data is masked** in the views, not in the server.
- **Row-level scope:** the user sees one region (or branch, or hotel) only. The view applies the scope.
- **Audit log:** every call is recorded in a separate table, and the MCP user can only INSERT into it.
- The user has **no access** to the users or credentials tables.
- A **timeout and a row cap**, set in the database and also enforced in the server.

The server side:

- At least **4 business tools** named in your track's `DELIVERABLES.md`, plus a **guarded `run_readonly_query`** tool.
- The guard refuses anything that is not a single read-only statement.
- The connection string comes from an **environment variable**. It is not committed.
- The server is registered in `.mcp.json`.

Around the server:

- A `permissions` deny rule or a hook that **blocks write SQL through the Bash tool**.
- **Negative tests** in `mcp/tests/` that prove each of these is refused: a write, DDL, multiple statements, a personal-data column, an out-of-scope region and `INTO OUTFILE`.
- `docs/mcp-security.md`: the permission design, what each control stops, and what it cannot stop.
- A **db-analyst agent** in `.claude/agents/` that may use only the MCP tools.

### Task 3: Brief to spec

Write `docs/specs/<feature>.md` from the stakeholder brief, using the template. See section 4 for what it must contain.

### Task 4: Build the feature backend

- Write business rules, not just fields. A status machine has legal and illegal moves.
- An illegal move returns a clear 4xx error that says what is allowed.
- One call tells the user "when, or what next" for a single record. Your track's `DELIVERABLES.md` names it.
- The feature must be built by **at least 3 agents working in parallel** (for example backend, frontend and tests). Record who built what in `docs/agent-map.md`.
- Concurrency, performance, schema and test-reliability problems exist in this codebase. Find them. Fix the ones that your feature touches, prove each fix with a test or a measurement, and write the evidence in `docs/perf-note.md` (before and after).

### Task 5: Fix the ticket

Your track has a ticket about a report that shows the wrong numbers.

- Use your MCP server to show the wrong numbers in the data.
- Find the root cause.
- Add a regression test that fails on the original code.
- Write `docs/debug-report.md`, including the explanation you tried first and threw away.

### Task 6: Refactor the importer

- Write characterisation tests first. They pin down what the importer does today, including its odd behaviour.
- Then refactor it.
- Write `docs/refactor-note.md`: what you changed, what you chose to keep, and how you know behaviour did not change.

### Task 7: Build the feature UI

- Follow the conventions the existing pages use.
- Every screen has loading, empty and error states.
- Validation in the form matches the validation in the backend.

### Task 8: Tests

- Write tests named after the acceptance criteria, for example `AC_04_CannotSkipTwoStatuses`. At least 25.
- Find the test that passes while checking the wrong thing, and fix it.
- Make every test reliable: it must pass on any day and at any time.

### Task 9: Review, harden and ship

- Run a **reviewer agent** and a **security agent in parallel** against `docs/review-checklist.md`. Write `docs/review-report.md`.
- Fix what they find that is real. Say so in the report where you decide not to.
- Fix the pipeline in `.github/workflows/ci.yml` using the log in `docs/ci-failing-run.log`, and add a job that runs the MCP negative tests.
- Fix `compose.yaml` so it works.
- Update `README.md`.
- Finish with `docs/ai-usage-reflection.md`: one page, written last, about where Claude Code helped, where it was wrong, and what you would do differently.

### Hooks and orchestration (across the tasks)

- **Hooks** in `.claude/settings.json`: one that blocks write SQL and secrets before a tool runs; one that runs the build and tests after edits; one that updates `CLAUDE.md` after each commit.
- **Orchestration:** at least 3 agents on the feature (Task 4), and a reviewer and a security agent in parallel (Task 9). `docs/agent-map.md` shows which agent did what and what you had to correct.

## 7. What you submit and where

Paths are relative to your track folder. `DELIVERABLES.md` in your track has the full checklist with the feature-specific items.

| What | Where |
|---|---|
| Onboarding brief | `docs/onboarding-brief.md` |
| Spec | `docs/specs/<feature>.md` |
| Prompt journal (15 entries or more) | `docs/prompt-journal.md` |
| MCP permission design | `docs/mcp-security.md` |
| Agent map | `docs/agent-map.md` |
| Ticket investigation | `docs/debug-report.md` |
| Importer refactor note | `docs/refactor-note.md` |
| Performance and concurrency note | `docs/perf-note.md` |
| Review report | `docs/review-report.md` |
| AI usage reflection (one page) | `docs/ai-usage-reflection.md` |
| Project memory | `CLAUDE.md` |
| Scoped rules | `.claude/rules/*` or a nested `CLAUDE.md` |
| Skills (2 or more) | `.claude/skills/` |
| Agents (5 or more) | `.claude/agents/` |
| Hooks and permissions | `.claude/settings.json` |
| MCP registration | `.mcp.json` |
| MCP server and its negative tests | `mcp/`, `mcp/tests/` |
| MCP database scripts (run in this order) | `db-setup/02-mcp-views.sql`, then `db-setup/03-create-mcp-user.sql` |
| Pipeline | `.github/workflows/ci.yml` |
| Compose file | `compose.yaml` |
| Run instructions | `README.md` |
| Backend code and tests | `backend/` |
| Frontend code | `frontend/` |

Done means: all tests pass (at least 25 are named after acceptance criteria), the MCP negative tests pass, the CI steps pass when you run them locally, every issue you found is fixed or explained in your docs, and every item in `DELIVERABLES.md` is ticked.

## 8. Submitting

Either push a branch, or zip your track folder. Do not include `node_modules`, `bin`, `obj` or `dist`. Name the zip `capstone-<name>-track<N>.zip`, for example `capstone-asha-track3.zip`.

Do not commit passwords, keys or connection strings. If your own MCP password is in a file you submit, you have lost marks in Task 2.
