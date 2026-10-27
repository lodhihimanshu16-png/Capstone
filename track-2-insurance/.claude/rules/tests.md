---
paths:
  - "backend/Harbourline.Tests/**"
  - "mcp/tests/**"
---

# Test rules (Harbourline)

- Name a test after the acceptance criterion it proves: `AC_04_AssessorCannotApprove`. The criterion lives in `docs/specs/claims-workflow.md`. Use the same id in the spec's Traceability table.
- A test must assert what its name says. Check the assertion, not just that the test passes.
- No `DateTime.Now`, `DateTime.UtcNow` or `DateOnly.FromDateTime(DateTime.Now)` in a test or in code under test. Pass a fixed clock or fixed dates so the test passes on any day.
- Tests must not depend on run order or shared state. `ApiFactory` creates a fresh in-memory database per factory instance.
- The in-memory provider does not enforce SQL types, lengths, unique indexes or concurrency. Anything about those (N+1, row versions, duplicate claim numbers) needs a test against real MySQL or a check of the EF SQL log.
- Service tests use Moq for repositories. Build the service the way `ClaimServiceTests.Build()` does.
- A regression test for a bug must fail on the original code. Run it before the fix and record that in the debug report.
