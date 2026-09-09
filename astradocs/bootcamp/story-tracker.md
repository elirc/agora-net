# Live story tracker

[Bootcamp home](README.md) | [Journal](journal.md)

Updated as implementations gain evidence. Original story documents remain the acceptance specifications. Complete requires implementation, meaningful verification, API documentation, and learning coverage.

Final verification (2026-09-08): **75 of 75 stories complete** (25 junior, 30 midlevel, 20 mid/senior). The frozen implementation passed all **820 tests**, with zero failures or skips. Migration/model consistency also passed. Evidence: `artifacts/bootcamp/final/all-stories-regression-3.trx`. Earlier per-story counts below preserve the history of focused checks; this final regression covers the integrated implementation.

| ID | Feature | Status | Evidence / lesson |
| --- | --- | --- | --- |
| JS-01 | Page navigation flags | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-02 | Active and saved cart line counts | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-03 | Set and read variant weight | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-04 | A predictable primary product image | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-05 | Find a category by slug | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-06 | Search category names | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-07 | Browse root or child categories | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-08 | Reject an unknown category parent | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-09 | Safe and stable category pages | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-10 | Find a product by exact SKU | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-11 | Find products with or without images | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-12 | Stable variant order in product responses | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-13 | Product variant count | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-14 | Filter product reviews by minimum rating | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-15 | Read product reviews oldest first | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-16 | An in-stock flag on inventory responses | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-17 | Filter shipping methods by delivery time | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-18 | Reject undefined shipping rate types | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-19 | Search my wishlist names | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-20 | Wishlist stock summary counts | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-21 | Clear one wishlist without deleting it | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-22 | Read one of my saved addresses | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-23 | Filter my address book by country | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-24 | Filter my order history by status | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| JS-25 | Reject reversed top-product report dates | Complete | [Modules 1-3](README.md); 483-test regression passed, see journal |
| MS-01 | Product tags | Complete | [Workshop 4b](04b-tags-and-collections.md); 147-test catalog regression passed, including upgrades and conflicts |
| MS-02 | Curated product collections | Complete | [Workshop 4b](04b-tags-and-collections.md); corrected public query and 147-test regression passed |
| MS-03 | Product comparison | Complete | [Module 4](04-bounded-reads-and-http-validators.md); 499-test regression passed, see journal |
| MS-04 | Edit variants with conflict detection | Complete | [Workshop 4d](04d-editing-live-values-and-galleries.md); 147 tests passed, including real checkout snapshots and stale saves |
| MS-05 | Manage and reorder product images | Complete | [Workshop 4d](04d-editing-live-values-and-galleries.md); 147 tests passed, including legacy upgrade and competing gallery edits |
| MS-06 | Clone a product as a draft | Complete | [Workshop 4c](04c-cloning-without-copying-history.md); 147 tests passed, including rollback, aliasing and tied image order |
| MS-07 | Atomic bulk stock adjustments | Complete | [Workshop 6d](06d-atomic-stock-and-replay.md); 57 selected tests passed, including barrier race, rollback trigger, and upgrade |
| MS-08 | Per-variant reorder policies | Complete | [Workshop 6c](06c-stock-policy-and-demand.md); all policy/upgrade checks passed in the 24-pass/3-fail stock run; failures isolated to MS-09 |
| MS-09 | Replenishment suggestions | Complete | [Workshop 6c](06c-stock-policy-and-demand.md); corrected scalar-null aggregate passed all report tests in the 57-test run |
| MS-10 | A read-only checkout quote | Complete | [Workshop 7a](07a-quotes-and-shared-pricing.md); no-write/provider assertions, totals equivalence, and existing pricing regressions passed in 133-test subset |
| MS-11 | Merge two carts | Complete | [Workshop 5c](05c-merging-carts-and-auditing-writers.md); insertion correction, writer audit, and stale-cart rollback in 160 passing tests |
| MS-12 | Reusable cart templates | Complete | [Workshop 5f](05f-templates-store-intent.md); corrected bounded SQLite projection, live-price application, capacity/apply races, and upgrade in 313 passing tests |
| MS-13 | Saved catalog searches | Complete | [Workshop 5d](05d-saved-criteria-and-explicit-history.md); shared execution, capacity race, and upgrade in 160 passing tests |
| MS-14 | Private wishlist item notes | Complete | [Module 5](05-private-notes-and-concurrent-copies.md); 499-test regression includes migration and independent-connection conflicts |
| MS-15 | Copy items between wishlists | Complete | [Module 5](05-private-notes-and-concurrent-copies.md); 499-test regression includes copy, rollback and membership-path coverage |
| MS-16 | Recently viewed products | Complete | [Workshop 5d](05d-saved-criteria-and-explicit-history.md); retention, concurrent upsert, and upgrade in 160 passing tests |
| MS-17 | Rating histograms with conditional reads | Complete | [Module 4](04-bounded-reads-and-http-validators.md); 499-test regression passed, see journal |
| MS-18 | Report a product review | Complete | [Workshop 5e](05e-review-reports-as-a-separate-workflow.md); ownership, duplicate race, resolution revision, and upgrade in 160 passing tests |
| MS-19 | Saved checkout defaults | Complete | [Workshop 7b](07b-precedence-and-time-predicates.md); precedence, use-time ownership, creation race, stale update, and upgrade passed |
| MS-20 | An owned order timeline | Complete | [Workshop 5b](05b-history-and-repeat-purchases.md); timeline checks passed in the 108-pass/1-fail account run; failure isolated to merge |
| MS-21 | Repeat an order into a new cart | Complete | [Workshop 5b](05b-history-and-repeat-purchases.md); repeat-purchase checks passed in the 108-pass/1-fail account run; failure isolated to merge |
| MS-22 | An admin packing slip | Complete | [Workshop 6b](06b-packing-work-and-safe-documents.md); 25 tests passed; corrected one-page/nine-page print output visually inspected |
| MS-23 | Return window and eligibility preview | Complete | [Workshop 7c](07c-return-eligibility-and-evidence.md); shared rules, startup validation, exact boundaries, and competing quantities in 313 passing tests |
| MS-24 | Return evidence links | Complete | [Workshop 7c](07c-return-eligibility-and-evidence.md); ownership, post-approval isolation, cap race, and upgrade in 313 passing tests |
| MS-25 | Manual shipment tracking history | Complete | [Workshop 7d](07d-shipment-progress-and-private-notes.md); all transition pairs, atomic event race, privacy, and upgrade in 313 passing tests |
| MS-26 | Internal order support notes | Complete | [Workshop 7d](07d-shipment-progress-and-private-notes.md); server attribution, cross-response privacy, actor retention, and upgrade in 313 passing tests |
| MS-27 | A fulfillment work queue | Complete | [Workshop 6b](06b-packing-work-and-safe-documents.md); 25 report/checkout/fulfillment tests passed, see journal |
| MS-28 | Scheduled discount start times | Complete | [Workshop 7b](07b-precedence-and-time-predicates.md); controlled-clock boundaries, offset parsing, replacement semantics, and upgrade passed |
| MS-29 | Gift-card transaction history | Complete | [Workshop 7e](07e-gift-card-ledgers-and-honest-history.md); backfill, atomic rollback, competing redemptions, and all midlevel regressions passed: 641 tests, zero failures/skips |
| MS-30 | Webhook delivery health report | Complete | [Workshop 6a](06a-webhook-health.md); 18 webhook tests passed, see journal |
| SS-01 | Catalog import preview and commit | Complete | [Workshop 8g](08g-preview-is-not-a-reservation.md); 7 import tests passed including replay, forced rollback, competing drafts, upgrade, and shared create regressions |
| SS-02 | Safe category tree editing | Complete | [Workshop 8a](08a-category-trees-and-global-invariants.md); 8 tree tests passed including global revision race, depth/size bounds, legacy diagnostics, and upgrade |
| SS-03 | Category option schemas | Complete | [Workshop 8b](08b-category-option-schemas.md); 19 tests passed including structured Observe logging, authoring/publication race, legacy grandfathering, and upgrade |
| SS-04 | Quantity price tiers | Complete | [Workshop 8h](08h-one-price-calculator-many-workflows.md); 13 tests passed for thresholds, every shared price stage, historical return, saved currencies, rollback, and upgrade |
| SS-05 | Shipping destination and weight rules | Complete | [Worked lesson](08e-shipping-eligibility.md); final full regression: 820 passed, zero failures/skips; see journal |
| SS-06 | Business-day delivery calendars | Complete | [Worked lesson](08f-business-day-calendars.md); final full regression: 820 passed, zero failures/skips; see journal |
| SS-07 | Supplier purchase orders and receipts | Complete | [Workshop 8c](08c-purchase-orders-and-receipts.md); shared warehouse suite passed 10 API/persistence and 6 domain tests; includes receipt race, trigger rollback, inactive supplier, deletion, and upgrade |
| SS-08 | Inventory count sessions | Complete | [Workshop 8d](08d-inventory-count-sessions.md); shared warehouse suite passed 10 API/persistence and 6 domain tests; includes stale all-or-nothing correction, deleted variants, and upgrade |
| SS-09 | Operational order holds | Complete | [Worked lesson](09c-operational-holds.md); final full regression: 820 passed, zero failures/skips; see journal |
| SS-10 | Warehouse work assignments | Complete | [Worked lesson](09d-warehouse-leases.md); final full regression: 820 passed, zero failures/skips; see journal |
| SS-11 | Revocable login sessions | Complete | [Workshop 9a](09a-revocable-login-sessions.md); 10 tests passed for issuance, revocation, owner/role/expiry validation, legacy-token cutover, and upgrade |
| SS-12 | Scoped integration API keys | Complete | [Worked lesson](09e-read-only-machine-credentials.md); final full regression: 820 passed, zero failures/skips; see journal |
| SS-13 | Guest order access credentials | Complete | [Worked lesson](09b-guest-order-capabilities.md); final full regression: 820 passed, zero failures/skips; see journal |
| SS-14 | Private account data export | Complete | [Worked lesson](09f-private-account-exports.md); final full regression: 820 passed, zero failures/skips; see journal |
| SS-15 | Durable webhook outbox | Complete | [Worked lesson](10a-durable-webhook-outbox.md); final full regression: 820 passed, zero failures/skips; see journal |
| SS-16 | Webhook attempt history | Complete | [Worked lesson](10b-webhook-attempt-evidence.md); final full regression: 820 passed, zero failures/skips; see journal |
| SS-17 | Historical webhook replay | Complete | [Worked lesson](10c-historical-webhook-replay.md); final full regression: 820 passed, zero failures/skips; see journal |
| SS-18 | Background sales export jobs | Complete | [Worked lesson](10d-background-sales-exports.md); final full regression: 820 passed, zero failures/skips; see journal |
| SS-19 | Cursor-based order history | Complete | [Worked lesson](10e-seeking-through-order-history.md); final full regression: 820 passed, zero failures/skips; see journal |
| SS-20 | Catalog synchronization feed | Complete | [Worked lesson](10-catalog-bootstrap-and-change-feeds.md); final full regression: 820 passed, zero failures/skips; see journal |
