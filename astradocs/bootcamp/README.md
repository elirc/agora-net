# Build Agora: implementation bootcamp

This is the guided learning companion to implementing all 75 stories. The main outcome is your ability to explain, change, and verify the codebase. Working features provide concrete examples to study and extend.

The original story documents remain design specifications. Their original “not implemented” labels describe when they were written; use the [live story tracker](story-tracker.md) for current implementation status. A story is complete only when its acceptance criteria, relevant tests, API documentation, and learning material are covered. A build alone is not completion.

## Start here

1. Read [the curriculum](curriculum.md) and choose the current module.
2. Read [the implementation journal](journal.md) to understand actual decisions and results.
3. Open [module 1: values, counts, and API contracts](01-values-and-contracts.md). Predict each example before reading its explanation.
4. Follow its links into actual code. Explain one request aloud, then run the focused tests.
5. Attempt an exercise without looking at the answer. Compare your explanation afterward.

Use [your learning log](learning-log.md) to keep predictions, corrections, and new examples. It includes two worked entries and a reusable session template.

Continue with [module 2: queries and counterexamples](02-queries-and-counterexamples.md) and [module 3: ownership and small writes](03-ownership-and-small-writes.md).

Next, study [module 4: bounded reads and HTTP validators](04-bounded-reads-and-http-validators.md) through product comparison and rating summaries.

The next catalog workshop covers [tags and ordered collections](04b-tags-and-collections.md), with set replacement and editorial ordering examples.

Then study [draft cloning without inherited history](04c-cloning-without-copying-history.md), including object identity, copied values, and transactional rollback.

[Editing live variants and galleries](04d-editing-live-values-and-galleries.md) connects current prices, purchase snapshots, strict option parsing, and parent revisions for ordered children.

[Module 5: private notes and concurrent copies](05-private-notes-and-concurrent-copies.md) introduces independent revisions, migrations, and rollback through wishlist features.

Continue account workflows with [history and repeat purchases](05b-history-and-repeat-purchases.md), then [merging carts and auditing writers](05c-merging-carts-and-auditing-writers.md).

[Saved criteria and explicit history](05d-saved-criteria-and-explicit-history.md) covers stored search definitions and recent views. [Review reports as a separate workflow](05e-review-reports-as-a-separate-workflow.md) explains moderation boundaries and one-way resolution.

[Templates store intent](05f-templates-store-intent.md) revisits snapshots, current prices, atomic cart changes, capacity races, and safe retries through reusable shopping lists.

[Quotes and shared pricing](07a-quotes-and-shared-pricing.md) teaches extraction without side effects and cent-by-cent totals. [Precedence and time predicates](07b-precedence-and-time-predicates.md) covers saved checkout defaults, stale references, and exact discount start/expiry boundaries.

[Return eligibility and evidence](07c-return-eligibility-and-evidence.md) distinguishes shared policy from supplementary context. [Shipment progress and private notes](07d-shipment-progress-and-private-notes.md) teaches independent state machines, atomic event history, and privacy across response paths.

[Gift-card ledgers and honest history](07e-gift-card-ledgers-and-honest-history.md) follows every balance writer, proves local rollback, and explains opening balances without inventing past transactions.

[Category trees and global invariants](08a-category-trees-and-global-invariants.md) begins the senior workshops with bounded graph traversal, shared revisions, writer audits, and explicit legacy-data remediation.

Continue with [category option schemas](08b-category-option-schemas.md), [preview versus reservation](08g-preview-is-not-a-reservation.md), and [one price calculator across workflows](08h-one-price-calculator-many-workflows.md). These connect authoring rules to staged imports, quantity pricing, and historical returns.

[Shipping eligibility](08e-shipping-eligibility.md) and [business-day calendars](08f-business-day-calendars.md) apply trusted inputs and exact boundaries to delivery choices and estimates.

[Purchase orders and receipts](08c-purchase-orders-and-receipts.md) and [inventory count sessions](08d-inventory-count-sessions.md) teach the difference between incoming stock, observed stock, and a safe committed correction.

[Revocable login sessions](09a-revocable-login-sessions.md) and [guest order credentials](09b-guest-order-capabilities.md) trace the difference between possessing a signed token and being authorized to access a particular resource. Follow the tracker for verification status.

[Read-only machine credentials](09e-read-only-machine-credentials.md) introduces named authentication schemes and proves why read scopes cannot become administrator roles. The [access API reference](access-api-reference.md) contains the login/guest route matrix and rollout changes.

[Workshop 6a: webhook health](06a-webhook-health.md) teaches creation cohorts, current outcomes, half-open date intervals, and weighted report totals.

[Workshop 6b: packing work and printable documents](06b-packing-work-and-safe-documents.md) separates inventory from fulfillment, traces historical snapshots, and explains safe HTML output.

[Workshop 6c: stock policy and demand](06c-stock-policy-and-demand.md) compares configured targets with sales-based suggestions, including null revisions, join multiplication, and exact ceiling arithmetic.

[Workshop 6d: atomic stock corrections and replay](06d-atomic-stock-and-replay.md) works through a lost response, canonical request identity, transactional receipts, and a deliberately injected persistence failure.

Use one short session at a time. You do not need to hold all 75 features in your head. Return to the same idea through a plain-language explanation, a worked example, a code trace, a test, and a small exercise.

## What the records mean

The operations and delivery workshops continue with [order holds](09c-operational-holds.md), [warehouse leases](09d-warehouse-leases.md), and [private account exports](09f-private-account-exports.md). Then follow [catalog bootstrap and change feeds](10-catalog-bootstrap-and-change-feeds.md), [the durable webhook outbox](10a-durable-webhook-outbox.md), [attempt evidence](10b-webhook-attempt-evidence.md), [historical replay](10c-historical-webhook-replay.md), [background sales exports](10d-background-sales-exports.md), and [seeking through order history](10e-seeking-through-order-history.md).

- **Planned:** implementation has not started.
- **In progress:** code or learning work is underway; do not assume acceptance criteria pass.
- **Implemented, awaiting verification:** behavior is present, but required evidence is incomplete.
- **Complete:** implementation, tests, documentation, and lesson evidence are recorded.

The journal distinguishes observations from intentions. Failed tests and changed decisions belong there too. Generated test artifacts live under ignored `artifacts/bootcamp`; the journal records commands, outcomes, and the evidence that matters without committing machine-specific logs or secrets.

## How to study completed code

Use [practice and explain-back drills](11-practice-and-explain-back.md) to revisit the lessons through plain-language explanations, layer boundaries, failure predictions, and small independent exercises.

Do not just copy the final solution. Cover the implementation, read the test name, predict the response and database state, then trace the code. Change one input in your prediction: empty list, stale version, another owner, expired time, or duplicate request. Explain why the answer changes.

The later modules build on these habits: first precise values and queries, then aggregate rules, transactions, access control, and durable workers. Each has a smaller experiment you can perform independently of the full system.
