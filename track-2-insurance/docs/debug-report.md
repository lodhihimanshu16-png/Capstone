# Debug report: HLI-87, loss ratio differs from Finance

Ticket: `docs/bug-HLI-87.md`. Status: **fixed**, with a regression test that fails on the original code.

## 1. Summary

The loss-ratio report counted one row for every `Settled` entry in `claim_status_history`. A claim that was settled, reopened and settled again has several such rows, so the report counted it, and added its paid amount, once for each. Finance counts each paid claim once. After the fix, all four products in the ticket and the whole book match Finance to the penny.

## 2. Reproducing the wrong numbers

**From the API** (`GET /api/reports/loss-ratio?from=2025-10-01&to=2026-09-30`, original logic, local MySQL with the seed data). These are the ticket's "What the report shows" figures exactly:

| Product | Premium | Paid (report) | Settlements (report) |
|---|---:|---:|---:|
| Motor Third Party Fire & Theft | 14,107.04 | 10,706.17 | 11 |
| Home Basic | 12,650.40 | 11,554.79 | 14 |
| Classic Car | 5,118.25 | 3,790.03 | 6 |
| Motor Comprehensive | 79,165.23 | 36,516.48 | 31 |
| Whole book | 180,339.57 | 84,426.57 | 99 (46.8%) |

**From the data, through my MCP server** (`run_readonly_query`, scope: Midlands only, so these are not whole-book figures). Claims with more than one `Settled` history row in the period:

| Claim | Product | Paid | Reopened | Settled rows |
|---|---|---:|---:|---:|
| HLC-26-00044 | MOT-TPFT | 2,625.00 | 2 | 3 |
| HLC-26-00036 | HOME-CNT | 1,325.97 | 1 | 2 |
| HLC-26-00071 | HOME-PRM | 684.64 | 1 | 2 |
| HLC-26-00081 | MOT-COMP | 2,028.80 | 1 | 2 |
| HLC-26-00086 | HOME-PRM | 805.47 | 1 | 2 |
| HLC-26-00105 | MOT-CLS | 1,133.55 | 1 | 2 |
| HLC-26-00115 | MOT-CLS | 647.79 | 1 | 2 |

Midlands, per product, counting the way the report does (one per `Settled` row) against each claim once:

| Product | Settlements as reported | Claims actually paid | Paid as reported | Paid, each claim once |
|---|---:|---:|---:|---:|
| MOT-TPFT | 5 | 3 | 8,659.44 | 3,409.44 |
| HOME-PRM | 5 | 3 | 3,287.16 | 1,797.05 |
| MOT-CLS | 4 | 2 | 3,562.68 | 1,781.34 |
| HOME-CNT | 3 | 2 | 2,923.48 | 1,597.51 |
| MOT-COMP | 11 | 10 | 5,941.26 | 3,912.46 |
| HOME-BAS, HOME-STD, MOT-TPO, MOT-VAN | equal | equal | equal | equal |

This also explains the ticket's remark that "some products are barely affected (Motor Van, Landlord Home match exactly)": only products with re-settled claims are out. For MOT-TPFT, the difference of 5,250.00 is exactly two extra counts of HLC-26-00044's 2,625.00.

## 3. The explanation I tried first and threw away

**First hypothesis: the paid amounts themselves are wrong.** `paid_amount` is set when a claim is settled (approved amount minus the excess), and the ticket says only the paid column differs. So I suspected the amount calculation, for example the excess being deducted wrongly.

**Test:** for every settled or closed claim in scope, is `paid_amount` equal to approved amount minus excess?

Result: 18 of 25 equal. At first that looked like partial support. I then listed the 7 that did not. **They are exactly the 7 claims settled more than once** (table above). For every claim settled once, the formula holds. So the formula is not the problem.

**Why I discarded it:** (a) the formula is right for every single-settled claim, and (b) the ticket's *count* is also wrong (11 settlements against 8 claims). An amount formula cannot change a count. Only counting the same claim more than once can explain both.

## 4. Root cause

