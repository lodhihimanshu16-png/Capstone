---
paths:
  - "backend/db/migrations/**"
  - "db-setup/**"
---

# SQL migration rules (Harbourline)

- Migrations are plain SQL files named `V00N__short_name.sql` in `backend/db/migrations/`. The next free number after `V005` is `V006`. DbUp runs them in name order at API start and records them, so a file that has already run is never re-run.
- **Never edit an existing migration.** Every schema or seed change is a new file.
- Keep SQL equal to the EF model in `Data/HarbourlineDbContext.cs`: the same column type, length and nullability. Money is `DECIMAL(12,2)`, never `FLOAT` or `DOUBLE`. Timestamps are `DATETIME` holding UTC. Dates without a time are `DATE`.
- Column names are snake_case. EF maps `ClaimNumber` to `claim_number` by convention, so a new C# property needs a matching snake_case column.
- Name every key and index (`ix_`, `uq_`, `fk_` prefixes, as in `V001`). Add an index for any column you filter or join on.
- Make a migration safe to run on a populated database: add nullable columns or give new NOT NULL columns a default.
- Files in `db-setup/` are run by hand (or by CI) and are not DbUp migrations. `02-mcp-views.sql` runs before `03-create-mcp-user.sql`. Never put a real password in either; take it from an environment variable.
