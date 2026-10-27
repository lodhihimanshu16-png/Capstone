---
name: add-migration
description: Add a new SQL migration to the Harbourline database and keep the EF model in step. Use when a schema or seed change is needed.
---

# Add a Harbourline migration

1. List `backend/db/migrations/` and take the next version number (`V006`, `V007`, ...). Name the file `V00N__what_it_does.sql`. Never edit an existing file.
2. Write the SQL in the style of `V001__create_schema.sql`: InnoDB, utf8mb4, snake_case, named keys (`ix_`, `uq_`, `fk_`), `DECIMAL(12,2)` for money, `DATETIME` for UTC timestamps. New NOT NULL columns on existing tables need a DEFAULT.
3. Update the entity in `backend/Harbourline.Api/Domain/Entities.cs` and, if a length, index or relation is needed, `Data/HarbourlineDbContext.cs`. The EF type, length and nullability must match the SQL exactly.
4. The migration is an embedded resource (`Harbourline.Api.csproj` includes `..\db\migrations\*.sql`), so no project edit is needed.
5. Prove it: start MySQL, run `dotnet run --project Harbourline.Api` from `backend/` and confirm the log shows the script applied. Then check with `DESCRIBE <table>` through the MCP db-analyst agent.
6. In-memory tests will not catch a mismatch. Add or run a MySQL-backed test when the change affects a constraint, index or column length.
7. Mention the new migration in the PR or in `docs/perf-note.md` if it is a performance or concurrency fix.
