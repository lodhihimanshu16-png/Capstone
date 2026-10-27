# Refactor note: the bordereau importer

## 1. What it was

`BordereauImportService.ImportAsync` was one 235-line method. It read the file, split the CSV by hand twice (header and rows, with different quote handling), validated 15 things in a fixed order, looked up the policy, overwrote the policyholder's email, updated or created the claim, chose a handler, and saved the batch. It had no tests.

## 2. Order of work

1. **Characterisation tests first**, written against the original code: `backend/Harbourline.Tests/ImporterCharacterisationTests.cs`, 52 tests (plus the existing `AC_47`). All 52 passed on the original importer before any change. Test file SHA-256 `137c89df…cbe59`.
2. **Then the refactor.** The test file was not touched afterwards. The same hash was checked after the refactor and again after the mutation checks.
3. Result: 52 of 52 and `AC_47` pass on the refactored code.

## 3. What I changed

| New unit | Holds | Why |
|---|---|---|
| `Imports/BordereauCsv.cs` | Reading non-blank lines, splitting the header, splitting a row, finding column positions | CSV handling had no reason to sit beside database code |
| `Imports/BordereauRowValidator.cs` | The checks that need no database, in the original order, returning the row or the error message | Pure, so it can be tested without a database |
| `Imports/BordereauImportService.cs` | Per-row decisions and saving: `ProcessRowAsync`, `UpdateContactEmailAsync`, `UpdateExistingClaimAsync`, `CreateClaimAsync`, `ChooseHandlerAsync`, `SaveBatchAsync`, plus a small `Tally` for counts and errors | Each step now has a name and fits on a screen |

The three places that built an `ImportBatch` (empty file, bad header, normal end) now use one `SaveBatchAsync`.

## 4. What I deliberately kept (odd behaviour, pinned by tests)

Each has a test named `Characterise_ODD_...` and a comment in the code. None is claimed to be right.

1. An unchanged re-import counts every row as **updated**.
2. The policyholder's email is overwritten **before** the claim checks, so it changes even when the row is then rejected (for example the claim is Closed).
3. "In the future" means after the fixed date **2026-10-01**, whatever today is.
4. A reference is marked "seen" **before** the row is fully validated, so a valid row after a rejected row with the same reference is called a duplicate.
5. Row numbers in errors count **non-blank** lines, not file lines.
6. Only Closed and Declined claims are protected. An Approved or Settled claim can still have its estimate changed by an import.
7. The header splitter drops doubled quotes and the row splitter keeps them (two different routines).
8. If nobody in the region can cover the amount, the limit rule is ignored and the first handler by name gets the claim.
9. Rows that succeed stay saved when later rows fail: there is no transaction.
10. An empty upload returns 400 with the message in `title` not `detail`, unlike every other error in the API.
11. A blank `source` is refused by model validation before the importer runs, so the `"Unknown"` fallback written in both the controller and the importer can never be reached.

## 5. What did change, and how I know

- **The importer now takes a `TimeProvider`** and uses it for its timestamps, where it used `DateTime.UtcNow`. This is a deliberate small change and is the only one outside the three files. The tests assert counts, messages, statuses, ids, handlers and text, not exact timestamps, so they do not prove the timestamps are the same. They are the same source of time in production (`TimeProvider.System`).
- **The updated-claim write** goes through the repository change made for the workflow (it now checks `row_version`), so an importer update also bumps the version. That came from the workflow work, not from this refactor, and is noted in the agent map.

How I know behaviour did not change:
- The 52 tests passed on the original and pass on the new code with the identical test file.
- **The tests can fail.** I broke the refactored code twice and ran them: moving the "future" date made `Characterise_ODD_LossDateAfter_2026_10_01_...` fail; stopping unchanged rows counting as updated made 3 tests fail. Both changes were reverted and 52 pass again.
- The tests reach the importer through the HTTP endpoint with a seeded database, so they cover wiring too.

## 6. Not covered

- The tests use the in-memory database, so nothing here shows MySQL behaviour (for example a failed row mid-file inside a real transaction, or handler counts under concurrent imports).
- I did not compare the original and refactored importers on the same large real file. The sample file and about 50 built cases are the evidence.
- Timestamps are not asserted exactly (section 5).
- Not fixed, and outside this task: claim numbers still come from `MAX(id)+1`, so two simultaneous imports could collide.
