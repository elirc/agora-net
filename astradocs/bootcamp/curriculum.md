# Curriculum and implementation order

[Bootcamp home](README.md) | [Tracker](story-tracker.md) | [Journal](journal.md)

The learning sequence and implementation dependencies guide the order. These are modules to develop, not claims that their features already exist.

| Module | Stories | Main outcome |
| --- | --- | --- |
| 1. Values and API contracts | JS-01/02/03/04/12/13/16/20 | Separate stored facts, computed values, and serialized fields |
| 2. Filtering and predictable pages | JS-05/06/07/09/10/11/14/15/17/19/23/24/25 | Build one filtered query for count and page; validate before executing |
| 3. Small safe writes and ownership | JS-08/18/21/22 | Reject invalid references and preserve other customers' data |
| 4. Catalog aggregates | MS-01/02/03/04/05/06/17 | Model related records, ordered membership, and stale edits |
| 5. Account workflows | MS-11/12/13/14/15/16/18/19/20/21 | Combine ownership, state, and current versus historical data |
| 6. Stock and operational reports | MS-07/08/09/22/27/30 | Make batches atomic and define honest reporting cohorts |
| 7. Pricing, returns, and fulfillment | MS-10/23/24/25/26/28/29 | Share calculations and preserve monetary/event history |
| 8. Catalog authoring and selling rules | SS-01/02/03/04/05/06 | Stage imports, preserve graph invariants, and compose policies |
| 9. Warehouse coordination | SS-07/08/09/10 | Reconcile stock observations and coordinate competing workers |
| 10. Access and portability | SS-11/12/13/14/19 | Separate valid credentials from authorized resource access |
| 11. Durable integrations | SS-15/16/17/18/20 | Survive restarts with explicit state, identity, and recovery rules |

SS-16 and SS-17 require SS-15. Shared catalog/cart/pricing helpers will be introduced where several implemented features need the same rule, rather than creating a framework before its first use. Schema-changing modules require both fresh-database tests and upgrades from the previous migration.

## The five-pass practice

The first worked lesson for module 4 is [bounded reads and HTTP validators](04-bounded-reads-and-http-validators.md), covering MS-03/MS-17. The first worked lesson for module 5 is [private notes and concurrent copies](05-private-notes-and-concurrent-copies.md), covering MS-14/MS-15. Other stories in those modules remain separate work; lesson availability does not mean the entire module is complete.

Module 4 continues with [tags and collections](04b-tags-and-collections.md), [draft cloning](04c-cloning-without-copying-history.md), and [variant/gallery editing](04d-editing-live-values-and-galleries.md). Follow the tracker for each workshop's verification status.

Module 5 continues through [owned history and repeat purchases](05b-history-and-repeat-purchases.md), [cart merging](05c-merging-carts-and-auditing-writers.md), [saved criteria and recent views](05d-saved-criteria-and-explicit-history.md), [review reports](05e-review-reports-as-a-separate-workflow.md), and [templates](05f-templates-store-intent.md). Saved preferences connect account workflows to [shared pricing](07a-quotes-and-shared-pricing.md) and [precedence/time predicates](07b-precedence-and-time-predicates.md).

Module 6 begins with [webhook health](06a-webhook-health.md), [packing work and safe documents](06b-packing-work-and-safe-documents.md), and [stock policy and demand](06c-stock-policy-and-demand.md). Lessons explain the implementation as it develops; the tracker distinguishes verified stories from work in progress.

1. **User pass:** what useful behavior changes?
2. **Data pass:** which values already exist, and which must be stored?
3. **Request pass:** where does validation, authorization, and execution happen?
4. **Failure pass:** what must remain unchanged if the request fails?
5. **Teaching pass:** explain the rule with a different example without looking at the code.

For each module, keep a prediction, the actual result, and a corrected explanation. A wrong prediction is useful when you can explain the difference.

## Return to the same idea at increasing depth

Use these routes through the lessons when a concept needs another explanation:

| Concept | Start with a concrete value | Follow a request | Study a failure |
| --- | --- | --- | --- |
| Current versus historical facts | [Values](01-values-and-contracts.md) | [Shared pricing](07a-quotes-and-shared-pricing.md) | [Quantity pricing and historical returns](08h-one-price-calculator-many-workflows.md) |
| Who can see a record? | [Small writes and ownership](03-ownership-and-small-writes.md) | [Guest capabilities](09b-guest-order-capabilities.md) | [Private exports](09f-private-account-exports.md) |
| One change, several rows | [Tags and collections](04b-tags-and-collections.md) | [Stock receipts](08c-purchase-orders-and-receipts.md) | [Outbox rollback and recovery](10a-durable-webhook-outbox.md) |
| A stale copy | [Wishlist notes](05-private-notes-and-concurrent-copies.md) | [Inventory counts](08d-inventory-count-sessions.md) | [Warehouse leases](09d-warehouse-leases.md) |
| A bounded read | [Queries](02-queries-and-counterexamples.md) | [Conditional catalog reads](04-bounded-reads-and-http-validators.md) | [Order-history seeking](10e-seeking-through-order-history.md) |
| An uncertain result | [Atomic stock replay](06d-atomic-stock-and-replay.md) | [Attempt evidence](10b-webhook-attempt-evidence.md) | [Historical replay](10c-historical-webhook-replay.md) |

Repeat a 25-minute session: spend five minutes predicting one example, ten tracing the code, five changing one input on paper, and five explaining the result aloud. If the explanation is difficult, return to the smaller example. There is no deadline for moving to the next lesson.

A useful weekly checkpoint is one small change you can explain end to end. State the user behavior, locate the rule, predict the database writes, name a failure that must roll them back, and point to the test that proves it. Senior-level reasoning grows from doing these small steps consistently.