`ReportRepository.LossRatioAsync` (`backend/Harbourline.Api/Repositories/Repositories.cs`) built its list of settlements like this:

```
from h in ClaimStatusHistory
join c in Claims on h.ClaimId equals c.Id
join p in Policies on c.PolicyId equals p.Id
where h.ToStatus == "Settled" and h.ChangedAt in the period
select { p.ProductId, Paid = c.PaidAmount }
```

It joined on the history **rows**, so a claim with two `Settled` rows produced two result rows, each carrying the claim's current `paid_amount`. The sums and the count both doubled up. The premium column was never involved, which is why it matched Finance.

**Why the existing test passed:** `LossRatioCalculatorTests` only tests the percentage helper (`paid / premium * 100`) with numbers it supplies. It never touches the repository query where the fault is.

## 5. The fix

Select the claims that have at least one `Settled` row in the period, and join on those claims:

```
settledInPeriod = ClaimStatusHistory where ToStatus == "Settled" and in period, select ClaimId
from c in Claims join p in Policies where settledInPeriod contains c.Id
```

Each claim now appears once, so its paid amount is added once and it is counted once.

## 6. Regression test

`ReportRegressionTests.HLI_87_ClaimSettledTwiceIsCountedOnceInLossRatio` builds one claim settled once (100.00), one claim settled, reopened and settled again (400.00, two `Settled` rows) and one control claim whose only settlement is before the period. It expects 2 claims and 500.00.

- **On the original code it fails**: expected 2 claims, got 3 settlements.
- After the fix it passes. A companion test, `HLI_87_ClaimSettledOnceIsStillCountedOnce`, passes both before and after.

**A mistake on the way:** my first version of this test failed on the original code for the wrong reason. The helper that adds the policy never saved it, so every claim dropped out of the report and even the single-settled control counted 0. I noticed because the control failed too. After adding the missing `SaveChanges`, the control passed and only the double-settled test failed, with the right numbers.

## 7. Checked against Finance

After the fix, `GET /api/reports/loss-ratio?from=2025-10-01&to=2026-09-30` on the seed data:

| Product | Paid | Claims | Ratio | Finance paid | Finance claims | Match |
|---|---:|---:|---:|---:|---:|---|
| Motor Third Party Fire & Theft | 4,770.20 | 8 | 33.8% | 4,770.20 | 8 | yes |
| Home Basic | 9,020.96 | 13 | 71.3% | 9,020.96 | 13 | yes |
| Classic Car | 2,008.69 | 4 | 39.2% | 2,008.69 | 4 | yes |
| Motor Comprehensive | 31,774.98 | 28 | 40.1% | 31,774.98 | 28 | yes |
| **Whole book** | **62,946.35** | **84** | **34.9%** | 62,946.35 | 84 | **yes** |

Before: 84,426.57 paid, 99 settlements, 46.8%.

## 8. What is still open

- **Which date decides the period.** The report, before and after, places a payment by the date of a `Settled` history row. A claim settled in one period and re-settled in the next will appear in both. That is consistent with Finance's totals here, but whether Finance counts by settlement date or by payment date is a question for Raj.
- **Paid can exceed approved on re-settled claims.** HLC-26-00044 has an approved amount of 1,277.58 but `paid_amount` 2,625.00 after three settlements. The numbers suggest the paid amount was added up across settlements. Whether that is right (extra payments) or a data error is a question for Finance. The report now counts such a claim once, with whatever amount it holds.
- **The MCP view is Midlands only**, so the whole-book comparison above came from the API, not from the MCP server. The MCP tool `loss_ratio_by_product` already counted each claim once from the start, so it shows the correct logic for that region.
- **One unexplained test failure.** While finishing this fix, the four MySQL-backed tests all failed together in one full run. I did not capture the message. They passed in the next full run (71 of 71) and in six further runs of those four tests, and I could not reproduce it. I am reporting it as unexplained, not as fixed.
- The first attempt to check these numbers with an administrator SQL query was refused by Claude Code's permission classifier. I did not work round it. The data checks above all went through the MCP server, and the whole-book comparison went through the API.
