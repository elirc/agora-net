# 20 mid/senior feature stories with guided implementation plans

[AstraDocs home](README.md) | [Codebase map](03-find-your-way.md) | [30 mid-level stories](17-midlevel-user-stories.md) | [API reference](../docs/api-reference.md)

**Plans only: none of these 20 features has been implemented by adding this document.** New routes, entities, workers, migrations, and tests below describe work for you to build. Existing files are linked; suggested new filenames are in code formatting. These stories add capabilities beyond the junior and mid-level lists, rather than counting those same features again.

The aim is to practice owning a feature from its user promise through its failure paths. You will make decisions about state, access, concurrency, historical data, and rollout. Each plan gives small, explicit steps, but some stories need several days and a design review. An easy-to-follow sequence does not make payments, authentication, or background delivery inherently easy.

## Choose your next learning step

| ID | New feature | What you practice | Prerequisite |
| --- | --- | --- | --- |
| [SS-01](#ss-01-catalog-import-preview-and-commit) | Catalog import preview and commit | Staging, validation, atomic application | None |
| [SS-02](#ss-02-safe-category-tree-editing) | Category tree moves and breadcrumbs | Graph invariants and serialized changes | None |
| [SS-03](#ss-03-category-option-schemas) | Category option schemas | Versioned rules and gradual enforcement | None |
| [SS-04](#ss-04-quantity-price-tiers) | Quantity price tiers | Shared pricing and historical snapshots | None |
| [SS-05](#ss-05-shipping-destination-and-weight-rules) | Shipping destination/weight eligibility | Policy composition and checkout integration | None |
| [SS-06](#ss-06-business-day-delivery-calendars) | Business-day delivery estimates | Calendar arithmetic and stable snapshots | None |
| [SS-07](#ss-07-supplier-purchase-orders-and-receipts) | Purchase orders and receipts | Lifecycle rules and stock receipt idempotency | None |
| [SS-08](#ss-08-inventory-count-sessions) | Physical inventory count sessions | Stale observations and controlled reconciliation | None |
| [SS-09](#ss-09-operational-order-holds) | Operational order holds | A reversible restriction across write paths | None |
| [SS-10](#ss-10-warehouse-work-assignments) | Warehouse work assignments | Expiring ownership and competing workers | None |
| [SS-11](#ss-11-revocable-login-sessions) | Revocable login sessions | Token validation and rollout compatibility | None |
| [SS-12](#ss-12-scoped-integration-api-keys) | Scoped integration API keys | Separate authentication schemes and least privilege | None |
| [SS-13](#ss-13-guest-order-access-credentials) | Guest order access credentials | Capability access and closing bypass paths | None |
| [SS-14](#ss-14-private-account-data-export) | Private account data export | Ownership, consistent reads, explicit data selection | None |
| [SS-15](#ss-15-durable-webhook-outbox) | Durable webhook outbox | Durable intent, leases, and duplicate delivery | None |
| [SS-16](#ss-16-webhook-attempt-history) | Webhook attempt history | Recording uncertain external outcomes | SS-15 |
| [SS-17](#ss-17-historical-webhook-replay) | Historical webhook replay | Controlled backfill and event identity | SS-15 |
| [SS-18](#ss-18-background-sales-export-jobs) | Background sales export jobs | Job state, bounded artifacts, cancellation | None |
| [SS-19](#ss-19-cursor-based-order-history) | Cursor-based order history | Keyset queries and protected cursors | None |
| [SS-20](#ss-20-catalog-synchronization-feed) | Catalog synchronization feed | Change sequences, tombstones, and bootstrap | None |

A useful route is SS-02 -> SS-05 -> SS-08 -> SS-11 -> SS-15. The two explicit prerequisites are real: SS-16 and SS-17 use the event/worker model introduced by SS-15. No story requires implementing anything from the previous 55-story lists. Conditional notes explain how to compose features if you later build them together.

## Use the same learning loop each time

1. **Say the promise:** explain the user benefit in one sentence, without naming a database table.
2. **Draw the flow:** request -> authorization -> validation -> calculation -> mutation -> persistence -> external action -> response. Cross out stages the feature does not need.
3. **Work one example:** predict the exact records before success, after success, and after a rejected request.
4. **Find the code:** follow the linked controller into its service/entity and EF mapping. Read the named existing tests before writing new ones.
5. **Build one step:** follow the ten steps in the chosen story. Stop and rerun focused tests after each meaningful increment.
6. **Explain the failure:** describe what another request, a process crash, or old data could change. Test the particular failures the story calls out.
7. **Prepare review:** include the contract, schema/configuration changes, test evidence, and rollout limits. Do not call a feature complete merely because the happy path works.

Examples use invented labels such as A and PO-1; replace them with fixture-generated IDs. Use [TestAuth](../tests/Agora.Tests/Integration/TestAuth.cs) and [AgoraApiFactory.WithDbAsync](../tests/Agora.Tests/Integration/AgoraApiFactory.cs). Give scenarios unique emails, slugs, and SKUs because a class fixture shares its database. Ownership tests need customer A, customer B, and a real resource owned by B; a random missing ID does not prove authorization.

## Shared engineering recipes

**Commands and checks.** Run these yourself when implementing a story, from the repository root. They have not been executed as feature implementation for this document. Replace the test filter and migration name with your chosen feature.

```powershell
git status --short
dotnet test Agora.slnx --filter "FullyQualifiedName~CategoriesApiTests"
dotnet tool restore
dotnet tool run dotnet-ef -- migrations add AddCategoryTreeState --project src/Agora.Infrastructure --startup-project src/Agora.Api --output-dir Migrations
```

Generate a migration only after changing the model and only for a story that needs one. Use the pinned [local EF tool](../dotnet-tools.json), inspect the migration and model snapshot, and keep unrelated working-tree changes intact. Before review, run relevant tests and then `dotnet test Agora.slnx`; report actual outcomes. See [setup help](../docs/learning/01-first-hour.md) if the SDK or restore fails.

**Old data matters.** The ordinary API fixture uses `EnsureCreated`, which does not test an upgrade. For every schema story, create a separate disposable SQLite file, migrate to the previous schema, insert representative old records, apply the new migration, and verify backfills/constraints/data preservation. Never use the working database for this exercise. [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs) stores money in cents and timestamps in UTC ticks. Do not apply those conversions a second time in backfill SQL.

**Protect complete operations.** Validate the full proposed change before mutating it. Prefer one local save; use a short transaction when several reads/saves must succeed together. For SQLite read-then-write invariants, acquire a provider-supported non-deferred write transaction before the decision reads, using the same connection for EF commands. Do not hold it across gateway calls, file downloads, or webhook sends. Use database uniqueness and EF original-value concurrency tokens, not just an early existence/version check. Translate recognized constraint/lock/conflict failures accurately, not every database exception into 409.

**Test competing writers correctly.** Use separate contexts and independent connections to a temporary file-backed database, with a barrier to control the interleaving. Do not use concurrent tasks against the fixture's single SQLite connection or one DbContext. After failure, inspect committed state from a fresh scope. A stale-version test, a uniqueness race, and a process-restart test prove different things.

**Keep boundaries honest.** A local transaction cannot roll back a remote payment. The current gateway interface has no durable lookup/reconciliation contract. These stories do not claim to fix checkout payment recovery. Background workers provide at-least-once execution where stated, never guaranteed exactly-once network delivery. Capture time once where a boundary matters, inject `TimeProvider`, and test time by advancing a controlled clock rather than sleeping.

**Use explicit access and data contracts.** Follow existing ProblemDetails patterns: 400 malformed input, 401 missing/invalid authentication, 403 insufficient privilege, 404 missing or another customer's private resource, 409 stale/conflicting state, and 422 unusable but well-formed business input. New private routes compare the authenticated owner with stored ownership. Admin-only does not mean secrets can be included in every admin response. Never serialize entity graphs as a shortcut.

**Integrate the small pieces.** Register new services, options, policies, and workers in [Program.cs](../src/Agora.Api/Program.cs). Keep API DTO types out of Domain/Infrastructure. Audit all existing writers when adding a token or central rule, including legacy endpoints and reads that save observation flags. Add cancellation tokens and documented request/page/payload limits. For a feature flag, test both modes; disabling a feature must not bypass a persisted business restriction or strand a worker's pending records silently.

## SS-01: Catalog import preview and commit

**Status:** Proposed; not implemented.

**User story:** As a catalog administrator, I want to preview a structured product import and commit it as one operation, so I can prepare a batch without discovering errors after half the products have been created.

**Current code and learning:** ProductsController creates one product graph at a time. There is no staging area or import receipt. Learn the difference between validating a proposal and reserving its identifiers: a successful preview does not make tomorrow's commit valid automatically.

**Contract and acceptance criteria:** Admin POST `/api/admin/catalog-imports/preview` accepts version-1 JSON, at most 1 MiB, containing 1..100 new products and at most 300 variants total. This slice creates inactive products only; no updates/deletes, CSV parsing, or stock import. Each row carries a client row key, current product-create fields, and existing category/tax references. Store a normalized proposal, its digest, row errors, author, creation time, revision, and 24-hour expiry. Admin GET `/{id}` reads it; POST `/{id}/commit` supplies its revision/digest. Commit revalidates current state. Any error creates no catalog rows. Successful replay returns the original product-ID receipt; different digest, expired draft, or invalid state returns 409. Imported stock is zero.

**Files and data:** Start with [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs), [ProductContracts](../src/Agora.Api/Contracts/ProductContracts.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add `CatalogImport`, result rows, typed staging DTOs, and `CatalogImportService`. Reuse product-create validation without depending on any previous planned cloning/import feature.

**Implementation plan:**

1. Trace current product creation, including slug/SKU uniqueness, images, tax lookup, and inventory initialization. Write the equivalent two-row import by hand.
2. Define bounded input and deterministic normalization: trim documented fields, retain row keys, sort only unordered fields, and hash the normalized representation.
3. Add staging/result tables, explicit DraftValid/DraftInvalid/Applied states, revision mapping, and migration. Never store raw arbitrary executable expressions.
4. Extract reusable validation and graph construction from the existing create flow, keeping single-product behavior unchanged.
5. Implement preview: validate every row, report row-key/field/error details, and save only staging data. Duplicate identifiers inside the batch must be detected before commit.
6. Implement the admin read with normalized proposal, current draft errors, expiry, revision, and receipt if already applied.
7. Implement commit in a short write transaction: load/recheck state, return an existing matching receipt first, otherwise compare revision/digest/expiry and revalidate all live references and identifiers.
8. Construct all product graphs as inactive with zero inventory; save graphs, receipt, and Applied state atomically. Recognized uniqueness races must leave the draft/catalog recoverable with no partial products.
9. Add `CatalogImportApiTests`, upgrade tests, and a controlled commit race between two drafts using the same slug.
10. Document request examples, row error handling, and the distinction between replaying one applied import and submitting a different import.

**Worked verification:** Rows A/B preview successfully. Another admin creates B's slug. Commit must report the conflict and create neither A nor B. In a clean scenario, commit creates both, and replay returns the same IDs without more inventory rows. Also test duplicate batch SKUs, removed category, expired preview, forged digest, payload limits, non-admin access, and a forced save failure. Run `ProductsApiTests` and `CatalogSearchApiTests`.

**Rollout and scope:** Add schema first; expose the new endpoints after upgrade verification. This creates drafts and preserves existing create routes. No worker cleans drafts in this slice; document a later retention task rather than secretly deleting staging history.

**Explain it back:** Why must commit validate again after a green preview? Done means staging, all-or-nothing creation, and replay receipts each have their own assertions.

## SS-02: Safe category tree editing

**Status:** Proposed; not implemented.

**User story:** As a merchandiser, I want to move category branches and retrieve breadcrumbs, so I can reorganize navigation without creating loops or losing product assignments.

**Current code and learning:** Category already has ParentCategoryId. Update rejects direct self-parenting but does not prevent longer cycles. Learn graph invariants: preventing A -> A is not enough to prevent A -> B -> C -> A.

**Contract and acceptance criteria:** Add admin GET `/api/admin/category-tree` with a global tree revision; POST `/api/admin/categories/{id}/move` takes new parent ID or null plus expected tree revision. Add public GET `/api/categories/{id}/breadcrumbs` returning root-to-current IDs/slugs/names. Reject missing parents, descendants as parents, and a resulting depth above ten. Root depth is one; include the moved subtree's height in the depth calculation. Support up to 5,000 categories in this bounded implementation; larger trees return a clear 422 requiring a larger-scale implementation. Names/slugs/product CategoryId values remain unchanged by a move. Every existing create, parent-changing update, and delete must participate in the same tree invariant/revision protocol.

**Files and data:** Read [CategoriesController](../src/Agora.Api/Controllers/CategoriesController.cs), [Category](../src/Agora.Domain/Entities/Category.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add singleton `CategoryTreeState` with concurrency revision and a `CategoryTreeService`. Existing parent relationships remain; no closure table is required.

**Implementation plan:**

1. Draw a five-node tree. Calculate descendants, ancestors, subtree height, and resulting depth for one valid and one invalid move.
2. Add the singleton revision row/mapping and migration. Create a read-only integrity check that reports legacy loops, missing parents, and excess depth without modifying them.
3. Implement a pure iterative traversal with visited sets and a node cap. A legacy loop must terminate with a diagnostic, never stack overflow or infinite recursion.
4. Define tree/move/breadcrumb DTOs and deterministic sibling order by name then ID.
5. Implement admin tree and public breadcrumbs using bounded no-tracking reads. Invalid legacy paths return a clear consistency error rather than a fabricated breadcrumb.
6. Implement move in a short serialized write transaction: read revision and full bounded parent map, check expected revision, validate the proposed whole tree, update parent, advance revision, save.
7. Route existing create/update/delete through the same service. Legacy requests without an expected revision still acquire the same transaction and validate the final tree; they advance the global revision.
8. Preserve existing slug uniqueness and in-use deletion restrictions. Translate a stale new move to 409 and invalid topology to 422.
9. Add `CategoryTreeApiTests`, pure traversal tests, legacy migration/integrity fixtures, and competing moves that would jointly form a loop.
10. Document branch moves and an explicit remediation procedure for pre-existing invalid trees; do not make the migration arbitrarily detach categories.

**Worked verification:** A is parent of B, and B of C. Moving A under C fails without changing any parent. Moving B under independent root D produces breadcrumbs D/B/C for C. Two admins reading the same revision cannot both commit incompatible moves. Test null parent, missing parent, subtree depth overflow, legacy loop, product links unchanged, and old update-route bypass attempts. Run `CategoriesApiTests`.

**Rollout and scope:** Run the integrity check before enabling moves. Preserve identifiers and catalog assignments. Repairs to legacy data are explicit follow-up decisions; a tree edit is not permission to delete or silently reparent data.

**Explain it back:** Why does the version belong to the whole tree rather than just the moved category? Done means every parent-writing route preserves the same graph rule.

## SS-03: Category option schemas

**Status:** Proposed; not implemented.

**User story:** As a catalog administrator, I want to define valid variant options for a category, so new products use consistent keys and values such as size and color.

**Current code and learning:** ProductVariant.Options is a JSON-backed dictionary with no category-specific schema. Learn versioned validation rules and a rollout that reports legacy mismatches before enforcing new behavior.

**Contract and acceptance criteria:** Admin GET/PUT `/api/admin/categories/{id}/option-schema` manages a versioned schema with mode Off, Observe, or Enforce. No inheritance from parent categories in this slice. Allow at most ten lowercase ASCII keys, each 1..40 characters; each rule has Required and 1..50 distinct permitted plain-text values up to 80 characters. Normalize keys to lowercase and compare allowed values ordinally after trimming. Unknown keys are invalid when a schema is applied. PUT uses create-only null revision or the exact existing revision. Admin GET `.../option-schema/violations` reports current mismatching variants with bounded paging. Enforce validates new products, changes to options, and category moves of products; existing untouched products remain readable/purchasable. Observe reports mismatches but permits writes.

**Files and data:** Open [ProductVariant](../src/Agora.Domain/Entities/ProductVariant.cs), [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs), [ProductContracts](../src/Agora.Api/Contracts/ProductContracts.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add `CategoryOptionSchema` with typed versioned JSON rules, mode, revision, and unique category ID. It is independent of the previously planned variant editor.

**Implementation plan:**

1. Follow option serialization/change tracking in the EF mapping. Write examples of absent required size, valid size M, and unknown key material.
2. Define the schema DTO and pure validation result containing key, reason code, and safe actual value. Keep validation independent of HTTP/EF.
3. Add storage, lengths, unique relationship, revision token, and migration. Categories without rows behave as Off.
4. Implement admin create/update/read with final-schema validation, normalized duplicate detection, and concurrency handling.
5. Build the violations report by reading at most 10,001 narrow candidate rows for the category. Reject more than 10,000 with 422; otherwise validate the bounded set, filter to violations, sort by SKU then ID, count, and only then page. Do not page candidates first and return an accidentally sparse page of violations.
6. Integrate schema checks into current product creation and product category changes. For multi-variant writes, validate all variants before changing any.
7. Acquire a short local write transaction covering schema lookup and catalog save for enforcement, so concurrent schema publication cannot be bypassed between reading the rule and saving the product.
8. If SS-01 or the earlier variant editor is implemented, call the same validator from those writes. Do not assume those endpoints already exist.
9. Add `CategoryOptionSchemaApiTests`, pure rule tests, and an upgrade case with old nonconforming dictionaries.
10. Document Off -> Observe -> Enforce, how to inspect violations, and what remains grandfathered. Observations must be structured counts/reasons rather than logging whole product payloads.

**Worked verification:** Require size in S/M/L. A new `{size: XL}` product succeeds in Observe and appears in violations; it fails in Enforce. An existing XL product stays readable after enforcement until options/category are edited. Test normalized duplicate keys, missing required value, unknown keys, rule revision conflicts, schema/product-write competition, and atomic rejection of a product with one invalid variant. Run `ProductsApiTests`.

**Rollout and scope:** Start at Observe and inspect actual catalog data. This is a controlled authoring rule, not retroactive product deactivation or a general JSON-schema engine. No new package is required for the bounded rule vocabulary.

**Explain it back:** Why can a stricter schema coexist with old nonconforming rows? Done means the distinction between observing, enforcing new writes, and repairing old data is testable.

## SS-04: Quantity price tiers

**Status:** Proposed; not implemented.

**User story:** As a shopper, I want an automatic lower unit price when I buy enough of one variant, so bulk purchases receive the advertised quantity discount.

**Current code and learning:** Carts and checkout multiply a variant's single Price by quantity. Orders snapshot UnitPrice and LineTotal. Learn to introduce a shared pricing rule without making cart, payment, and refund calculations disagree.

**Contract and acceptance criteria:** Admin GET/PUT `/api/admin/variants/{id}/quantity-pricing` manages up to five tiers with an expected policy revision. A tier has minimum quantity 2..99 and a nonnegative unit amount with at most two decimal places, in the variant's currency. Thresholds are distinct and increasing; amounts are nonincreasing and cannot exceed current base price when saved. Empty tiers disable the policy. For a cart line, select the highest qualifying threshold and use the lower of its amount and current base price; a later base-price reduction must never make the tier a surcharge. Quantity applies to one variant line, not the whole basket. Cart responses show base unit price, applied unit price, and selected threshold; checkout snapshots the applied price. Coupon -> tax -> shipping/gift rules keep their existing relative order after this base line-pricing step.

**Files and data:** Trace [CartContracts](../src/Agora.Api/Contracts/CartContracts.cs), [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs), [ReturnService](../src/Agora.Infrastructure/Services/ReturnService.cs), and [OrderItem](../src/Agora.Domain/Entities/OrderItem.cs). Add `VariantQuantityPricing`/tier storage, revision mapping, and a pure `VariantPriceCalculator`. Use existing cent mappings; no exchange rates.

**Implementation plan:**

1. Find every current variant-price multiplication in cart mapping and checkout. Separately identify order/refund code that correctly uses historical UnitPrice.
2. Write the tier-selection function with base amount, ordered thresholds, and quantity as inputs. Test exact thresholds before integrating it.
3. Add policy/tier entities, unique variant/threshold constraints, revision, and migration. Existing variants have no policy and retain base pricing.
4. Implement admin GET/PUT with create-only null revision or exact existing revision, full replacement validation, and atomic child updates.
5. Refactor cart response construction to receive calculated line prices with policies batch-loaded. Avoid adding one database query per line or database access inside a static DTO mapper.
6. Show the applied unit price for active and saved lines, but include only active lines in totals. Select subtotal currency from active lines, with a documented default for an empty active set, rather than the current mapper's first arbitrary item, which may be saved. Reject mixed active currencies consistently; saved lines keep their own labeled unit prices.
7. Make checkout call the same calculator before subtotal/coupon/tax/shipping calculations. Snapshot the selected applied unit price and line total on OrderItem.
8. Leave historical refund calculations based on OrderItem snapshots. If the quote story is later implemented, use this same calculator there; do not fork the formula.
9. Add `QuantityPricingApiTests`, calculator unit tests, and migration tests. Inspect all API mapping call sites after the response construction change.
10. Document threshold examples and the fact that catalog base prices remain base prices while cart prices depend on quantity.

**Worked verification:** Base price 10.00, quantity-5 tier 9.00, quantity-10 tier 8.00: quantities 4/5/10 cost 40.00/45.00/80.00. Buy ten, change the live tiers, then return two: the refund calculation must begin from the purchased 8.00 unit snapshot. Test removal below a tier, saved-line activation, zero amount, invalid precision, stale policy replacement, base-price reduction, coupon/tax composition, and no policy. Run cart, `TotalsPipelineTests`, and `RefundTenderTests`.

**Rollout and scope:** Empty policies preserve existing totals. Publish the additive DTO fields before clients display tier labels. This is deterministic volume pricing, not coupon stacking, cross-variant bundles, or a promise that a previous cart price stays fixed until payment.

**Explain it back:** Why should a return read the order's unit price rather than today's tier table? Done means cart, checkout, and refund tests each prove the correct source of price.

## SS-05: Shipping destination and weight rules

**Status:** Proposed; not implemented.

**User story:** As a shopper, I want to choose only shipping methods that can carry my order to its destination, so checkout does not offer an unusable delivery option.

**Current code and learning:** ShippingMethod calculates flat/weighted charges and has activity/default flags. Checkout does not have a per-method destination or package-weight eligibility policy. Learn to separate “can use this method” from “what does it cost.”

**Contract and acceptance criteria:** Admin GET/PUT `/api/admin/shipping-methods/{id}/eligibility` manages a versioned policy: up to 50 unique uppercase two-letter country codes and optional maximum weight 0..1,000,000 grams. Empty countries means any country; null maximum means no configured weight cap. This is syntactic country-code validation, not an address-verification service. A public POST `/api/shipping-methods/eligibility` accepts country and nonnegative weight, returning active eligible methods with code/name/delivery-day fields. It is informational; checkout recomputes actual active-cart weight and applies the rule to the resolved address/method before reserving stock. Exact maximum weight is allowed. An explicitly selected or default method that fails eligibility returns 422; never silently switch to another method.

**Files and data:** Read [ShippingMethod](../src/Agora.Domain/Entities/ShippingMethod.cs), [ShippingMethodsController](../src/Agora.Api/Controllers/ShippingMethodsController.cs), and [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs). Add `ShippingEligibilityPolicy`, unique method ID, revision, allowed-country storage, and a pure evaluator.

**Implementation plan:**

1. Follow ResolveShippingAddressAsync and ResolveShippingMethodAsync, noting that saved-address ownership is already checked separately from shipping eligibility.
2. Define the evaluator result with eligible boolean and stable reason codes such as CountryNotServed or WeightExceeded. Keep pricing out of this function.
3. Add policy schema, normalization, revision, relationship, and migration. Methods without policy retain unrestricted destination/weight behavior.
4. Implement admin GET/PUT using full replacement, create-only null revision, and stale-update 409 semantics.
5. Implement the informational public endpoint using only active methods, deterministic code/ID order, and bounded input. Do not trust its previous result as checkout authorization.
6. Add checked active-line weight aggregation in checkout using a wide intermediate integer. Reject negative legacy weights or an unsupported total with a field-specific error before reservation.
7. Evaluate the selected method using normalized country on the address copy, without rewriting the customer's address-book record. Preserve tax address semantics.
8. Keep the rule evaluation before all stock/payment mutations. If a quote/default-preference feature exists, share this selection rule there as well.
9. Add `ShippingEligibilityApiTests`, pure boundary tests, migration checks, and fake-gateway no-call assertions on rejection.
10. Update method-selection documentation with one unsupported default and an explicit valid alternative selected by the client.

**Worked verification:** Method Light serves US/CA up to 2,000 grams. US at 2,000 is eligible; US at 2,001 and GB at 500 are not. A client falsely previewing weight 100 must still fail checkout if its actual cart weighs 2,500. Test lowercase country normalization, inactive methods, no policy, unknown selected code, saved address belonging to another customer, overflow, and unchanged stock/gift/coupon state after rejection. Run `ShippingApiTests` and checkout tests.

**Rollout and scope:** Add policies selectively so unconfigured methods retain existing behavior. Existing paid orders keep their shipping snapshots even if a rule changes later. This does not validate postal addresses or introduce carrier rate lookup.

**Explain it back:** Why does a shipping preview accept a weight while checkout calculates it independently? Done means the command enforces the same rule using trusted cart data.

## SS-06: Business-day delivery calendars

**Status:** Proposed; not implemented.

**User story:** As a shopper, I want delivery estimates to account for weekends, configured closure dates, and a daily dispatch cutoff, so the displayed dates match the shop's operating calendar.

**Current code and learning:** Checkout currently uses `now.AddDays(MinDays/MaxDays)`. Learn calendar arithmetic, exact cutoff boundaries, and preserving old order promises when calendar configuration changes.

**Contract and acceptance criteria:** Admin GET/PUT `/api/admin/delivery-calendar` manages one versioned shop calendar: Enabled flag, UTC cutoff time to minute precision, and at most 366 unique ISO closure dates. Business days are Monday-Friday excluding those dates. Disabled keeps current AddDays behavior. When enabled, the dispatch date is today only if today is a business day and time is strictly before cutoff; otherwise it is the next business day. Add MinDays/MaxDays business days after that dispatch date, where zero means dispatch day. Store date estimates at 00:00 UTC and document them as dates, not promised midnight delivery times. Reject invalid method day ranges or searches exceeding 730 calendar days. Existing orders retain their captured estimates. No local-time-zone/DST configuration in this first slice.

**Files and data:** Start in [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs), [ShippingMethod](../src/Agora.Domain/Entities/ShippingMethod.cs), [Order](../src/Agora.Domain/Entities/Order.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add singleton `DeliveryCalendar`, revision and closure dates, plus a pure `DeliveryDateCalculator`. Store closure dates explicitly as ISO date-only values; do not treat them as instants.

**Implementation plan:**

1. Work one week on paper with a Monday closure and Friday 14:00 UTC cutoff. Mark dispatch days and day-0/day-1/day-2 estimates.
2. Define separate functions for IsBusinessDate, NextBusinessDate, and AddBusinessDays, all with a bounded iteration guard.
3. Add configuration entities, singleton/unique-date constraints, revision token, and migration. Seed disabled behavior for old databases.
4. Implement admin replacement with cutoff/date validation and expected revision; sort dates for stable responses and reject duplicates.
5. Add pure tests before integrating with checkout, using explicit instants and dates rather than the operating system's local timezone.
6. Inject TimeProvider and capture the operation time once. Load one calendar snapshot and calculate estimates before reserving inventory.
7. Use the existing AddDays path when disabled; use date-only midnight-UTC snapshots when enabled. Reject malformed legacy shipping day ranges clearly.
8. Persist calculated dates on the order as today, with no subsequent lookup when reading old orders. If checkout quote exists, label its dates as recalculated estimates.
9. Add `DeliveryCalendarApiTests`, migration checks, and a checkout regression covering both calendar modes.
10. Document UTC-only cutoff semantics, exact-boundary behavior, date serialization, and why changing closures does not rewrite existing orders.

**Worked verification:** Friday at 13:59 UTC with cutoff 14:00 dispatches Friday; one business day is Tuesday when Monday is closed. Friday exactly 14:00 dispatches Tuesday; one business day is Wednesday. Test zero-day methods, weekends, year/leap-day boundaries, consecutive closures, disabled mode, stale calendar replacement, and the search cap. Run `ShippingApiTests` and `CheckoutApiTests`.

**Rollout and scope:** Start disabled, review a dated example with the shop owner, then enable. Keep the old calculation available for disabling future estimates; never migrate historical dates to a new interpretation. Carrier transit promises and geographic calendars are separate work.

**Explain it back:** Why is adding 24 hours different from advancing to the next business date? Done means cutoff and calendar examples are deterministic across machines and test timezones.

## SS-07: Supplier purchase orders and receipts

**Status:** Proposed; not implemented.

**User story:** As an inventory administrator, I want to track supplier orders and receive delivered quantities against them, so incoming stock has a source and partial deliveries are visible.

**Current code and learning:** Inventory can be set/restocked, but no procurement document explains an inbound delivery. Learn a lifecycle where a receipt changes both document progress and stock, with an idempotent local receipt.

**Contract and acceptance criteria:** Add admin supplier create/list/deactivate and purchase-order create/read/submit/cancel/receive routes under `/api/admin`. Suppliers have a name and optional reference, both bounded to 120 characters. A draft PO contains 1..100 distinct variant lines with ordered quantity 1..1,000,000 and SKU/name snapshots. States: Draft -> Ordered -> PartiallyReceived -> Received; cancel is allowed only from Draft/Ordered with no receipts. Receipt input contains a unique operation GUID, expected PO revision, and positive quantities for a subset of its lines, never exceeding remaining ordered quantities. Save the receipt, stock increments, PO revision, and state atomically. Same operation/content returns the original receipt; different content returns 409. Deactivated suppliers cannot receive new POs, but their submitted POs remain receivable. No invoicing, accounting integration, or supplier messaging.

**Files and data:** Open [InventoryItem](../src/Agora.Domain/Entities/InventoryItem.cs), [InventoryController](../src/Agora.Api/Controllers/InventoryController.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add Supplier, PurchaseOrder/Line, Receipt/Line, a PO concurrency token, and a unique operation-ID constraint. Keep historical snapshots when a variant disappears; a nullable variant FK may set null on deletion.

**Implementation plan:**

1. Draw the PO states and distinguish ordered, received, and remaining quantities. Trace InventoryItem.Restock and its existing version increment.
2. Define bounded DTOs and an operation fingerprint including PO ID and sorted line quantities, plus expected revision for exact replay matching.
3. Add entities, relationships, uniqueness, snapshot fields, and migration. Historical receipt rows must not vanish through catalog deletion.
4. Implement supplier management and draft PO creation using active supplier/current variants, validating the whole line set before saving.
5. Implement submit/cancel with expected revision and explicit transition methods. There is no draft-edit endpoint in this slice; a wrong draft can be cancelled and recreated.
6. Implement receipt replay lookup before current PO/version checks so a completed receipt remains replayable after the PO advances.
7. For a new receipt, acquire a short write transaction, recheck operation ID, load current PO/stock, validate all remaining quantities/revision, and reject deleted variants with line-specific 422 errors.
8. Calculate on-hand additions with checked arithmetic, call Restock, create immutable receipt rows, derive PO state from all received totals, and save together.
9. Add `PurchaseOrdersApiTests`, transition tests, upgrade checks, and an independent-connection race for two receipts competing for the final units.
10. Document partial receipt/replay examples and the distinction between incoming receipts and manual stock corrections.

**Worked verification:** Order ten A and five B. Receive four A -> PartiallyReceived; stock A rises by four. Replay does not restock again. Receive six A/five B -> Received. Test over-receipt, receipt against a draft/cancelled PO, concurrent final receipt, deleted variant, inactive supplier with existing order, integer overflow, and forced save failure leaving both stock and received totals unchanged. Run `InventoryApiTests` and inventory domain tests.

**Rollout and scope:** Empty new tables preserve current inventory behavior. Existing manual adjustments remain possible and are not reclassified as supplier receipts. This is not the earlier bulk-adjustment story: the PO's outstanding quantities and immutable receipt identity govern every inbound operation.

**Explain it back:** Why is a receipt more than an inventory delta? Done means document progress and physical stock accounting cannot disagree after a local save failure.

## SS-08: Inventory count sessions

**Status:** Proposed; not implemented.

**User story:** As an inventory lead, I want to collect stock counts in a session and apply them only if stock has not changed since the baseline, so a stale count cannot erase a sale or receipt.

**Current code and learning:** SetStock changes one inventory row immediately. There is no staged count worksheet. Learn why an observation has a time/version and why overwriting a newer balance with an old physical count is unsafe.

**Contract and acceptance criteria:** Admin POST `/api/admin/inventory-counts` selects 1..100 distinct variants and captures SKU, on-hand, reserved, and inventory version. GET `/{id}` reads the worksheet; PUT `/{id}/lines/{lineId}` records count 0..1,000,000 plus expected session revision; POST `/{id}/apply` and `/cancel` require that revision. States Open/Applied/Cancelled are explicit. All lines require counts before apply. Every inventory version must still equal its baseline, and every count must be at least reserved. A single stale line rejects the entire application. Record counted/applied actors and timestamps plus resulting differences. An Applied replay returns the stored receipt without changing stock again.

**Files and data:** Read [InventoryItem](../src/Agora.Domain/Entities/InventoryItem.cs), [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs), and [FulfillmentService](../src/Agora.Infrastructure/Services/FulfillmentService.cs). Add `InventoryCountSession`/lines, revision, states, snapshot values, and applied receipt values. Preserve historical lines if variants are removed.

**Implementation plan:**

1. Define the counting instruction precisely: count stock represented as on-hand, including pending checkout reservations but excluding units already sold and awaiting shipment. This repository deducts paid units before fulfillment; counting all units physically in the building would overstate its on-hand balance.
2. Work a baseline example with on-hand 10/reserved 2 and counted 9. Explain which physical units belong in that nine.
3. Add session/line schema, unique session/variant constraint, revision mapping, and migration. Existing inventory values are untouched.
4. Implement create with one consistent baseline read and immutable snapshots. Cap the selected set before loading data.
5. Implement counted-value updates scoped by session/line, allowed only in Open, advancing the parent revision. Do not adjust live stock while entering counts.
6. Implement apply: return an existing Applied receipt first; otherwise compare revision and require complete counts in a short write transaction.
7. Load all live inventory, compare baseline versions, validate reserved bounds/missing variants, and collect every conflict before changing anything. Do not automatically rebase a stale count by adding the latest sales delta.
8. Call SetStock for all lines, store before/after/difference and applied actor/time, mark Applied, and save atomically. Implement cancellation as a terminal no-stock-change action.
9. Add `InventoryCountApiTests`, upgrade checks, stale-sale/receipt tests, and a forced rollback test.
10. Document restarting a stale session after recounting, the meaning of inventory version, and why count sessions are not warehouse-wide physical asset accounting.

**Worked verification:** Baseline 10/reserved 2, count 9 -> apply yields on-hand 9/reserved 2/available 7. If a checkout changes stock/version after baseline, apply returns 409 and preserves the new stock. Test count below reserved, incomplete session, variant deletion, stale line edit, two applies, cancelled session, and a rejection leaving every valid line unchanged. Run inventory and stock reservation tests.

**Rollout and scope:** Pilot with a small selected variant set and the written counting instruction. Do not add a timer that applies unfinished counts. The earlier bulk-adjustment proposal accepts intentional deltas; this feature reconciles a versioned observation and therefore has different failure rules.

**Explain it back:** Why can a numerically plausible count still be stale? Done means both the count's definition and its version assumptions are visible in the worksheet and tests.

## SS-09: Operational order holds

**Status:** Proposed; not implemented.

**User story:** As a support administrator, I want to place an operational hold on an order, so the warehouse cannot create another fulfillment until the issue is resolved.

**Current code and learning:** Paid/partially fulfilled orders can be fulfilled without a separate hold decision. Learn to add a reversible restriction without overloading Order.Status or accidentally blocking unrelated financial operations.

**Contract and acceptance criteria:** Admin GET/POST `/api/admin/orders/{number}/holds` and POST `.../holds/{id}/release` manage a hold. Reasons are AddressQuestion, StockInvestigation, or CustomerRequest, with internal plain-text note up to 500 characters. Create requires an order currently Paid/PartiallyFulfilled; allow at most one active hold. Release requires the hold revision, stores actor/time, and does not delete history. Fulfillment creation returns 409 while held. A hold neither changes Order.Status nor adjusts stock/payment. Cancellation/refund retain their existing business rules; a later cancelled/refunded order may retain hold history but is already unfulfillable by its status. Internal reasons are not added to public order DTOs.

**Files and data:** Follow [FulfillmentService](../src/Agora.Infrastructure/Services/FulfillmentService.cs), [OrdersController](../src/Agora.Api/Controllers/OrdersController.cs), and [OrderService](../src/Agora.Infrastructure/Services/OrderService.cs). Add `OrderHold` with revision and a filtered unique active-hold index or an equivalent unique active-slot table. Avoid adding an Order concurrency token just for holds: it would also affect existing post-gateway saves.

**Implementation plan:**

1. Trace all entry points that create Fulfillment records. Write the invariant: no new fulfillment may commit after observing an active hold in the same serialized local operation.
2. Define reason enums, internal DTOs, state transitions, actor/time fields, and history-preserving order relationship.
3. Add schema, active-hold uniqueness, revision, and migration. Old orders have no holds.
4. Implement create inside a short write transaction covering order-status/active-hold checks and insertion; reject a second active hold with 409.
5. Implement admin list and revision-protected release. A released hold stays in history and cannot be released twice as a new action.
6. Refactor the local read/validate/save part of FulfillmentService into a short serialized write transaction that includes checking holds before creating coverage rows.
7. Commit before invoking any webhook sender. Do not hold SQLite's writer lock while an external call runs.
8. If the earlier fulfillment queue is implemented, show/filter held orders there, but retain the command-side guard; a queue filter alone is insufficient.
9. Add `OrderHoldsApiTests` with a controlled hold-versus-fulfillment race and tests that refund/cancellation behavior is not implicitly rewritten.
10. Document hold/release examples and make the meaning of “hold” explicit: future shipment creation, not undoing an existing shipment.

**Worked verification:** An order for five has two already fulfilled. Add a hold; an attempt to fulfill the remaining three fails. Release it; fulfillment can proceed. Test duplicate active hold, stale release, wrong order/hold combination, invalid order state, non-admin access, and both race orderings. If fulfillment commits before the hold, that shipment remains valid; if hold commits first, fulfillment must fail. No test should restock the already-paid units.

**Rollout and scope:** Deploy schema and the central fulfillment guard together before exposing hold creation. Hiding or disabling the management UI must not make saved holds stop working. This is a blocking workflow, distinct from the earlier internal-note feature.

**Explain it back:** Why must the hold check and fulfillment save share a transaction? Done means a hold is enforced at the write boundary, not merely displayed in a list.

## SS-10: Warehouse work assignments

**Status:** Proposed; not implemented.

**User story:** As a warehouse administrator, I want to claim an order for a short packing session, so colleagues can see who is working on it and cannot concurrently take the same assignment.

**Current code and learning:** There is no durable work owner; fulfillment is an immediate admin operation. Learn leases: an assignment is temporary ownership with an expiry and generation, not a permanent lock.

**Contract and acceptance criteria:** Admin POST `/api/admin/orders/{number}/work-assignment` claims a Paid/PartiallyFulfilled order for 15 minutes. GET reads the current assignment; POST `/renew` and `/release` require its opaque assignment ID and revision. Only the assigned admin may renew/release; others receive 409 when trying to claim an unexpired assignment. At `now >= expiresAt`, a new claim may replace it with a new assignment ID. While a live assignment exists, fulfillment creation requires the same assigned admin and assignment ID; an expired or stale supplied ID returns 409. With no live assignment and no supplied ID, the existing admin fulfillment behavior remains available. No stock reservation or automatic fulfillment occurs on claim/expiry.

**Files and data:** Read [OrdersController](../src/Agora.Api/Controllers/OrdersController.cs), [FulfillmentService](../src/Agora.Infrastructure/Services/FulfillmentService.cs), and [FulfillmentContracts](../src/Agora.Api/Contracts/FulfillmentContracts.cs). Add `WarehouseAssignment`, unique active slot per order, owner/admin ID, expiry, generation ID, revision, and clock injection. A full permanent assignment audit is outside this slice.

**Implementation plan:**

1. Draw claim -> renew -> release and claim -> expire -> new claim. Identify why an old worker must not regain authority merely because the owner ID happens to match again.
2. Define request/response fields and the exact inclusive expiry boundary. Keep assignment IDs in structured input, not credentials embedded in URL logs.
3. Add schema, unique order slot, revision mapping, and migration. Existing orders have no assignment.
4. Implement claim in a short write transaction with current order/status/expiry checks. Generate a new assignment ID for each replacement and derive owner from authentication.
5. Implement renew/release using owner, assignment ID, expected revision, and current expiry; never resurrect an expired assignment through renew.
6. Add optional assignment ID to fulfillment input and pass authenticated admin identity through the application input rather than trusting a request-supplied owner.
7. Check assignment validity in the same short transaction as coverage validation and fulfillment save. If SS-09 exists, use one shared transaction for hold and assignment guards.
8. Release a matching assignment after the order becomes fully fulfilled in that same local save; partial fulfillment leaves it available for renewal or explicit release.
9. Add `WarehouseAssignmentsApiTests`, controlled-clock boundary tests, and competing claim/stale-owner tests using separate connections.
10. Document what colleagues see after expiry and explain that a claim coordinates work but does not deduct inventory a second time.

**Worked verification:** Admin A claims at 10:00, expiring 10:15. B cannot claim at 10:14:59, but can at 10:15. A's old ID cannot fulfill or renew after B's claim. Test duplicate claims, renewal by another admin, release revision mismatch, full/partial fulfillment cleanup, cancelled order, no-assignment legacy flow, and a blocked request leaving coverage unchanged. Run `FulfillmentsApiTests`.

**Rollout and scope:** Introduce the optional contract and server-side guard before clients start claiming work. An assigned order cannot be bypassed through the old fulfillment endpoint. Worker performance dashboards and picking per warehouse location are separate features.

**Explain it back:** Why are expiry, revision, and a new assignment ID all useful? Done means a delayed request from a previous packing session cannot act as the current owner.

## SS-11: Revocable login sessions

**Status:** Proposed; not implemented.

**User story:** As an account holder, I want to see and revoke my active login sessions, so a token from a lost device can stop working before its normal expiry.

**Current code and learning:** JwtTokenService issues signed tokens with customer/email/role claims; validation does not look up a revocable session. Learn the difference between a cryptographically valid token and a currently authorized session.

**Contract and acceptance criteria:** Registration/login create a persisted session and include its ID in a signed `sid` claim; the token response also exposes session ID. Session rows contain customer ID, optional device label up to 80 characters, issue/expiry times, and nullable revoked time, never the raw JWT. Authenticated GET `/api/me/sessions` lists only the caller's sessions with bounded paging and an `isCurrent` flag. DELETE `/{id}` revokes one owned session; POST `/revoke-all` revokes all currently saved sessions including the caller's. Every authenticated JWT request must validate signature/issuer/audience/lifetime as today, then validate the session/customer relationship, exact session expiry, revocation, and current role matching the issued role. Missing `sid` tokens are rejected after cutover. Revocation applies to subsequent authorization checks; it cannot cancel work already authorized and running.

**Files and data:** Open [AuthController](../src/Agora.Api/Controllers/AuthController.cs), [JwtTokenService](../src/Agora.Api/Auth/JwtTokenService.cs), [Program.cs](../src/Agora.Api/Program.cs), and [Customer](../src/Agora.Domain/Entities/Customer.cs). Add `LoginSession`, customer/expiry indexes, and an authentication-session service. Do not implement refresh tokens or a custom JWT cryptographic format.

**Implementation plan:**

1. Trace registration and login through token issuance and JWT middleware. List which checks occur before the new session lookup.
2. Define session DTOs and exact expiry/revocation semantics. Choose a controllable clock and align boundary tests with the framework's existing token lifetime checks.
3. Add session schema and migration. Existing JWTs cannot be backfilled from database rows because no issued-token history exists.
4. Refactor issuance to generate a session ID, persist customer/session as needed, then sign a token containing that session ID and the same expiry. Do not return a usable token if persistence fails.
5. Add session validation in the JWT validated-token hook, using a request-scoped context. Reject unknown, revoked, expired, wrong-customer, or role-stale sessions; preserve existing cryptographic checks.
6. Implement the owned list and single revoke. Treat repeated revoke of an existing owned session as a successful no-op; another customer's ID is 404.
7. Implement revoke-all as one local transaction over the caller's saved sessions. A login that commits afterward is a new session, not one secretly exempted from the saved set.
8. Update TestAuth and auth fixtures so normal tests obtain real saved sessions rather than bypassing the new validation hook.
9. Add `LoginSessionsApiTests`, migration/cutover tests, and a test proving a still-unexpired revoked token fails on a different protected controller.
10. Document required re-login at cutover, additional database reads during authentication, and the limits of revoking already-running work.

**Worked verification:** Log in twice as A, producing S1/S2. Revoke S1 using S2; S1 receives 401 on `/api/auth/me`, while S2 remains valid. Revoke-all using S2; its next request also fails. Test B's session ID, missing sid, forged sid, expired session with otherwise valid JWT, removed customer, changed role, failed session save, and anonymous access. Run `AuthApiTests` and `AuthzMatrixTests`.

**Rollout and scope:** Deploy the schema and issuance/validation change as a documented authentication cutover. Existing tokens require re-login. Do not roll back to permissive stateless validation after promising revocation; use a coordinated auth rollback/re-login plan instead. Password reset and refresh-token rotation are separate features.

**Explain it back:** Why can a valid signature still lead to 401? Done means revocation is checked centrally on every authenticated JWT route, not just the session-management endpoints.

## SS-12: Scoped integration API keys

**Status:** Proposed; not implemented.

**User story:** As an administrator, I want to issue narrowly scoped read-only integration keys, so a catalog synchronizer does not need a full administrator login.

**Current code and learning:** Authentication currently distinguishes Customer/Admin JWT roles. There is no machine credential scheme with its own scopes. Learn explicit authentication schemes, one-time secret disclosure, and authorization that cannot accidentally inherit admin power.

**Contract and acceptance criteria:** Admin POST/GET `/api/admin/integration-keys` and POST `/{id}/revoke` manage keys with name 1..80 characters, expiry 1..90 days, and scopes drawn only from CatalogRead and InventoryRead. Creation returns the full secret once; list shows ID/name/scopes/expiry/revocation only. Generate at least 32 random secret bytes using platform cryptographic randomness; store a SHA-256 digest and public lookup ID, not plaintext. New key-authenticated GET `/api/integrations/catalog` and `/inventory` require the matching scope and bounded paging. Keys use `X-Agora-Api-Key`, never query strings. They do not authenticate as Admin or grant access to customer, checkout, existing admin-write, or key-issuance routes. Public unauthenticated catalog routes remain public as before.

**Files and data:** Start in [Program.cs](../src/Agora.Api/Program.cs), [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs), [InventoryController](../src/Agora.Api/Controllers/InventoryController.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add `IntegrationApiKey`, a named authentication handler, scope policies, and narrow integration response DTOs.

**Implementation plan:**

1. Draw two authentication paths: default JWT for existing account/admin routes, named API-key scheme only for the new integration endpoints. Mark the accepted scheme on each endpoint.
2. Define the key format as a public GUID lookup ID plus random secret. Define fixed allowed scopes and reject unknown/empty/duplicate-normalized scope input.
3. Add metadata/digest schema, creator ID, expiry/revocation timestamps, and migration. Use a fixed-length digest field and no raw-secret column.
4. Implement admin issuance with platform randomness and hashing, returning the full key only in the create response. Keep keys out of structured logging and error details.
5. Implement list/revoke without reconstructing or revealing the key. Revocation is checked on every authenticated key request rather than cached indefinitely.
6. Implement the named handler: parse bounds, load by public ID, compare digest with constant-time platform comparison, then check revocation/expiry.
7. Issue only machine identity/scope claims, with no Admin role. Bind the new policies explicitly to the API-key scheme so an unrelated principal cannot satisfy them accidentally.
8. Add the two bounded read endpoints with explicit catalog/stock DTOs, deterministic paging, and no customer information.
9. Add `IntegrationKeysApiTests` with a complete key-scope/route matrix and assertions that response/log fixtures do not contain stored or raw secrets.
10. Document one-time copying, caller-side storage, key rotation by creating/revoking separate keys, and the precise route set each scope grants.

**Worked verification:** CatalogRead can read the integration catalog but receives 403 on integration inventory; a malformed/expired/revoked key receives 401. The same key cannot create a product or issue another key. Test both scopes together, unknown ID, wrong secret for a real ID, JWT-only access to a key-only route, non-admin key creation, and second reads not redisclosing the secret. Run `AuthzMatrixTests`.

**Rollout and scope:** Add the scheme without changing the existing default JWT scheme. Keep request-header logging disabled for credentials. This is a local read-only integration interface; write scopes, delegated customer access, OAuth, and secret-manager integration are outside the slice.

**Explain it back:** Why is “this key has CatalogRead” different from “this caller is Admin”? Done means the route matrix proves both allowed reads and denied privilege escalation.

## SS-13: Guest order access credentials

**Status:** Proposed; not implemented.

**User story:** As a guest shopper, I want a private credential for my order, so I can view it and request a return without an account or relying on someone guessing my email/order number.

**Current code and learning:** Some order routes currently trust order numbers, and return ownership can use an email match. Learn capability access by binding an unpredictable credential to one resource and checking every alternative route to that resource.

**Contract and acceptance criteria:** Successful checkout for an order with no CustomerId returns a one-time-disclosed `guestOrderAccessToken`; store only its digest, order binding, expiry 30 days after issue, and revocation metadata. Send it on later requests in `X-Agora-Order-Access`, never in a path/query string. A valid token permits reading that order and creating/reading/cancelling its return requests; it does not permit full order refund, return approval, or access to another order. Account-owned orders require actual customer ownership or Admin; matching email does not grant access. Order cancellation requires owner/Admin; full refund requires Admin. Existing routes must enforce these rules too, not remain bypasses. Admin rotation can revoke the old guest credential and reveal a replacement once; no email delivery is part of this story.

**Files and data:** Read [CheckoutController](../src/Agora.Api/Controllers/CheckoutController.cs), [OrdersController](../src/Agora.Api/Controllers/OrdersController.cs), [ReturnsController](../src/Agora.Api/Controllers/ReturnsController.cs), and [ReturnService](../src/Agora.Infrastructure/Services/ReturnService.cs). Add `GuestOrderCredential`, a central order-access evaluator, explicit actor/capability inputs, and safe customer-facing DTO projections.

**Implementation plan:**

1. Build a route/action matrix for order reads, cancellation/refund, return create/read/cancel/approve/reject, and fulfillment reads. Mark current identity/email checks and intended owner/admin/guest rights.
2. Define a high-entropy token with public lookup ID and random secret, digest storage, exact expiry, and revocation. Reuse platform cryptographic primitives, not homegrown encryption.
3. Add schema with order binding and active-credential uniqueness; retain revoked metadata as needed without preserving plaintext tokens.
4. Generate a guest credential before checkout's final successful local save so paid state and digest persist together. Return the raw value through a dedicated checkout result only once; never put it into a webhook payload.
5. Implement an access evaluator that checks stored Order.CustomerId and, only for guest orders/actions, the bound credential. Treat Email as contact data, not authorization.
6. Replace the old access paths using the matrix, including ReturnService's email-based fallback. Carry trusted caller context from the API and recheck the actual loaded order in the service.
7. Add the new guest read entry point and adapt existing public order/return routes to the same evaluator. Reject credential reuse against another order and reject guest financial-admin actions.
8. Project safe order/return responses: exclude gift-card bearer codes, payment credentials, unrelated customer data, and internal notes. Add admin credential rotation with atomic revoke-and-replace.
9. Add `GuestOrderAccessApiTests`, a full owner/admin/guest/wrong-email/foreign-token matrix, and tests against old endpoint bypasses.
10. Update checkout/return API documentation and record this as an intentional access-contract change requiring clients to retain the one-time token.

**Worked verification:** Guest checkout A returns token TA. Order number/email alone cannot read A; TA can read A and request a return, but cannot read B, approve its own return, or perform a full refund. Rotate TA; the old token fails. Test expired token, account-owned order with matching guest email, old routes, lost checkout response, and secret absence in logs/webhooks/normal reads. Run order, return, checkout, and authorization tests.

**Rollout and scope:** Deploy the new credential response and all access guards together as a documented breaking change. Legacy guest orders need an explicit admin-assisted rotation/reissue path; do not mint access from email alone. Lost payment responses remain part of the existing payment-recovery limitation, not a reason to bypass access checks.

**Explain it back:** Why does a protected new guest endpoint accomplish little if the old order-number endpoint stays open? Done means the whole route matrix, including legacy routes, enforces the same resource-bound access.

## SS-14: Private account data export

**Status:** Proposed; not implemented.

**User story:** As an account holder, I want a downloadable copy of my profile and purchase history, so I can inspect and retain the information associated with my account.

**Current code and learning:** Account information is spread across profile, address, order, wishlist, and review endpoints. There is no explicitly scoped portable export. Learn allowlisted data selection, consistent reads, and why ownership must follow stored relationships rather than shared email text.

**Contract and acceptance criteria:** Authenticated POST `/api/me/data-export` produces version-1 JSON as an attachment with generatedAt and a documented section list: profile contact fields, addresses, owned orders/items, their fulfillments and returns, owned wishlists/items, and authored reviews. This is the specified export scope, not a claim to export every possible future table. Never include password hashes, JWTs/session secrets, guest/cart/gift-card credentials, integration keys, webhook secrets, other reviewers' identities, or internal admin notes. Include order history only by CustomerId; a guest order sharing the account's email is not automatically owned. Capture a consistent database read snapshot. Cap the combined export at 10,000 records and 5 MiB; if exceeded, return 422 with no partial download. No job/retention/email system in this bounded slice.

**Files and data:** Read [MeController](../src/Agora.Api/Controllers/MeController.cs), [AuthController](../src/Agora.Api/Controllers/AuthController.cs), [WishlistsController](../src/Agora.Api/Controllers/WishlistsController.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add `AccountExportService` and explicit versioned export DTOs. No migration is needed.

**Implementation plan:**

1. Make a field allowlist for each promised section. Start from entities but copy only named allowed fields; do not reuse secret-bearing response DTOs blindly.
2. Draw the ownership joins: customer -> addresses/orders/wishlists/reviews, and owned order -> items/fulfillments/returns. Order ownership controls return inclusion even when a legacy ReturnRequest.CustomerId is inconsistent.
3. Define the record-count budget across all sections and a stable JSON format/version. Document which temporary or future features are outside version 1.
4. Implement authenticated caller resolution with no request-supplied customer ID and no email-based ownership inference.
5. Open one read transaction and perform bounded no-tracking projections. Check count limits before loading a large child set; apply a global remaining-record budget rather than 10,000 per table.
6. Avoid calling wishlist response helpers that save stock-observation flags. This export must not mutate recently viewed state or other read-observation metadata.
7. Serialize to a bounded in-memory buffer before sending response headers. Reject byte-limit overflow without returning a truncated file, then close the database transaction promptly.
8. Return a server-generated safe filename, JSON content type, attachment disposition, and private/no-store headers. Do not cache the export under an unprotected public URL.
9. Add `AccountDataExportApiTests`, two-customer fixtures with shared-looking contact data, excluded-secret markers, and concurrent-change snapshot tests.
10. Document the exact exported schema, size-limit behavior, and how a later version could add sections without silently changing consumers' assumptions.

**Worked verification:** A owns one order and wishlist; B has another order and a review on A's purchased product. A's export contains A's order/wishlist/review only, not B's order or review body. A guest order with A's email stays excluded. Test secret markers in every sensitive table, private support notes if implemented, exact size/count boundaries, no partial response on rejection, zero-data account, and no observation writes. Run address, wishlist, auth, and owned-order tests.

**Rollout and scope:** This adds a private read capability without changing existing data. Its limits are explicit so it does not silently become a memory-heavy background system. Account deletion, legal compliance guarantees, and asynchronous large-account exports are separate decisions.

**Explain it back:** Why is an entity dump different from a deliberate account export? Done means every included field has a stated purpose and every ownership join has a negative test.

## SS-15: Durable webhook outbox

**Status:** Proposed; not implemented.

**User story:** As an integration operator, I want committed order events to remain available for delivery after the API process restarts, so a notification is not lost between saving an order and sending its webhook.

**Current code and learning:** WebhookService sends before saving its delivery rows, after the business save. Learn the transactional outbox: save the intent with the business change, then send outside that transaction. This improves notification durability, not checkout payment reconciliation.

**Contract and acceptance criteria:** Persist version-1 OutboxEvent records for the existing order.created/order.paid/fully order.fulfilled/order.refunded emission points. Preserve today's meaning that order.created is emitted after successful checkout, not at the temporary pending-order save. Save events even with no subscribers. In the same local transaction, create one delivery per matching active subscription, unique by event/subscription, with frozen URL, serialized payload, signature, and stable event ID. Keep the legacy payload `id` as delivery ID and add `eventId`/schema version. A worker claims at most ten due deliveries, uses a 60-second lease and 15-second send timeout, sends outside a DB transaction, and finalizes only if its lease generation still matches. Reserve one of the existing five attempt slots when claiming, before sending. Expired/uncertain attempts consume a slot. Duplicate delivery is possible; receivers should deduplicate stable IDs.

**Files and data:** Read [WebhookService](../src/Agora.Infrastructure/Services/WebhookService.cs), [Webhook entities](../src/Agora.Domain/Entities/Webhook.cs), [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs), [OrderService](../src/Agora.Infrastructure/Services/OrderService.cs), and [FulfillmentService](../src/Agora.Infrastructure/Services/FulfillmentService.cs). Add OutboxEvent, delivery event FK/lease/version/due-time/URL snapshot fields, and a hosted worker using fresh scopes.

**Implementation plan:**

1. Draw three crash points: before business commit, after commit before send, and after remote acceptance before local acknowledgement. State the expected durable rows at each.
2. Define event identity, payload version, 64-KiB payload cap, unique event/subscription rule, and due-time retry schedule: 1, 5, 15, then 60 minutes after failures.
3. Add schema and migration. Legacy deliveries keep payload/signature/counters and nullable EventId; backfill their destination from the existing subscription and initialize due times only for Pending/Failed rows with remaining attempts. Never requeue Succeeded/exhausted rows or invent historical OutboxEvents.
4. Split enqueueing from transport. Enqueue only stages tracked rows and never saves/sends independently of its caller's local unit of work.
5. Update each event origin to stage events/deliveries before its final business save, with subscription selection in that same short local transaction. Retain immutable event data rather than re-reading changed orders during retries.
6. Implement an atomic due-row claim with lease generation, revision, and attempt-slot advancement. Recover expired claims as uncertain failures; never reset their count or refund a used slot.
7. Implement worker send/finalize with the fake transport first. Only the current unexpired generation may update delivery outcome; a late worker cannot overwrite a newer claim. Do not increment AttemptCount again on completion.
8. Change manual retry to schedule the same worker path and return 202, without a second synchronous sender. Pause claims for inactive subscriptions. Change subscription deletion to soft deletion and cancel unsent work; deletion cannot retract an already in-flight network call.
9. Add `WebhookOutboxTests` with restart, claim competition, lease expiry, late acknowledgement, delete/disable, and forced business-save failure scenarios. New Cancelled status, if used for deletion, must be explicit in DTOs and any previously implemented health report.
10. Document worker configuration, bounded attempts, legacy migration behavior, receiver deduplication, and shutdown/drain behavior. Worker-off mode retains queued work; it must not revert to send-before-save.

**Worked verification:** Commit a paid order with the worker stopped; events/deliveries exist and zero sends occurred. Restart the worker and deliver them. Simulate acceptance followed by a crash before local completion: another attempt may send the same delivery ID/event ID, but business payment must never run again. Test all five slots exhausted, concurrent workers, failed commit producing no event, and partial fulfillment not emitting the fully-fulfilled event. Run webhook, checkout, order, and fulfillment tests.

**Rollout and scope:** Migrate first, stop the old synchronous dispatcher when enabling the outbox, and enable the worker only after queued rows are readable. Existing historical gaps cannot be reconstructed automatically. Real HTTP transport, payment idempotency, and guaranteed exactly-once delivery remain outside this story.

**Explain it back:** Why is a duplicate notification preferable to silently losing the durable intent, and what must the receiver do? Done means crash tests prove the stated boundaries, not just retry success.

## SS-16: Webhook attempt history

**Status:** Proposed; not implemented. **Prerequisite:** SS-15's durable delivery/lease model.

**User story:** As an integration operator, I want to inspect each recorded delivery attempt, so I can distinguish explicit rejection, transport uncertainty, and a worker that disappeared.

**Current code and learning:** WebhookDelivery currently retains only a count and last outcome. SS-15 adds durable claiming but does not itself provide immutable per-attempt history. Learn to record what is known without converting an uncertain external result into a fictional success/failure.

**Contract and acceptance criteria:** Admin GET `/api/admin/webhook-deliveries/{id}/attempts` returns bounded history ordered by attempt number. Each new attempt stores delivery ID, attempt number, lease generation, reserved time, optional send-initiated time, finish time, outcome Pending/Succeeded/Failed/Unknown, optional HTTP code, and a bounded safe reason code. An explicit transport result is Succeeded/Failed; a timeout, lost worker, or expired lease with no accepted completion is Unknown. Starting an attempt does not prove the receiver saw it. Terminal attempt rows are not rewritten by a late worker. Old deliveries expose `historyStartsAtAttempt`; do not fabricate rows for their earlier AttemptCount. Payloads, secrets, signatures, full URLs, and exception stacks are excluded from this endpoint.

**Files and data:** Extend SS-15's planned worker and [Webhook entities](../src/Agora.Domain/Entities/Webhook.cs); inspect [WebhooksController](../src/Agora.Api/Controllers/WebhooksController.cs) and [WebhookTests](../tests/Agora.Tests/Unit/WebhookTests.cs). Add `WebhookAttempt` with unique delivery/attempt-number and a delivery history-start marker. This is distinct from the earlier health report, which summarizes current delivery state.

**Implementation plan:**

1. Draw attempt 1 timing out while attempt 2 later succeeds. Keep final delivery status separate from the two individual attempt outcomes.
2. Define the evidence vocabulary and mapping from fake sender results/exceptions. Only explicitly confirmed response codes should populate HttpStatusCode.
3. Add attempt schema, uniqueness, history-start marker, and migration. Set the marker to old AttemptCount + 1 for legacy deliveries; do not reset their remaining budget.
4. Modify the worker claim transaction to insert Pending attempt N together with reserving delivery slot N. A failed claim must leave neither an increment nor an orphan attempt.
5. Before invoking transport, record send-initiated time if possible while retaining its precise meaning: local intent to invoke, not proof of remote receipt.
6. On completion, check the current lease generation and update delivery plus attempt terminal outcome in one local transaction. Duration derives from recorded timestamps, not a made-up receiver timing.
7. On timeout/lease recovery, finalize the unfinished attempt as Unknown before a new claim can proceed. A stale completion must not change that terminal row or the newer delivery state.
8. Add the admin read with paging/counts and explicit missing-history metadata. Do not serialize the delivery/subscription graph.
9. Add `WebhookAttemptHistoryTests`, two-worker late-result scenarios, migration coverage, and a uniqueness race on the same attempt number.
10. Update operational docs to show the relationship between attempt history, current delivery status, reserved attempt slots, and the configured five-slot limit.

**Worked verification:** Attempt 1 is Pending, then times out -> Unknown. Attempt 2 receives HTTP 200 -> Succeeded; delivery is Succeeded with count 2. A late completion from attempt 1 cannot overwrite either fact. Test explicit 500 -> Failed, worker death before send initiation, legacy count 3 with first new history row 4, claim rollback, invalid page, non-admin access, and secret marker exclusion.

**Rollout and scope:** Stop old workers and drain or expire active leases before upgrading the worker/schema pair, so a legacy worker cannot consume slots without the required new attempt record. Preserve older missing-history metadata permanently. This feature records observations, not provider-side delivery receipts or a guarantee about what happened during a timeout.

**Explain it back:** Why does Unknown carry useful information instead of merely being an error label? Done means every new reserved attempt slot has one durable row and late results cannot rewrite settled history.

## SS-17: Historical webhook replay

**Status:** Proposed; not implemented. **Prerequisite:** SS-15's stored events and worker.

**User story:** As an integration administrator, I want to deliver selected retained events to a newly configured subscription, so I can backfill that consumer without changing or re-paying the original orders.

**Current code and learning:** Existing retry repeats an already-created delivery. It cannot create a delivery to a subscriber that did not exist at event time. Learn the difference between retrying transport and replaying immutable business-event data.

**Contract and acceptance criteria:** Admin POST `/api/admin/webhook-replays` takes operation GUID, target subscription ID, and 1..100 distinct retained OutboxEvent IDs. The subscription must be active/not deleted and subscribe to every requested event type. Support only understood schema version 1 and events no more than 30 days old at request evaluation. Validate the entire set before enqueueing. Existing event/subscription deliveries are reported as AlreadyExists, regardless of their current result; use normal retry for those instead. New deliveries use current target URL/signing configuration but original event data/time/event ID and a new delivery ID. Return a durable replay receipt with Enqueued/AlreadyExists results. Reusing operation/content returns the same receipt; changed content returns 409. The replay action never sends inline or reruns a business service.

**Files and data:** Start with [WebhooksController](../src/Agora.Api/Controllers/WebhooksController.cs) and [WebhookService](../src/Agora.Infrastructure/Services/WebhookService.cs), then SS-15's new event/enqueue types. Add `WebhookReplayBatch`/result rows, unique operation ID, canonical request digest, requester, timestamp, and links to event/delivery identities. No prerequisite on SS-16.

**Implementation plan:**

1. Draw one OrderPaid event and two subscriptions: A existed at purchase; B was created today. Show one shared event ID and separate delivery IDs.
2. Define replay DTOs, event-age boundary, schema validation, request normalization, and receipt statuses. Sort event IDs for the operation digest while retaining a documented response order.
3. Add batch/result schema and uniqueness. Preserve receipt metadata when subscriptions are soft-deleted, using SS-15's deletion model.
4. Implement matching-operation replay lookup before current target validation so an already-applied receipt remains readable after target deactivation.
5. For a new batch, acquire a short write transaction and load the target plus all selected events. Reject missing/too-old/unsupported/type-mismatched events with 422 and no new deliveries.
6. Query existing event/subscription pairs together. Classify these as AlreadyExists without resetting status, attempt count, lease, or payload.
7. Create missing deliveries through SS-15's enqueue helper using immutable event data and current target configuration. Store receipt/results atomically with deliveries.
8. Let the existing worker claim/send new rows; return 202 for newly enqueued work and a matching saved receipt on replay. No gateway, order transition, or inventory method is called.
9. Add `WebhookReplayApiTests`, concurrent replay batches targeting the same pair, receipt rollback, and replay-after-configuration-change cases.
10. Document receiver deduplication by event/delivery IDs, the 30-day eligibility limit, and the difference between this API and retrying an existing failed delivery.

**Worked verification:** Create paid event E with subscriber A only, then add B. Replay E to B: a new delivery uses E's original total/time even if the order/catalog has changed. A second batch for E/B reports AlreadyExists; the same operation returns the same receipt. Test old event, unknown schema, one invalid event among valid ones, inactive target, target missing the event type, race with normal enqueue, and unchanged payment/stock counts.

**Rollout and scope:** Expose replay only after the outbox and worker are stable. Events from before SS-15 that were never stored cannot be replayed from invented snapshots. This bounded administrative backfill does not provide arbitrary payload editing or an unlimited historical export endpoint.

**Explain it back:** Why should replay preserve business-event identity while creating a different delivery identity? Done means the consumer can recognize the original fact and the transport can still track its new destination.

## SS-18: Background sales export jobs

**Status:** Proposed; not implemented.

**User story:** As an administrator, I want to request a downloadable sales CSV and poll its progress, so generating the file does not hold my original HTTP request open.

**Current code and learning:** AdminReportsController returns synchronous reports, including in-memory sales bucketing. There is no persisted export job or downloadable artifact. Learn to separate request acceptance, bounded computation, and publishing a completed result.

**Contract and acceptance criteria:** Admin POST `/api/admin/report-exports` accepts version-1 sales-export parameters: paired increasing `paidFrom`/`paidTo`, at most 90 days. Return 202/job ID. GET `/{id}` shows state; POST `/{id}/cancel` requests cancellation; GET `/{id}/download` returns a completed artifact only. Management/download are restricted to the requesting admin. States Queued/Running/Succeeded/Failed/Cancelled, with explicit lease generation and cancellation flag. Export one row per paid order in the half-open interval, including order number, paid time, current status, currency, purchased quantity, and snapshotted order/discount/tax/shipping totals. Label these historical order totals, not net revenue after refunds. Never sum different currencies. Cap at 10,000 orders and 10 MiB; oversize jobs fail clearly. Artifacts expire after 24 hours and then download returns 410.

**Files and data:** Open [AdminReportsController](../src/Agora.Api/Controllers/AdminReportsController.cs), [ReportContracts](../src/Agora.Api/Contracts/ReportContracts.cs), [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs), and [Program.cs](../src/Agora.Api/Program.cs). Add `ReportExportJob`, a one-to-one bounded artifact blob, state/lease logic, and a worker. This does not require the webhook outbox.

**Implementation plan:**

1. Write the exact CSV column list and date/status/currency semantics. Note the new half-open range versus the current sales endpoint's inclusive upper bound; leave that old endpoint unchanged.
2. Add job/artifact schema, requester, query version, timestamps, lease generation, claim count, cancellation flag, content digest, and migration.
3. Implement create with parameter validation and at most ten nonterminal jobs per admin, enforcing the count/insert in a short write transaction. Repeated creates intentionally create new jobs.
4. Implement owned status/cancel/download endpoints. Never serve a Running job's partial buffer; return 409 until Succeeded, and 410 after artifact expiry.
5. Build a worker that claims one job atomically with a two-minute lease and at most three recoverable execution claims. Use fresh dependency scopes and expose a run-once method for deterministic tests.
6. In one bounded read snapshot, project at most 10,001 matching orders plus grouped item quantities. Reject oversize input and record sourceSnapshotAt; execution may occur later than request time.
7. Build CSV in a bounded buffer after the read transaction ends. Quote delimiters/newlines correctly, use invariant numeric/date formatting, and neutralize formula-like text cells while keeping numeric amount columns numeric.
8. Check cancellation/lease ownership before atomically saving artifact plus Succeeded state. A cancelled or stale worker cannot publish. Recover an expired job by recomputing locally; no external business action is repeated.
9. Add `ReportExportJobTests` covering controlled worker ticks, restart, cancellation, stale publication, size limits, and encoded malicious-looking order-number fixture text.
10. Add a cleanup worker pass that deletes at most 25 expired artifact blobs per tick while retaining job metadata and its expiry information. Document polling/download examples and Failed reason codes. Disable automatic worker activity in ordinary API fixtures and drive it explicitly in worker tests, including cleanup.

**Worked verification:** Queue an export, see Queued with no artifact, run one worker tick, then download matching rows. A cancelled Running job must never later become Succeeded through a delayed worker. Test a crash before publication, another admin's job ID, 10,001 orders, byte-limit failure, quoted commas/newlines, expired artifact, mixed currencies as separate labeled rows, and refunded orders retaining their historical total plus current status. Run `AdminReportsApiTests`.

**Rollout and scope:** Deploy schema/API first with the worker disabled, then enable it and monitor job age/failure counts. Keep the synchronous reports available. This is a bounded local artifact service, not a spreadsheet provider, public file host, or general-purpose task queue.

**Explain it back:** Why is “job accepted” different from “download ready”? Done means cancelled/stale work cannot publish and a failed export never produces a partial success file.

## SS-19: Cursor-based order history

**Status:** Proposed; not implemented.

**User story:** As a customer with many orders, I want to continue through my history without increasingly expensive page offsets, so older purchases remain straightforward to browse.

**Current code and learning:** MeController pages owned order history using offsets. Learn keyset pagination: continue after the last ordered key rather than asking the database to skip an ever-growing number of rows.

**Contract and acceptance criteria:** Add authenticated GET `/api/me/orders/feed?limit=25&cursor=...`, limit 1..100. Sort by CreatedAt descending, then immutable unique order Number using the same binary database collation in ordering and seek predicates. Initial request captures a created-time cutoff; later pages keep that upper bound. Return items, hasMore, and an opaque nextCursor, with no expensive total-count promise. Protect cursor integrity using the platform data-protection service; include version, customer ID, cutoff, last CreatedAt/Number, page limit, and 24-hour expiry. Reject malformed/expired/wrong-owner/changed-limit cursors with generic 400. This is live traversal bounded by a cutoff, not a frozen export: backdated inserts or ownership changes can affect later pages. Existing offset endpoints remain available.

**Files and data:** Read [MeController](../src/Agora.Api/Controllers/MeController.cs), [Order](../src/Agora.Domain/Entities/Order.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add a supporting customer/CreatedAt/Number index, a cursor DTO/protector, and `OrderHistoryFeedQuery`. No per-customer saved paging session is needed.

**Implementation plan:**

1. Write a sorted list containing three orders with exactly the same CreatedAt. Work out the strict “less than last time, or same time and lesser number” predicate for descending order.
2. Define cursor version/expiry/owner binding and use a dedicated data-protection purpose. Do not accept a merely base64-encoded unsigned JSON cursor as trusted input.
3. Add and inspect the composite index migration. Verify the actual SQLite timestamp conversion and Number collation used by queries.
4. Implement the initial owned query with CreatedAt <= captured cutoff and limit + 1 rows to determine hasMore.
5. Implement cursor unprotection/validation before query execution. Reapply customer ownership independently even after the cursor validates.
6. Add the keyset predicate using exactly the same stored ordering. Inspect generated SQL; if the provider cannot translate the string comparison, use a bounded parameterized key query with identical collation, not AsEnumerable or an interpolated SQL string.
7. Return only limit rows and create the next cursor from the last returned row, not the extra lookahead row. Keep the original cutoff and limit.
8. Configure a persistent data-protection key location for the intended single-host deployment; document that losing keys invalidates old cursors. Tests should use isolated key storage.
9. Add `OrderHistoryFeedApiTests`, tied-key fixtures, inserted-new-order traversal, protected-cursor tampering, and query-plan/index checks on a representative large fixture.
10. Document traversal semantics, missing totals, cursor expiry, and restarting from the first page after invalidation. Do not advertise snapshot isolation across separate HTTP requests.

**Worked verification:** With five orders and limit 2, fetch 2/2/1 without duplicates on an unchanged dataset. Insert a new normally timestamped order after page 1; it stays outside the captured cutoff. Test equal timestamps, a removed previously returned row, empty account, limit 100, tampering, B using A's cursor, limit changes, expiry, and backdated insertion behavior as documented. Run owned order-history tests.

**Rollout and scope:** Add the index and new route without replacing the old pagination contract. Compare generated SQL and representative query plans before claiming a performance gain. A snapshot export, arbitrary mutable-status filters, and bidirectional cursors are later features.

**Explain it back:** Why must the seek predicate use the same tie-breaker as the ORDER BY? Done means traversal handles equal timestamps and does not reintroduce a hidden offset scan.

## SS-20: Catalog synchronization feed

**Status:** Proposed; not implemented.

**User story:** As an integration developer, I want a catalog bootstrap plus an ordered change feed, so I can maintain a local product mirror without downloading the entire catalog after every change.

**Current code and learning:** Product APIs expose current state but no ordered mutation stream or deletion tombstones. Learn the relationship between an initial snapshot, a continuation watermark, and durable changes that survive deletion of their source entity.

**Contract and acceptance criteria:** Admin GET `/api/admin/catalog-sync/bootstrap` returns a version-1 product snapshot and committed sequence watermark in one consistent read. Cap bootstrap at 1,000 products/5 MiB; larger catalogs receive 422 requiring a future larger-export feature. GET `/changes?after=...&limit=...` returns ordered changes after a sequence, maximum 100 rows and 1 MiB per page, stopping before the next row would exceed the byte budget. Each record is Upsert with a complete catalog snapshot or Delete with product ID/revision only. Scope includes product metadata/category and tax IDs, variant base prices/options/weights, and images; it excludes inventory availability, reviews, taxonomy names, and quantity-price policies. Include inactive products because this is an administrative mirror. Reject individual snapshots over 256 KiB instead of truncating them. Old data starts at revision zero; no fictional historical changes are backfilled.

**Files and data:** Read [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs), [ProductContracts](../src/Agora.Api/Contracts/ProductContracts.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add Product.CatalogRevision, `CatalogChange` with SQLite monotonic integer sequence, payload version/byte count, and `CatalogFeedState` holding last committed and last purged sequence. Tombstones have no cascading product FK.

**Implementation plan:**

1. Draw bootstrap watermark 40, then Upsert 41 and Delete 42. Demonstrate how a consumer applies those changes to its local dictionary keyed by product ID.
2. Define explicit snapshot fields and payload serialization. Parent category/tax labels are outside the stream so their independent edits do not silently require product events.
3. Add revision/change/feed-state schema and migration. Inspect SQLite AUTOINCREMENT behavior so purging old rows cannot reuse a previously published sequence.
4. Centralize current catalog writes so each create/update/delete stages exactly one corresponding change and advances the product revision. Capture a tombstone before deleting the product graph.
5. Save product/change rows, then update the feed's last committed sequence after generated sequence values are available, all inside one local write transaction. A failed commit must publish neither state nor change.
6. Audit every writer: current product endpoints, plus imports, variant/image editors, or schema-driven edits only if those earlier proposals have actually been implemented. Inventory checkout writes do not emit catalog changes in version 1.
7. Implement bootstrap with one read transaction covering product data and watermark. Enforce row/byte limits before sending headers; consumers must replace their local mirror on bootstrap, not merge in stale deleted rows.
8. Implement changes with stable sequence ordering. Read bounded row-size metadata first, select a fitting prefix, then load those payloads together. Return the last delivered sequence and current high watermark; reject a future cursor rather than silently accepting it.
9. Add a bounded admin purge operation that removes only an oldest contiguous prefix older than 30 days, at most 1,000 rows per call, and advances the retention floor atomically. `after < lastPurgedSequence` returns 410 with bootstrap instructions. Never delete a recent barrier row merely to reach older timestamps behind it.
10. Add `CatalogSyncApiTests`, upgrade checks, bootstrap/write races, rollback, payload-cap, and purge-boundary tests. Document the writer cutover and consumer deduplication/checkpoint sequence.

**Worked verification:** Bootstrap includes A/B at watermark 10. Update A -> 11; delete B -> 12. A consumer applying changes has new A and no B. Re-reading from 10 repeats the same records safely. Test rolled-back product edits, old product revision zero, tombstones after source deletion, bootstrap concurrent with update, byte-limited prefix continuation, stale cursor 410, future cursor 400, and no event on a stock-only checkout change. Run product/catalog tests.

**Rollout and scope:** Audit oversized legacy products first; do not silently truncate them or allow an untracked writer. Deploy schema and all current write hooks together before exposing the feed. If SS-12 exists, a separately reviewed CatalogRead integration route may expose this contract; do not automatically grant new access through an unrelated key policy. This is a bounded product mirror protocol, not general database replication.

**Explain it back:** Why does a deletion need a surviving tombstone, and why must bootstrap and its watermark come from the same read snapshot? Done means a consumer can recover after interruption or retention expiry without guessing what it missed.

## Your implementation review worksheet

Use this for one story at a time. The questions repeat the same feature through user behavior, code flow, persisted state, and failure evidence.

| Prompt | Your notes |
| --- | --- |
| Story ID and one-sentence user promise | |
| Exact successful request and expected response | |
| Existing entry point and important service/entity method | |
| New stored data versus values calculated on read | |
| Owner/role/capability checks, including old routes | |
| Invariant enforced by the database or domain method | |
| Two competing requests and which one is allowed to win | |
| Crash point and durable records remaining afterward | |
| Retry/replay behavior and stable operation identity | |
| Legacy-row migration and compatibility behavior | |
| Request/query/payload/time limits | |
| Focused test results, migration evidence, final regression results | |
| Deployment order and what a safe rollback actually means | |
| Deliberately excluded behavior | |

For review, show one happy path, one authorization failure, one business-rule failure, and the story's most important race/restart/boundary case. For writes, inspect persisted state after a rejected operation. For read features, prove ordering, bounded work, and absence of writes. For schema changes, prove an upgrade from old data as well as a fresh database.

Then explain the feature three ways: as a user action, as a request moving through named files, and as a sequence of database states. If one explanation is difficult, return to its worked example before widening the feature. Progress toward senior work comes from making promises precise and testing the cases that could break them, not from adding more layers to every change.
