# Practice backlog

These tickets are **not implemented** by the learning-material update. Work in increasing difficulty. For every ticket, provide a contract or design, focused tests, full-suite result, docs, and a short tradeoff note. Pick one at a time.

## L1: Exact SKU catalog filter — foundations

**User need:** find the product containing a known SKU and combine it with price or stock requirements.

**Scope:** extend `ProductSearchRequest` and `ProductCatalogQuery`. Specify trimming, case behavior, and blank-input handling before coding.

**Acceptance:** exact matching; no substring false positives; SKU, stock, currency, and price all match the same variant; no-match response is a successful empty page; returned product retains all variants.

**Tests:** a product whose target SKU is unavailable but whose other SKU is available must not match available-target-SKU search. Include a similarly prefixed SKU and invalid input. **Hint:** the existing single `Any` is the extension point. **Stretch:** evaluate whether the database SKU uniqueness behavior agrees with your case policy.

## L2: Consistent pagination validation — foundations to intermediate

**User need:** predictable list errors across API surfaces.

**Scope:** inventory the existing paged controllers. Introduce a shared validation approach only after listing their differing defaults and limits; preserve those intentional differences.

**Acceptance:** invalid page sizes, page zero, and offset overflow return 400; valid empty pages retain counts; ties have unique ordering. Existing request/response names remain compatible.

**Tests:** boundary cases on at least two affected endpoints, including different defaults. **Hint:** `page >= 1` alone does not make `(page - 1) * pageSize` safe. **Stretch:** explain why this still does not solve deep-offset cost.

## L3: Deterministic expiry tests — intermediate

**User need:** discount and gift-card expiry behavior that is reliable at exact boundaries.

**Scope:** inspect existing `now` arguments and `DateTimeOffset.UtcNow` call sites; inject `TimeProvider` at workflow boundaries where useful, keeping pure domain methods explicit.

**Acceptance:** a request uses a consistent instant for its decision; tests can move across expiry without sleeping; UTC behavior is documented.

**Tests:** just before, exactly at, and just after expiry; no dependence on the wall clock or test duration. **Hint:** use existing `RedeemabilityBoundaryTests` as a starting point. **Stretch:** describe clock assumptions across multiple services.

## L4: Bounded sales reports — intermediate

**User need:** a report request must not unexpectedly load years of order data.

**Scope:** review `Sales` and `TopProducts` date handling. Choose and document a range limit; review in-memory aggregation and response bounds.

**Acceptance:** reversed and excessive ranges fail clearly; default range remains useful; day/week/month boundaries are correct in UTC; monetary totals remain accurate.

**Tests:** boundary range, reversed dates, ISO week across New Year, and fractional monetary values. **Hint:** do not change historical revenue semantics while optimizing; document gross versus refunded revenue separately. **Stretch:** measure database aggregation against the current approach on the same dataset.

## L5: Order ownership and guest access — intermediate to advanced

**User need:** only authorized people may read, cancel, or refund an order, including legitimate guests.

**Scope:** design account-owner and admin rules plus an unguessable guest credential, its storage, expiry/recovery policy, and exposure to logs. Review order, fulfillment, and return routes together.

**Acceptance:** knowing an order number alone grants no sensitive action; customer B cannot act on A's order; legitimate guests have a documented access path; failures do not leak unnecessary resource data.

**Tests:** anonymous, owner, other customer, admin, valid guest credential, invalid credential, and expired/revoked credential according to the chosen policy. **Hint:** `[Authorize]` establishes identity, not ownership. **Stretch:** plan compatibility for previously created guest orders.

## L6: Durable checkout idempotency and reconciliation — advanced

**User need:** retrying after a network failure does not create duplicate orders or charges.

**Scope:** persist operation identity and fingerprint, define concurrent request behavior, use a stable gateway idempotency identifier, and design recovery for ambiguous payment outcomes.

**Acceptance:** same-key same-payload replay returns the established outcome; changed payload is rejected; restart preserves behavior; concurrent identical requests cannot double charge; unresolved charges can be reconciled by a documented process.

**Tests:** simultaneous requests coordinated by gates, gateway decline, accepted charge with lost response, final-save failure, restart and replay. Assert gateway calls and durable state. **Hint:** a uniqueness constraint is needed to arbitrate competing key creation. **Stretch:** define key retention and abandoned reservation cleanup.

## L7: Durable webhook outbox — advanced

**User need:** a paid order eventually produces its notification even if the process stops.

**Scope:** save notification intent with the business state; process it through a scoped worker with bounded retries, claim/lease behavior, and visible terminal failure.

**Acceptance:** business commit and event intent are atomic; restart resumes pending work; transient failure retries; duplicate delivery is explicitly supported through a stable event ID; multiple workers do not casually process the same claim.

**Tests:** stop before send, send succeeds but acknowledgement is lost, worker restart, lease expiry, retry exhaustion, competing workers. **Hint:** an outbox enables recoverable delivery, not exactly-once network effects. **Stretch:** design replay permissions and an operational dashboard contract without exposing secrets.

## L8: Catalog performance experiment — advanced

**User need:** browse a much larger catalog with predictable response cost.

**Scope:** create disposable representative data, capture generated SQL and query plans, compare projection/split-query or cursor designs, and choose based on measured requirements.

**Acceptance:** correct filters and stable ordering remain covered; measurements are reproducible; allocation and latency claims identify dataset and environment; any API compatibility change has a migration plan.

**Tests:** ties, empty results, many variants/images, and concurrent inserts under the declared consistency model. **Hint:** page size bounds products, not the number of joined rows. **Stretch:** document a reason to retain the simpler current implementation if measurement does not justify complexity.
