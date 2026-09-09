# Workshop 6c: a threshold is a policy; demand is an observation

Stories: MS-08 and MS-09. Implementation and verification status live in [the tracker](story-tracker.md). Read this alongside the actual code and record your predictions before running the examples.

## Start with the policy example

On hand is 12 and reserved is 4. Available is 12 - 4 = 8. The administrator chooses a threshold of 8 and a target of 20.

Available is exactly at the threshold, so the variant appears in the reorder report. Suggested quantity is 20 - 8 = 12. If you subtract on hand instead, you obtain 8 and ignore four units already promised to another operation.

Threshold answers “when should this row appear?” Target answers “what available level would we like to reach?” Neither value changes the stock record when a report is opened.

## Defaults are not stored overrides

Read [InventoryReorderPolicy](../../src/Agora.Domain/Entities/InventoryReorderPolicy.cs). It validates `0 <= threshold <= targetLevel <= 1,000,000` before replacing any fields. The revision advances on accepted updates.

Read [ReorderPoliciesController](../../src/Agora.Api/Controllers/ReorderPoliciesController.cs). A real variant with no policy returns threshold 5, target 5, `hasOverride=false`, and a null version. A GET does not insert a policy. An explicit 5/5 policy instead has `hasOverride=true` and an integer revision, even though its numeric values match the defaults.

Say the distinction another way: “I did not choose a value” and “I deliberately chose the default number” are different pieces of information. The API preserves that distinction.

The report starts from inventory records, including inactive catalog variants that still have stock records. A variant without an inventory record has no stock observation and is absent from this report. The individual policy read still works for that real variant. The existing low-stock endpoint keeps its own contract.

## Null can be a meaningful command

The PUT contract requires the `expectedVersion` property to be present, but allows its value to be null. Null means “create only if no override exists.” An integer means “replace the override only if its revision equals this number.”

| Stored policy | Expected version | Result |
| --- | --- | --- |
| Absent | null | Create revision 0 |
| Absent | 0 | Conflict |
| Revision 0 | null | Conflict |
| Revision 0 | 0 | Replace and advance to 1 |
| Revision 1 | 0 | Conflict |

Open [the DTO](../../src/Agora.Api/Contracts/ReorderPolicyContracts.cs). `JsonRequired` enforces property presence. A normal required-value annotation would reject null, which would remove our create-only command. Missing and explicit null are different JSON inputs.

The controller check gives a useful early error, while the database key and concurrency token enforce the rule when two requests overlap. A check followed by a later unguarded write would leave a race.

## Now calculate demand instead of reading a policy

The replenishment report is independent of stored reorder policies. It selects paid order lines in a recent creation-independent cohort: payment time in `[asOf - windowDays, asOf)`. Eligible current statuses are Paid, PartiallyFulfilled, and Fulfilled. Cancelled and fully refunded orders do not contribute.

Suppose the 30-day cohort contains 30 ordered units. Currently approved returns on those same order lines total 6. Net demand is 24 units; daily average is 24 / 30 = 0.8. With 10 cover days and 3 available units, suggestion is ceiling(0.8 × 10 - 3) = 5.

An approved return counts even if approval happened after the sales interval. The question is “what is the current net outcome of those purchases?” It is not “how many returns happened during those dates?” Review [the webhook cohort workshop](06a-webhook-health.md) for the same distinction applied to deliveries.

## Why join totals instead of raw children?

Imagine one 30-unit order line and two approved return rows of 2 and 4 units. A raw join produces two rows:

| Joined row | Ordered units | Returned units |
| --- | ---: | ---: |
| First return | 30 | 2 |
| Second return | 30 | 4 |

Summing both columns gives 60 - 6 = 54, which is wrong. Sales did not double because two returns exist.

[ReplenishmentController](../../src/Agora.Api/Controllers/ReplenishmentController.cs) separately groups cohort sales and approved returns by variant, then joins those aggregate results. Each side contributes at most one row per variant. The correct result stays 30 - 6 = 24.

Trace the return join carefully: it joins by order-item ID to the cohort lines, then groups using the line's variant ID. It cannot subtract unrelated returns merely because their timestamps fall within the same interval.

## Exact integer arithmetic until the response

For nonnegative whole units, ceiling(net × cover / window) equals `(net × cover + window - 1) / window` using integer division. Subtracting an integer availability after this ceiling gives the same result as subtracting it before the ceiling.

Example: one net unit, ten cover days, thirty window days, zero available. Integer ceiling is `(1 × 10 + 29) / 30 = 1`. Rounding the daily average down too early could incorrectly suggest zero.

The implementation widens sums to long, checks intermediate multiplication bounds, and rejects negative net demand or negative availability as inconsistent data. It filters positive suggestions before counting and paging. Decimal daily average is computed for the bounded response page, not used as an early rounded input to the stock recommendation.

Only surviving variants whose current parent product is active appear. Historical sales for a deleted variant cannot become a current replenishment row. A surviving variant without an inventory record is treated as having zero available units in this advisory report; this is distinct from the inventory-rooted policy report.

## Repeat the whole idea in one minute

First explain the policy report: “Look at current available stock, compare it with my threshold, and show how far it is below my target.” Then explain replenishment: “Look at recent net sales, calculate average demand for the requested cover period, and subtract available stock.” Finally point to the two different controllers and identify their common habits: explicit bounds, filtering before pagination, stable tie order, a read transaction, and no stock mutation.

## Practice and answers

1. On hand 12, reserved 4, threshold 8, target 20: inclusion and suggestion? **Included; 12.**
2. Threshold 0, target 0, available 0: policy report row? **Included with suggestion 0; equality qualifies.**
3. Does the demand report include zero suggestions? **No; it lists positive suggestions only.**
4. What does a missing expectedVersion property mean? **An invalid request, distinct from explicit null.**
5. Thirty ordered, returns 2 approved and 4 requested: net units? **28; requested returns do not reduce this report's sales.**
6. Why does approving that second return change a previously queried cohort? **The report uses current approval state for the original purchase cohort.**
7. Does this forecast seasonal peaks? **No; it is a transparent average, with its inputs returned for inspection.**

Write one new numerical example in your [learning log](learning-log.md). Calculate it on paper, predict the JSON, then verify the database rows and response. Keep the wrong prediction if you made one; add the reason it was wrong.
