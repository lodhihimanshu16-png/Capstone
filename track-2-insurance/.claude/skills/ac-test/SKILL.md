---
name: ac-test
description: Turn an acceptance criterion from docs/specs/claims-workflow.md into an xUnit test named after it. Use when writing or checking AC tests.
---

# Write a test for an acceptance criterion

Input: an AC id such as `AC-04` from `docs/specs/claims-workflow.md`.

1. Read the criterion (Given / When / Then) and the business rule it comes from.
2. Name the test `AC_04_<WhatItProves>` in PascalCase, for example `AC_04_AssessorCannotApprove`. One criterion maps to one or more tests. Every test name starts with its AC id.
3. Arrange the Given with fixed data. Use fixed dates and an injected clock. Never use `DateTime.Now`.
4. Act with the When: call the service method (Moq repositories, as in `ClaimServiceTests.Build()` in `backend/Harbourline.Tests/UnitTests.cs`) or the HTTP endpoint (`ApiFactory`).
5. Assert the Then. For a refusal, assert the exception type or the HTTP status (`422` or `409`) and that the message names what is allowed. Assert nothing changed (status, amounts, history count).
6. Do not assert something weaker than the name says. A test called "cannot skip two statuses" must try to skip two statuses.
7. Add the test name to the Traceability table in the spec (`AC` -> `Test` column).
8. Run `dotnet test` in `backend/` and confirm the test fails first when the rule is missing, then passes.
