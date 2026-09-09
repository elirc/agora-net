# 30 mid-level feature stories with guided implementation plans

[AstraDocs home](README.md) | [Codebase map](03-find-your-way.md) | [Junior stories](16-junior-user-stories.md) | [API reference](../docs/api-reference.md)

**Planning document only. All 30 stories are proposed features, not implemented functionality.** Routes, fields, migrations, and test classes described as additions do not exist merely because they appear here. This document changes no application behavior.

These stories help you progress from editing a controller to owning a complete feature: define its contract, follow data across layers, protect ownership, handle competing writes, preserve existing behavior, and prove the result. Each story is bounded enough for a mid-level engineer to work through independently. Several deserve multiple sessions. Small steps make the work approachable; they do not make concurrency or money workflows inherently simple.

The stories add capabilities beyond the junior list. No junior story is a prerequisite. Existing wishlists already support renaming and deletion, and gift cards already support deactivation; those are deliberately not presented as new features here.

## How to work through one story

1. Pick one ID. Read its contract and verification examples before editing anything.
2. Open the linked entry points. Trace `request -> controller -> service/domain -> database -> response` on paper. Some reads legitimately stay in the controller/query layer.
3. Write down one successful request and one rejected request. Explain what the database should contain afterward.
4. Inspect `git status --short` and preserve existing work. Make one feature branch using your normal workflow.
5. Run the named existing tests to establish a baseline. Add the smallest proposed test, then implement only enough to satisfy the next step.
6. Stop at each numbered step and explain its purpose aloud. If the explanation is unclear, follow one concrete example through the debugger.
7. Run focused tests during development, then `dotnet test Agora.slnx` before review. Record actual outcomes, including setup failures separately from failed assertions.
8. Update the API reference and document migration/configuration changes. Submit one story with its examples, evidence, and limitations.

Commands below assume the repository root and the .NET SDK described in [the first-hour guide](../docs/learning/01-first-hour.md). For example:

```powershell
dotnet test Agora.slnx --filter "FullyQualifiedName~ProductsApiTests"
```

New filenames are suggestions shown in code formatting. Existing files are clickable. Replace the filter with your new test class once it exists. A plan's verification section tells you what to assert, not that these assertions have already passed.

## Shared implementation recipes

### A. Contracts, ownership, and validation

Use the repository's existing ProblemDetails conventions. Unless a story says otherwise, use 400 for malformed input, 401 for an unauthenticated private request, 403 for a non-admin on an admin route, 404 for an absent or other customer's private resource, 409 for a stale version or conflicting current state, and 422 for a well-formed request whose referenced values cannot be used. Define field limits in the request DTO and enforce important business rules below it too.

An order number or email address is not proof that the caller owns an account resource. New `/me` routes must compare the authenticated customer ID with the stored owner ID. A cart token is already a credential in the current guest flow; do not print it in logs. Admin routes require the existing admin authorization policy/role pattern. Nothing here requires a new external service, real payment, email sender, or frontend.

### B. Data changes and migrations

Start with [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add the entity and relationship, define required lengths and unique indexes, and then generate a named migration using the repository's existing EF tooling. Inspect the generated migration before applying it. Decide what every old row should receive: nullable values, zero revisions, or explicit opening records are different choices.

For a schema story, these are learner commands to run **after changing the model**; they have not been run for this planning document. The example name is for MS-01. Replace it with a descriptive name for your chosen story. The local tool version comes from [dotnet-tools.json](../dotnet-tools.json); keep migrations in the existing Infrastructure `Migrations` directory.

```powershell
dotnet tool restore
dotnet tool run dotnet-ef -- migrations add AddProductTags --project src/Agora.Infrastructure --startup-project src/Agora.Api --output-dir Migrations
```

Inspect the new migration and model snapshot together. If unrelated changes appear, investigate the model difference before applying anything. For upgrade tests, use the migrations API with an explicitly configured temporary database connection, first targeting the previous migration and then the new one. A migration command that merely generates files is not an upgrade test.

The normal [AgoraApiFactory](../tests/Agora.Tests/Integration/AgoraApiFactory.cs) uses `EnsureCreated`. That proves tests can use the new model; **it does not prove an existing database upgrades correctly**. For every schema story, also use an isolated, disposable SQLite file: migrate to the previous migration, insert representative old data, apply the new migration, and check preserved values, backfills, indexes, and relationships. Never perform this exercise on your working database. Use existing money/date mappings: money amounts are integer cents in storage, and timestamps use UTC ticks. Tax rates have separate precision rules.

### C. Competing requests and atomic local writes

An `expectedVersion` is the version the client last read. Compare it with the loaded row, change the entity, advance the version, and configure the version as an EF concurrency token. A pre-check alone cannot catch another writer between reading and saving. A parent version protects child membership only if **every membership-writing path advances that parent version**.

Audit all existing writers of an entity when adding a concurrency token, including deletes and reads that persist observation flags. EF checks mapped tokens even if that endpoint was written before your feature. If two stories add tokens to the same parent, preserve both mappings and handle a conflict in either; do not remove the earlier protection to make a test pass. Register new services/options in [Program.cs](../src/Agora.Api/Program.cs) and keep API DTO dependencies out of Domain and Infrastructure. The existing [DomainExceptionFilter](../src/Agora.Api/Filters/DomainExceptionFilter.cs) already maps EF concurrency exceptions to 409; reuse that mapping where appropriate.

Use one `SaveChangesAsync` for a complete tracked aggregate where possible. Use a transaction for multiple saves or reads that must stay consistent with a write. For SQLite count limits, acquire the local write transaction before reading the count; use a provider-supported non-deferred/immediate transaction, and attach EF commands to that same transaction. Test the exact provider behavior. Keep these transactions short and free of external calls. Unique indexes still enforce uniqueness. Translate only recognized constraint/concurrency failures; do not turn every database exception into a misleading 409. Handle known lock contention as a retryable conflict rather than a 500, without retrying external actions.

In tests, use separate contexts and independent SQLite connections for competing writers. Never call two operations concurrently on one `DbContext`. A stale-write test can load twice, save the first context, and then save the second. A real race needs an explicit barrier and a file-backed database or correctly configured shared database, not a timing-dependent sleep.

### D. Reliable examples and regression checks

Use [TestAuth](../tests/Agora.Tests/Integration/TestAuth.cs) and `WithDbAsync` for setup. Each scenario needs unique emails, slugs, and SKUs because a class fixture's database is shared. Ownership tests need two real customers and an existing resource belonging to the other customer. After rejected writes, reload in a fresh scope and prove there was no partial change. Keep gateways and webhook senders fake.

Filter before counting and paging. Add a stable ID tie-breaker to ordering. Batch related reads instead of querying once per item. Capture the clock once per operation when boundaries matter; use an injected `TimeProvider` with a controllable test implementation instead of sleeping. Do not claim a read is a reservation, a transaction is a distributed transaction, or a current-state report is a historical audit.

## Choose a story

| ID | Feature | Main learning | Schema change |
| --- | --- | --- | --- |
| [MS-01](#ms-01-product-tags) | Product tags | Many-to-many relationships and filtering | Yes |
| [MS-02](#ms-02-curated-product-collections) | Curated collections | Ordered membership and aggregate versions | Yes |
| [MS-03](#ms-03-product-comparison) | Compare products | Batch reads and stable response order | No |
| [MS-04](#ms-04-edit-variants-with-conflict-detection) | Edit existing variants | Optimistic concurrency and snapshots | Yes |
| [MS-05](#ms-05-manage-and-reorder-product-images) | Manage product images | Child writes and parent revisions | Yes |
| [MS-06](#ms-06-clone-a-product-as-a-draft) | Clone a draft product | Deep copying and transactional creation | No |
| [MS-07](#ms-07-atomic-bulk-stock-adjustments) | Bulk stock adjustment receipts | Local idempotency and atomic batches | Yes |
| [MS-08](#ms-08-per-variant-reorder-policies) | Reorder policies | Configuration versus derived values | Yes |
| [MS-09](#ms-09-replenishment-suggestions) | Stock replenishment suggestions | Net quantities and cohort reports | No |
| [MS-10](#ms-10-a-read-only-checkout-quote) | Checkout quotes | Shared calculations without side effects | No |
| [MS-11](#ms-11-merge-two-carts) | Merge carts | Two-aggregate atomic writes | No |
| [MS-12](#ms-12-reusable-cart-templates) | Cart templates | Stored intent versus live prices | Yes |
| [MS-13](#ms-13-saved-catalog-searches) | Saved searches | Versioned input and query reuse | Yes |
| [MS-14](#ms-14-private-wishlist-item-notes) | Wishlist notes | Field ownership and stale edits | Yes |
| [MS-15](#ms-15-copy-items-between-wishlists) | Copy wishlist items | Set operations and membership conflicts | Yes |
| [MS-16](#ms-16-recently-viewed-products) | Recent products | Upserts, retention, and clock control | Yes |
| [MS-17](#ms-17-rating-histograms-with-conditional-reads) | Rating histograms | Aggregation and HTTP validators | No |
| [MS-18](#ms-18-report-a-product-review) | Review reports | A separate moderation workflow | Yes |
| [MS-19](#ms-19-saved-checkout-defaults) | Checkout defaults | Input precedence and stale references | Yes |
| [MS-20](#ms-20-an-owned-order-timeline) | Order timeline | Combining events without inventing history | No |
| [MS-21](#ms-21-repeat-an-order-into-a-new-cart) | Reorder previous purchases | Historical snapshots versus current catalog | No |
| [MS-22](#ms-22-an-admin-packing-slip) | Printable packing slip | Safe HTML and operational projections | No |
| [MS-23](#ms-23-return-window-and-eligibility-preview) | Return eligibility | Shared policy and exact time boundaries | No |
| [MS-24](#ms-24-return-evidence-links) | Return evidence links | Private attachments and bounded collections | Yes |
| [MS-25](#ms-25-manual-shipment-tracking-history) | Shipment tracking | State transitions and append-only events | Yes |
| [MS-26](#ms-26-internal-order-support-notes) | Internal support notes | Separate admin data and author attribution | Yes |
| [MS-27](#ms-27-a-fulfillment-work-queue) | Fulfillment queue | Remaining quantities and efficient projections | No |
| [MS-28](#ms-28-scheduled-discount-start-times) | Scheduled discounts | Backward-compatible temporal rules | Yes |
| [MS-29](#ms-29-gift-card-transaction-history) | Gift-card ledger | Atomic financial records and backfills | Yes |
| [MS-30](#ms-30-webhook-delivery-health-report) | Webhook health report | Honest metrics from limited history | No |

Suggested first route: MS-03, MS-17, MS-14, MS-01, MS-04. Then choose a workflow you want to understand. MS-07, MS-10, MS-11, and MS-29 are the later exercises because their failure paths need particular care. Each stands alone; conditional integration notes explain what to do if you implement related stories later.

## MS-01: Product tags

**Status:** Planned; not implemented.

**User story:** As a shopper, I want to filter by a tag such as `summer`, so I can find related products across categories.

**Current behavior and learning:** Products have a category but no tag relationship. Learn why a tag belongs in a reusable table and why filtering through a relationship must happen before pagination. In plain terms: a category is one shelf; tags are labels that can appear on several shelves.

**Feature contract:** Add public `GET /api/tags`, admin `POST /api/admin/tags`, and admin `PUT /api/admin/products/{id}/tags`. A tag has a display name of 1..60 trimmed characters and a lowercase ASCII slug of 1..60 letters, digits, or single hyphen-separated segments. Slugs are unique and immutable. Assignment takes at most 20 distinct tag IDs and `expectedVersion`; an empty list clears membership. Add an optional single `tagSlug` to product search. An unknown valid slug returns an empty page. Return product tags sorted by slug plus a `tagVersion`. Tag renaming/deletion and multi-tag Boolean expressions are outside this story.

**Files and data:** Start in [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs), [ProductSearchRequest](../src/Agora.Api/Contracts/ProductSearchRequest.cs), and [ProductCatalogQuery](../src/Agora.Api/Queries/ProductCatalogQuery.cs). Add `Tag`, `ProductTag`, and `Product.TagVersion`; use the shared migration recipe. The join needs a unique product/tag pair, product cascade deletion, and restricted tag deletion.

**Implementation plan:**

1. Follow one existing category filter through validation, count, and page materialization. Sketch where the tag predicate will join it.
2. Add the entities, length rules, relationships, unique slug index, and concurrency token. Backfill existing products with revision zero and no tags.
3. Add tag list/create DTOs and endpoints. Normalize before checking uniqueness; translate the unique-index race into 409.
4. Add replacement input. Load the product and all current memberships, compare the expected revision, and validate every requested tag before changing any membership.
5. Compute additions and removals as sets, advance the product revision, and save the entire replacement atomically. Return the new revision.
6. Validate `tagSlug` once, then add an `Any` predicate before count/paging. Keep all existing filters and same-variant price/stock semantics.
7. Batch-load response tags for the page and update public product mapping without querying once per product.
8. Add `ProductTagsApiTests`, migration checks, and an API example showing creation, assignment, and search.

**Verification:** Tag products in two categories and show both in a tag-only search. Combining a category and tag must narrow the results. Verify unknown slug -> zero total, duplicate normalized slug -> 409, unknown assignment ID -> 422 with unchanged membership, stale replacement -> 409, and anonymous/customer writes denied. Re-run `CatalogSearchApiTests` and `ProductsApiTests`.

**Explain it back / completion:** Explain why filtering after `Skip` loses matches. Finish when tag assignment survives reload, concurrent replacement cannot silently win, old products remain readable, and the new filter preserves paging totals.

## MS-02: Curated product collections

**Status:** Planned; not implemented.

**User story:** As a merchandiser, I want to publish an ordered collection such as “Starter workspace,” so shoppers see a deliberately chosen product sequence.

**Current behavior and learning:** Catalog ordering is computed from product fields; there is no persisted editorial list. Learn ordered child rows and a parent revision. Think of a collection as a playlist: membership and order are part of one edit.

**Feature contract:** Admins create a collection, read it, and replace its title, publication flag, and complete ordered product-ID list using `expectedVersion`. Use `/api/admin/collections` and `/{id}`. Title is 1..120 characters; immutable slug follows MS-01's described syntax without requiring that story. A collection holds 0..100 distinct existing products. Public `GET /api/collections/{slug}` returns only published collections and active members, with page size 1..100, total count, and stable stored order. Draft or unknown slugs return 404. Unpublishing preserves membership. Product deletion removes its membership row.

**Files and data:** Read [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs) and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add `ProductCollection`, `CollectionItem`, `CollectionsController`, and DTOs. Store `Position` on each member, a unique collection/product pair, and a collection concurrency token. Do not require a unique position index that makes intermediate reorder updates collide.

**Implementation plan:**

1. Draw one collection with three product IDs and positions 0, 1, 2. Write the expected public result when the middle product is inactive.
2. Add tables, slug uniqueness, foreign keys, and revision mapping. Generate and inspect the migration.
3. Implement admin creation as an empty unpublished collection. Return its ID and revision.
4. Implement an admin read that includes inactive members and their IDs so an editor can round-trip the complete list.
5. Implement replacement: validate the entire input, reject duplicates and missing IDs with 422, check revision, then update membership/order and advance revision in one save.
6. Implement public lookup with the publication predicate in the query. Filter inactive members before count/paging and order by position then product ID.
7. Reuse or extract existing product response mapping; batch ratings and other related values for the returned page.
8. Add `CollectionsApiTests`, migration tests, and examples for publish, reorder, and unpublish.

**Verification:** Reorder `[A,B,C]` to `[C,A,B]`; reload and assert exact order. Make A inactive and assert `[C,B]` with total 2. Verify draft 404, duplicate slug 409, duplicate member 422, missing member leaves the old collection untouched, and a stale editor cannot overwrite the successful editor. Existing catalog search order must remain unchanged.

**Explain it back / completion:** Explain why changing a child position must advance the collection revision. Finish when draft/public behavior, member deletion, order preservation, and conflicting replacements are tested; scheduling and nested collections remain separate work.

## MS-03: Product comparison

**Status:** Planned; not implemented.

**User story:** As a shopper, I want to compare two to four products in one response, so I can inspect their choices without opening several detail pages.

**Current behavior and learning:** Product details exist individually. There is no comparison contract. Practice designing a read model, preventing N+1 queries, and preserving an order supplied by the client rather than the database.

**Feature contract:** Public `POST /api/products/compare` accepts an ordered array of 2..4 distinct product IDs. Malformed IDs/count/duplicates return 400. Any missing or inactive product makes the whole request 422 with the unusable IDs. On success, return products in request order: identity, name, category, images, approved review count/average, and variant summaries containing SKU, name, options, currency, amount, weight, and observed availability. Different currencies stay explicitly labeled; do not invent currency conversion or one cross-currency “cheapest” value. The response is current information, not a stock or price guarantee.

**Files and data:** Open [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs), [ProductCatalogQuery](../src/Agora.Api/Queries/ProductCatalogQuery.cs), and [ReviewsController](../src/Agora.Api/Controllers/ReviewsController.cs). No migration is needed. Proposed new types: `ProductComparisonRequest`, `ProductComparisonResponse`, and `ProductComparisonApiTests`.

**Implementation plan:**

1. Read the current product detail mapper and list exactly which values come from Product, Variant, Inventory, Category, Image, and Review.
2. Define a comparison DTO rather than serializing tracked entities. Write a two-product expected JSON example before implementation.
3. Validate count and uniqueness before any database read. Keep an ID-to-input-position map.
4. Load all requested active products and required child data in bounded queries using no tracking. Determine unusable IDs by comparing requested IDs with loaded IDs.
5. Return the whole-request error before building a partial response if any product is unusable.
6. Fetch approved review aggregates for all loaded products together. Reuse mapping helpers where practical without changing ordinary product routes.
7. Build each response, sort products by input position, and sort variants/images deterministically. Preserve explicit currency on every price.
8. Add integration tests and inspect SQL logging or a command interceptor to prove query count stays bounded when going from two to four products.

**Verification:** Request `[B,A]` and assert B is first even when A sorts first by name. Cover no reviews, no images, unavailable variants, different currencies, duplicate IDs, one missing product, and one inactive product. Confirm a failed comparison creates no rows or tracking observations. Run `ProductsApiTests` and `CatalogSearchApiTests`.

**Explain it back / completion:** Explain why an SQL `IN` condition does not preserve input order. Finish when the complete response is predictable, no partial success is hidden, and adding products does not add one query per product.

## MS-04: Edit variants with conflict detection

**Status:** Planned; not implemented.

**User story:** As a catalog administrator, I want to change a variant's name, price, weight, and options without silently overwriting another administrator's edit.

**Current behavior and learning:** Product updates edit product metadata, not an existing variant's complete commercial fields. Orders store item snapshots, while carts read live variants. Learn optimistic concurrency and the difference between historical and live data.

**Feature contract:** Add admin GET/PUT `/api/admin/products/{productId}/variants/{variantId}`. PUT requires `expectedVersion` and replaces editable fields: trimmed name 1..120, price amount 0..1,000,000 with at most two decimal places, weight 0..1,000,000 grams, and at most 20 option pairs with nonblank keys up to 60 and values up to 120 characters. Reject normalized duplicate keys. SKU, product identity, and currency are immutable in this feature. An unrelated variant ID returns 404; stale revision returns 409. GET and successful PUT expose `version`.

**Files and data:** Follow [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs), [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs), and [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs). Locate `ProductVariant` and `Money` with editor search. Add a variant version defaulting to zero and a domain edit method that advances it.

**Implementation plan:**

1. Trace how a cart response reads variant price and how checkout copies price/name into an OrderItem. Write those two behaviors beside your test plan.
2. Add the version property and EF concurrency mapping. Generate a migration that preserves existing variant values.
3. Implement input validation, keeping currency and SKU absent from the editable DTO. Reject excess price precision before constructing Money so rounding cannot hide invalid input.
4. Add the admin read scoped by both product and variant IDs.
5. Implement the domain edit method with the same essential bounds, copying the option dictionary rather than retaining the caller's mutable instance.
6. Implement PUT: compare revision, call the method, save, and translate a true concurrency conflict to 409. Return the updated representation.
7. Audit other existing variant-writing paths so future edits cannot bypass revision advancement; initial creation starts at zero.
8. Add `VariantEditingApiTests`, focused domain tests for validation, and an upgrade test for old variants.

**Verification:** Two clients read revision 0; one changes price to 24.50 and receives 1; the other's old edit fails and cannot restore the old price. A pre-existing order retains its purchased name/price, while an existing cart's next read sees the new price. Test bad precision, option duplicates, negative weight, non-admin access, and mismatched parent ID.

**Explain it back / completion:** Explain why this prevents lost admin edits but does not make price changes atomic with an external payment. Finish when stale edits fail, snapshots stay unchanged, and existing product/checkout tests pass.

## MS-05: Manage and reorder product images

**Status:** Planned; not implemented.

**User story:** As a merchandiser, I want to add, remove, and reorder a product's image links, so its gallery can evolve after product creation.

**Current behavior and learning:** Product images exist, but there is no complete post-creation gallery editing workflow. Practice ordered child changes protected by a parent revision. An image list is one editable document even though it occupies multiple database rows.

**Feature contract:** Add admin gallery GET plus POST `/api/admin/products/{id}/images`, PUT `.../images/order`, and DELETE `.../images/{imageId}?expectedVersion=...`. Every write requires the gallery revision and returns the new revision. POST accepts an absolute HTTP/HTTPS URL up to 2,000 characters and alt text up to 500; the server never fetches it. PUT takes the exact permutation of current image IDs, including an empty array for an empty gallery. New additions are limited to ten images; pre-existing larger galleries remain readable/reorderable and may be reduced. Removal compacts positions to start at zero. First ordered image is first in public output.

**Files and data:** Open [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs) and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs); locate `ProductImage` and existing image mapping. Add `Product.ImageRevision` as a concurrency token with default zero. Reuse existing image rows and their sort-order field.

**Implementation plan:**

1. Draw a gallery with IDs A/B/C. Work through adding D, moving C first, and removing B; write the positions after each operation.
2. Add the revision and migration. Do not rewrite old URLs or delete old rows during the upgrade.
3. Add gallery DTOs and admin GET exposing all image IDs, positions, and revision.
4. Implement POST with URL/length validation, revision check, and the addition cap. Add at the end and advance the parent revision in the same save.
5. Implement order replacement by checking duplicates and exact set equality before changing positions. Missing/extra IDs return 422 with no changes.
6. Implement DELETE scoped by product and image ID. Compare revision, delete, compact positions, and advance revision atomically.
7. Make public image ordering explicit: sort order, then ID. Audit every gallery-writing path so edits to an existing gallery advance its revision.
8. Add `ProductImagesApiTests`, stale-write cases, upgrade coverage, and gallery examples in the API reference.

**Verification:** Reload after every operation and assert IDs and positions. Test a stale reorder racing an addition, an image from another product, duplicate IDs, an eleventh addition, a pre-existing eleven-image gallery, and `javascript:`/relative URL rejection. Put HTML-like text in alt text and verify it remains plain JSON data. Run `ProductsApiTests`.

**Explain it back / completion:** Explain why a version on each image alone cannot protect the complete ordering. Finish when all gallery writes share the revision rule and no uploaded-file storage or network fetching has been introduced.

## MS-06: Clone a product as a draft

**Status:** Planned; not implemented.

**User story:** As a catalog administrator, I want to copy an existing product into an inactive draft, so I can create a related product without re-entering its options and images.

**Current behavior and learning:** Products can be created, but creation has no source-product option. Learn to distinguish reusable catalog data from identity, stock, and history. Copy the recipe, not the sales that happened using it.

**Feature contract:** Admin `POST /api/admin/products/{sourceId}/clone` accepts a new name, new slug, and an exact mapping from each source variant ID to a new SKU. Limit the source to 50 variants and reject larger sources with 422. Validate name/slug/SKU using current create rules; reject case-insensitive duplicates within the request and collisions with stored SKUs. Copy description, category, tax category, variant names/prices/currencies/weights/options, and image links/alt text/order. All entity IDs are new. The result is always inactive, every new inventory row starts at zero on-hand/reserved, and no reviews, cart memberships, order history, tags, or collection memberships are copied. Return 201 with the new product identity. Uniqueness conflicts return 409.

**Files and data:** Read [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs), [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs), and [ProductsApiTests](../tests/Agora.Tests/Integration/ProductsApiTests.cs). No schema change. Suggested helper: `ProductDraftCloner`; suggested test class: `ProductCloningApiTests`.

**Implementation plan:**

1. List every Product/Variant/Image/Inventory property and mark it “copy,” “new identity,” or “reset.” Resolve the list before writing the endpoint.
2. Define a request containing only new identity inputs. Require exactly one SKU mapping per source variant; reject missing, extra, and repeated IDs.
3. Load the source with all required children in one consistent read. A missing source returns 404; an inactive source is still clonable by an admin.
4. Validate the new slug and all SKUs before adding tracked entities. Reuse create validation instead of making a second incompatible rule set.
5. Construct a new object graph explicitly. Allocate new dictionaries for options and new child objects; do not attach source navigation objects as clone children.
6. Initialize new inventory rows to zero and force `IsActive=false` regardless of the source status.
7. Save the complete graph once. Let unique constraints resolve final races, translating only recognized conflicts. No partial draft may survive a failure.
8. Add tests and an API example that activates the draft only through a later, separate normal edit.

**Verification:** Clone an active, stocked, reviewed product and assert all copied fields plus all reset fields. Editing clone options must not change the source. A conflicting SKU must leave no product, image, variant, or inventory fragments. Test missing mapping, extra mapping, source with zero variants if permitted by existing data, and source over the limit.

**Explain it back / completion:** Explain why copying a navigation collection reference differs from creating new child rows. Finish when the source is untouched and the new draft has no inherited operational history.

## MS-07: Atomic bulk stock adjustments

**Status:** Planned; not implemented.

**User story:** As a warehouse administrator, I want to apply a counted-stock correction to several variants together and receive a reusable receipt, so a retry cannot apply the same correction twice.

**Current behavior and learning:** Inventory supports individual changes and a versioned stock invariant. There is no batch adjustment receipt. Learn atomic local writes and idempotency: either every correction happens once, or none happens.

**Feature contract:** Admin POST `/api/admin/inventory/adjustments` takes a client-generated GUID `operationId`, reason of 1..200 trimmed characters, and 1..50 distinct variant lines. Each line has nonzero signed `delta` between -1,000,000 and 1,000,000 and `expectedVersion`. Calculate using checked arithmetic; resulting on-hand must be between reserved and 1,000,000. A new batch returns 201 and before/after/version values. Replaying the same operation ID with the same normalized content returns 200 and the original receipt. Reusing that ID with changed content returns 409. One bad or stale line rejects the whole batch.

**Files and data:** Start in [InventoryController](../src/Agora.Api/Controllers/InventoryController.cs), [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs), and [InventoryItemTests](../tests/Agora.Tests/Unit/InventoryItemTests.cs). Add `InventoryAdjustmentBatch` and immutable `InventoryAdjustmentLine` rows, unique operation ID, actor ID, timestamp, canonical request fingerprint, and stock snapshots. Receipt lines preserve SKU/variant identity even if the catalog later changes.

**Implementation plan:**

1. Trace InventoryItem.SetStock and its version advancement. Compute the proposed absolute on-hand value from the delta, then reuse SetStock's reserved-stock invariant rather than setting fields directly.
2. Define normalized fingerprint content: trimmed reason plus lines sorted by variant ID, including delta and expected version. Never use dictionary iteration or raw JSON property order as identity.
3. Add receipt tables and the operation-ID constraint. Choose history-preserving foreign keys/snapshot fields and verify migration behavior.
4. Validate the input shape, then look up the operation ID before checking current stock. A completed replay must succeed even though its original expected versions are now old.
5. In a short local write transaction, recheck the operation ID, load all inventory rows, and validate all expected versions and proposed balances before mutation.
6. Apply every adjustment, build receipt rows from actual before/after values, and save stock plus receipt together. Commit only when the entire save succeeds.
7. If a competing request wins the operation-ID race, discard the failed context/transaction and read the committed receipt in a fresh scope. Compare fingerprints before returning it. Handle stock conflicts separately.
8. Add `BulkInventoryAdjustmentApiTests` with rollback and independent-connection race cases; document local-only retry behavior.

**Verification:** Start A=10/reserved=2 and B=8/reserved=0; deltas -3/+4 yield 7/12. Replay gives the same receipt and leaves 7/12. A delta taking A below 2 must leave both rows unchanged. Test stale B, missing variant, duplicate line, changed replay body, concurrent identical operation IDs, and caller attribution.

**Explain it back / completion:** Explain why “check whether operation exists, then save later” needs a database uniqueness rule. Finish when stock and receipt cannot diverge. This feature does not change checkout/payment idempotency.

## MS-08: Per-variant reorder policies

**Status:** Planned; not implemented.

**User story:** As an inventory administrator, I want a different reorder threshold and target for each variant, so frequently stocked items can use different rules from occasional items.

**Current behavior and learning:** Low-stock reporting reads current inventory with a shared threshold. There is no persisted policy per variant. Learn the distinction between configuration you store and a suggestion you calculate.

**Feature contract:** Admin GET/PUT `/api/admin/inventory/{variantId}/reorder-policy` manages a policy with threshold, target level, and version. Require `0 <= threshold <= targetLevel <= 1,000,000`. PUT requires nullable `expectedVersion`: null means create only; an integer means update that exact revision. Existing-policy create and missing-policy update return 409. GET for a real variant without a policy returns `hasOverride=false`, threshold 5, target 5, version null. Add paged `/api/admin/inventory/reorder-report`: include variants where available stock is at or below their effective threshold and return `max(0, targetLevel - available)` as suggested quantity. Sort suggested quantity descending, then variant ID. Availability remains on-hand minus reserved.

**Files and data:** Read [InventoryController](../src/Agora.Api/Controllers/InventoryController.cs), [AdminReportsController](../src/Agora.Api/Controllers/AdminReportsController.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add one `InventoryReorderPolicy` per variant, with unique variant ID, concurrency version, and updated timestamp. This story does not alter the existing low-stock endpoint's contract.

**Implementation plan:**

1. Work through on-hand 12/reserved 4 with threshold 8/target 20: available 8 qualifies and suggested quantity is 12.
2. Add the policy entity, unique relationship, cascade behavior on variant deletion, validation, and migration. Do not create policy rows for every existing variant; defaults are computed.
3. Implement admin GET with explicit default-versus-override information.
4. Implement create/update semantics, comparing expected revision and mapping recognized uniqueness/concurrency conflicts to 409.
5. Build the report as an inventory query with an optional policy join and effective default expressions. Filter qualifying rows before counting.
6. Apply validated page bounds and stable ordering, then project current inventory, effective policy, and suggestion without changing stock.
7. Add `ReorderPoliciesApiTests` and `ReorderReportApiTests`; include upgrade checks proving existing inventory remains untouched.
8. Document the difference between the policy threshold and target, and show one default row alongside an overridden row.

**Verification:** Cover exactly-at-threshold inclusion, above-threshold exclusion, reserved stock, default values, explicit zero values, stale updates, null-version create conflict, and missing variant 404. A GET/report must not insert a policy or adjust inventory. Re-run `InventoryApiTests` and `AdminReportsApiTests`.

**Explain it back / completion:** Explain why target minus on-hand would differ from target minus available. Finish when stored overrides and computed defaults are distinguishable, and report totals match the filtered dataset.

## MS-09: Replenishment suggestions

**Status:** Planned; not implemented.

**User story:** As a stock planner, I want suggested replenishment quantities based on recent net unit sales, so I can compare demand with currently available stock.

**Current behavior and learning:** Sales and low-stock reports exist separately. They do not estimate stock coverage. Learn cohort definitions, separate aggregates, and why joining two child collections can accidentally multiply quantities.

**Feature contract:** Admin GET `/api/admin/reports/replenishment` accepts `windowDays` 7..90 (default 30), `coverDays` 1..60 (default 14), and bounded paging. Capture `asOf`; the sales cohort has `PaidAt` in `[asOf-windowDays, asOf)`. Include paid, partially fulfilled, or fulfilled orders; exclude cancelled and fully refunded orders. For each surviving current active variant, net units are cohort ordered units minus quantities from currently approved returns on those same order lines. Divide by windowDays, multiply by coverDays, subtract current available stock, take the ceiling, and clamp at zero. Return net units, daily average, available units, suggested units, dates, and the formula inputs. Include only positive suggestions. This is advisory, with no purchase-order creation or automatic stock write.

**Files and data:** Open [AdminReportsController](../src/Agora.Api/Controllers/AdminReportsController.cs), [ReturnService](../src/Agora.Infrastructure/Services/ReturnService.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). No migration or dependency on MS-08. Suggested query type: `ReplenishmentReportQuery`.

**Implementation plan:**

1. Write a fixture with one order line and two approved return records. Hand-calculate its net units before querying anything.
2. Define the request bounds, half-open time interval, response DTO, and clock source. State that returns are counted by current approval state, even when approval occurred after the cohort period.
3. Query cohort order-line quantities grouped by variant. Separately aggregate approved return quantities restricted to those order lines.
4. Combine the aggregates by variant, then join current active variants and inventory. Do not join raw order lines, raw returns, and raw fulfillment lines into one multiplication-prone result.
5. Compute net demand and suggestions with sufficient intermediate precision; round only the final quantity upward. Flag or fail an impossible negative net quantity as data inconsistency rather than quietly recommending negative stock.
6. Filter positive suggestions, count, and page with suggestion descending then variant ID. Keep aggregation database-side where supported; verify generated SQLite SQL rather than loading every order into memory.
7. Add `ReplenishmentReportApiTests` with deterministic timestamps and exact expected quantities.
8. Document the cohort definition, absent historical variants, and the fact that this is a simple average rather than a forecast of seasonality.

**Verification:** In a 30-day window, 30 ordered units minus 6 approved returned units gives 0.8/day; 10 cover days and 3 available yields 5 suggested units. Requested/rejected returns do not reduce units. Test full refunds, cutoff timestamps, zero demand, deleted variants, equal-ranking pagination, and two return rows without duplicated sales.

**Explain it back / completion:** Explain why this report can change after a return is approved. Finish when the arithmetic is reproducible from the response and the query avoids double counting and unbounded application-side loading.

## MS-10: A read-only checkout quote

**Status:** Planned; not implemented.

**User story:** As a shopper, I want to preview my checkout totals before paying, so I can understand discounts, tax, shipping, and gift-card contribution.

**Current behavior and learning:** Checkout calculates totals as part of reserving stock and charging a payment. There is no dedicated side-effect-free quote. Learn to extract shared calculations without accidentally moving payment or stock mutations into a read operation.

**Feature contract:** Add POST `/api/checkout/quote` with cart token, email/address inputs, shipping selection, discount code, and gift-card code as applicable to current checkout; it has no payment token. Return `calculatedAt`, current `cartVersion`, currency, line totals, subtotal, discount, tax, shipping, gift-card contribution, and remaining payable amount. Use the same address ownership and pricing rules as checkout. Invalid selections produce matching validation failures; observed insufficient stock is 409. A quote is nonbinding. It creates no order, reservation, redemption, usage increment, cart edit, or webhook and makes no gateway call.

**Files and data:** Trace [CheckoutController](../src/Agora.Api/Controllers/CheckoutController.cs), [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs), [TaxService](../src/Agora.Infrastructure/Services/TaxService.cs), and [TotalsPipelineTests](../tests/Agora.Tests/Integration/TotalsPipelineTests.cs). No schema change. Proposed types: domain/application quote input/result, `CheckoutPricingService`, API quote DTOs, and `CheckoutQuoteApiTests`. Keep API contract types out of Domain/Infrastructure.

**Implementation plan:**

1. Mark every checkout line as load, validate, calculate, mutate, persist, or external call. Use [the checkout storyboard](07-checkout-storyboard.md) to check the sequence.
2. Add characterization tests for existing shipping, discount, tax rounding, and mixed gift/payment tender totals before moving code.
3. Define a calculation result containing all amounts and line-level inputs needed by both quote output and order creation. Capture one timestamp per operation.
4. Extract reusable selection/validation/calculation code. Preserve the existing order of discounts, shipping thresholds, tax allocation, and gift tender. It must not call Reserve, Redeem, RegisterUse, SaveChanges, or a gateway.
5. Make checkout use that result before its existing reservation/payment sequence. Preserve current payment failure behavior; this refactor does not solve payment recovery.
6. Add the quote endpoint using the shared path with read-only loading. Apply existing cart-token access semantics and saved-address ownership checks.
7. Add fake gateway/sender assertions and before/after database snapshots to prove that repeated quotes have no writes.
8. Run `TotalsPipelineTests`, `CheckoutApiTests`, `TaxGiftCardApiTests`, and `StockReservationEdgeTests`; document what can change between quote and payment.

**Verification:** For fixed inputs, quote and immediate successful checkout must report equal totals. Repeat a quote three times and assert unchanged inventory reservations, cart version, discount usage, gift balance, and order count. Test other customer's saved address, unavailable stock, expired codes, full gift coverage, and a price change between quote and checkout: checkout must recalculate.

**Explain it back / completion:** Explain why displaying 20.00 in a quote does not reserve either that price or the item. Finish when one calculation path serves both workflows and zero-side-effect assertions pass.

## MS-11: Merge two carts

**Status:** Planned; not implemented.

**User story:** As a signed-in shopper, I want to merge my guest cart into my account cart, so I can keep the items I selected before signing in.

**Current behavior and learning:** A guest cart can be claimed, but there is no defined two-cart merge. Learn how one request can change two aggregates while preserving their quantity, ownership, and version rules.

**Feature contract:** Authenticated POST `/api/me/carts/merge` takes source/target tokens and both expected versions. Target must belong to the caller; source must be unclaimed or belong to the caller. Another customer's cart is 404. Identical tokens are 400; an empty source is 422. Combine quantities by variant, never exceeding 99. If either copy of a variant is active, its merged line is active; otherwise it stays saved for later. Validate merged active quantities against current stock and product activity. For this bounded first version, require one currency across all merged lines, including saved lines; mixed currencies return 422. On success, retain existing target line IDs, clear all source items, and advance both cart versions atomically. Saved-only lines do not reserve stock or become active merely through merging.

**Files and data:** Open [CartsController](../src/Agora.Api/Controllers/CartsController.cs), [CartTests](../tests/Agora.Tests/Unit/CartTests.cs), and [CartSavedForLaterTests](../tests/Agora.Tests/Unit/CartSavedForLaterTests.cs). Locate Cart and the cart response mapper. No new column is required: Cart already has a version. Expose it additively in CartResponse and return both resulting versions from this action. Add a `CartMergeService` in the appropriate existing layer.

**Implementation plan:**

1. Make a table of four overlap cases: active/active, active/saved, saved/active, saved/saved. Calculate resulting quantity and state for each.
2. Add expected-version request fields and the response's existing cart version. Establish authorization before returning any source/target contents.
3. Load both carts and required live variant/inventory data in one short local transaction. Check both expected versions.
4. Build a proposed merged representation without editing either cart. Validate every quantity, the all-line currency rule, and every active line before applying anything. Read CartResponse.From: its initial subtotal currency currently comes from the first item, which may be saved; the single-currency rule avoids creating a response this mapper cannot total.
5. Add a domain operation that applies the validated state while preserving target IDs and using Cart's version/timestamp rules. Ensure saved-state changes do not accidentally add quantities twice.
6. Clear the source and save both carts together. If either version conflicts, roll back everything and return 409.
7. Map the updated target response and both versions. Document that an empty-source retry is rejected rather than applying a second merge.
8. Add `CartMergeApiTests` and run `CartsApiTests`, `SavedForLaterApiTests`, and cart domain tests.

**Verification:** Target active A=2 and source saved A=3 produce active A=5. Target saved B=1 plus source saved B=2 produce saved B=3. Test sum 100, unavailable active stock, stale source, stale target, foreign ownership, and unchanged rows after rejection. After success, source is empty and inventory is unchanged.

**Explain it back / completion:** Explain why validating and saving each line independently would break all-or-nothing behavior. Finish when both cart versions participate in conflict detection and every overlap case is explicit.

## MS-12: Reusable cart templates

**Status:** Planned; not implemented.

**User story:** As a repeat customer, I want to save a named set of items and add it to my cart later, so routine purchases take fewer steps.

**Current behavior and learning:** Saved-for-later items remain attached to one cart. There is no reusable account template. Learn to persist purchasing intent separately from prices and availability that must be checked again.

**Feature contract:** Under authenticated `/api/me/cart-templates`, create from an owned cart's active items, list/read owned templates, delete a template, and POST `/{id}/apply` to an owned target cart with `expectedCartVersion`. A name is 1..80 trimmed characters; allow at most ten templates per customer and 1..50 distinct lines per template. Store variant ID and quantity only, plus a display SKU/name snapshot for identifying a subsequently deleted item. Apply adds quantities to the target, activates overlapping saved lines, enforces 1..99, current activity, currency, and stock, and rejects the whole operation if any item is unusable. It never charges or reserves. No template editing or automatic recurring order is included.

**Files and data:** Read [MeController](../src/Agora.Api/Controllers/MeController.cs), [CartsController](../src/Agora.Api/Controllers/CartsController.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add `CartTemplate`/`CartTemplateLine`, customer cascade deletion, unique template/variant membership, and snapshot fields. Preserve lines after variant deletion using historical IDs without a destructive cascade. Expose Cart.Version if MS-11 has not already done so.

**Implementation plan:**

1. Trace active versus saved items and write a two-line template example. Explicitly omit unit prices, discount codes, payment tokens, and gift-card codes from stored data.
2. Add entities and migration, choosing historical variant-ID storage so missing catalog items remain explainable.
3. Implement create with owned-cart loading, line limits, and a copied snapshot. Serialize the customer count-check plus insert using shared recipe C so two ninth-to-tenth creations cannot exceed ten.
4. Implement owned list/detail/delete routes with deterministic ordering and no disclosure of another customer's template.
5. Define apply input and error details containing template line IDs/reasons. Load all live variants together; a missing variant must produce a useful error, not disappear silently.
6. Build and validate the complete proposed cart state before editing it. Require a single currency across all resulting lines, including existing saved lines, or return 422; this keeps the current CartResponse subtotal mapping valid. Reuse cart domain operations; if MS-11 exists, share the pure combination rules without requiring its endpoint.
7. Save the target once with version protection. Leave the template unchanged and return the current cart representation.
8. Add `CartTemplatesApiTests`, upgrade tests, and examples demonstrating a changed live price.

**Verification:** Save at 10.00, change the variant to 12.00 in a fixture, apply, and verify 12.00 is used. Test a deleted variant, active/saved overlap, aggregate quantity overflow, stale cart version, two owners, and concurrent creation at the account cap. A failed apply must not add the valid subset.

**Explain it back / completion:** Explain why a template's display snapshot is useful but cannot authorize an old price. Finish when template ownership, capacity, and atomic application are proved.

## MS-13: Saved catalog searches

**Status:** Planned; not implemented.

**User story:** As a shopper, I want to save a named catalog search and run it later, so I can revisit the same criteria without reconstructing filters.

**Current behavior and learning:** The catalog accepts structured filters but does not persist them per customer. Learn versioned stored input, whitelisting, and reuse of validation/query behavior.

**Feature contract:** Authenticated `/api/me/saved-searches` supports create/list/read/delete and GET `/{id}/results?page=...&pageSize=...`. Name is 1..80 characters; at most 50 searches per customer. Store a version-1 payload containing only current search text, category ID or slug, minimum/maximum price, currency, in-stock, active flag, and sort selection accepted by ProductSearchRequest. Do not persist page/pageSize, raw SQL, arbitrary property names, or filters from unimplemented stories. Results always use current catalog data and current public-search rules. Unknown future payload versions return a clear 409 explaining that the saved definition cannot be run. A removed category yields no matches rather than deleting the search.

**Files and data:** Start in [ProductSearchRequest](../src/Agora.Api/Contracts/ProductSearchRequest.cs), [ProductCatalogQuery](../src/Agora.Api/Queries/ProductCatalogQuery.cs), and [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs). Add `SavedCatalogSearch` with customer ID, name, schema version, bounded JSON definition, and creation timestamp. The JSON is serialized from a typed DTO, never accepted as an unchecked database query.

**Implementation plan:**

1. Read every currently supported ProductSearchRequest property and validation rule. Make an explicit persisted-field whitelist, retaining current combinations and public visibility semantics.
2. Add the entity and migration with customer cascade deletion and reasonable payload length. No catalog foreign key should erase a saved search when a category disappears.
3. Extract reusable filter validation if it currently lives only in the controller. Keep paging validation separate from the saved definition.
4. Implement create: authenticate, validate name/filters, serialize the typed version-1 definition, and enforce the 50-search cap inside a serialized local write transaction.
5. Implement owned CRUD reads/deletion, ordered by creation time then ID. Return the interpreted fields as well as their schema version.
6. Implement results: load by owner, reject unknown versions, deserialize the known DTO, revalidate, and combine it with the current request's page controls.
7. Reuse the existing catalog query and response mapping instead of duplicating filter predicates in the new controller.
8. Add `SavedSearchesApiTests`, migration coverage, and an example showing that new matching products appear in later runs.

**Verification:** Compare ordinary catalog results with saved-search results for the same criteria and sort. Test literal `%`/`_` search text, price/currency combinations, removed category, invalid saved version inserted by a fixture, owner isolation, changed paging, and account cap. Run `CatalogSearchApiTests` to protect existing filters.

**Explain it back / completion:** Explain why the payload version is about the stored definition, not the current catalog contents. Finish when one query implementation serves both entry points and stored input cannot bypass validation.

## MS-14: Private wishlist item notes

**Status:** Planned; not implemented.

**User story:** As a shopper, I want to attach a private note to one wishlist item, so I can remember the intended size, occasion, or recipient.

**Current behavior and learning:** Wishlist items store variant identity and stock-observation information, but no personal note. Learn field ownership, response boundaries, and conflict detection for independent edits.

**Feature contract:** Authenticated PUT `/api/me/wishlists/{wishlistId}/items/{itemId}/note` takes `note` and `expectedVersion`. Trim surrounding whitespace; blank or null clears the note; maximum length is 500 characters after trimming. Treat all text as plain text. The item must belong to the specified wishlist, and that wishlist must belong to the caller. Return the updated note and `noteVersion`. Stale note edits return 409. Include these fields in owned wishlist item responses, but never in product responses, cart lines, orders, or webhook payloads. Adding a note does not alter item quantity, stock, or back-in-stock observation.

**Files and data:** Open [WishlistsController](../src/Agora.Api/Controllers/WishlistsController.cs), [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs), and [WishlistsApiTests](../tests/Agora.Tests/Integration/WishlistsApiTests.cs). Locate WishlistItem and its mapper. Add nullable Note and integer NoteVersion defaulting to zero; configure the latter as a concurrency token.

**Implementation plan:**

1. Follow the existing ownership-scoped wishlist load and identify where stock observation is persisted during reads. Keep note updates separate from that observation helper.
2. Add note fields, limits, domain normalization/edit behavior, and migration. Old items must return null note/version zero.
3. Add request/response fields with clear null semantics. A missing expected version is invalid; clearing a note still requires one.
4. Implement a query scoped by customer, wishlist, and item IDs. Return 404 for every missing/foreign combination rather than probing unowned resources first.
5. Compare the note revision, update only note/revision, and save. Use EF's original-value token to catch an edit that arrives after the comparison.
6. Audit stock-observation saves for the newly introduced token: they must not overwrite notes, and a concurrent note edit must be handled by a bounded fresh read/reapply of observation, or a documented conflict, rather than an unexplained 500.
7. Update owned wishlist mapping and verify no unrelated mapper starts serializing the entity directly.
8. Add `WishlistNotesApiTests` plus upgrade coverage and run existing wishlist tests.

**Verification:** Save “gift for Sam,” clear it with whitespace, and reload each time. Test 500/501 characters, HTML-like input remaining literal text, two clients editing the same note, an item under the wrong wishlist ID, another customer's existing item, and a stock-observation read racing an edit. Cart movement must not copy the note into public data.

**Explain it back / completion:** Explain why knowing an item GUID is not authorization. Finish when note edits preserve the existing stock-observation workflow and stale writes cannot erase a newer note.

## MS-15: Copy items between wishlists

**Status:** Planned; not implemented.

**User story:** As a shopper, I want to copy selected items from one of my wishlists into another, so I can reuse selections without losing the original list.

**Current behavior and learning:** Wishlists support individual additions and moving an item to a cart, but no selected-item copy. Learn set operations, parent membership versions, and the difference between copying and moving.

**Feature contract:** Authenticated POST `/api/me/wishlists/{targetId}/copy-items` takes `sourceId`, 1..50 distinct source item IDs, and `expectedTargetVersion`. Both lists must belong to the caller and differ. Unknown/foreign lists return 404; an item not in the owned source returns 422. Copy selected variants not already present in the target; report added and skipped variant IDs separately. Existing target variants are successful skips. Source rows never change. Out-of-stock variants remain valid wishlist choices. Copied rows get new IDs/timestamps and fresh observation state based on current availability. Private notes, if MS-14 exists, are not copied in this slice.

**Files and data:** Read [WishlistsController](../src/Agora.Api/Controllers/WishlistsController.cs) and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add a `MembershipVersion` on Wishlist, expose it in owned responses, and configure concurrency. Keep the existing unique wishlist/variant constraint. No dependency on MS-14 or junior wishlist stories.

**Implementation plan:**

1. Work through source `[A,B,C]`, target `[B,D]`, selection `[A,B]`: result `[B,D,A]` conceptually, added A, skipped B, unchanged source.
2. Add the membership revision with zero backfill and response mapping. Inventory observation alone must not advance this revision.
3. Audit all current membership writes: add, remove, and move-to-cart; advance the parent revision whenever membership changes. If the junior clear feature was later implemented, include it too. Deletion must handle stale parent tokens consistently.
4. Implement request shape validation, then ownership-scoped loading of both lists and selected source items.
5. Build the candidate set by variant ID and subtract existing target variants. Validate the full selected item set before adding anything.
6. Inside a short transaction, compare target revision, create new rows, and advance revision only when membership actually changes. Use the unique constraint as final race protection.
7. Return added/skipped IDs in deterministic input-derived order and the resulting target version. An all-skipped repeat is a successful no-op when the supplied version is current.
8. Add `WishlistCopyApiTests` and run `WishlistsApiTests`, including existing add/remove/move behavior under the new token.

**Verification:** Test overlap, all skipped, unavailable variants, source==target, wrong-source item, two owners, stale target, and concurrent copying of the same variant. Reload both lists after every error. A competing target edit must not be silently erased, and copying must never remove source rows.

**Explain it back / completion:** Explain why a unique child index prevents duplicates but cannot by itself detect a stale membership edit. Finish when all existing membership paths participate in the new revision rule.

## MS-16: Recently viewed products

**Status:** Planned; not implemented.

**User story:** As a signed-in shopper, I want to revisit products I recently viewed and clear that history, so I can find an item I did not save.

**Current behavior and learning:** There is no per-account browsing history. Learn upserts, bounded retention, stable ordering, and why reads should not unexpectedly create tracking records.

**Feature contract:** Authenticated POST `/api/me/recent-products/{productId}` records an explicit view of an existing active product. GET `/api/me/recent-products` returns the latest 20 currently active products ordered by last-viewed time descending then product ID. DELETE on the collection clears only the caller's history. Store at most 50 unique products per customer; repeat views update one row and move it to the front. Use server time. Recording an absent/inactive product returns 404. Anonymous product reads and ordinary GET detail/list calls do not record anything. This feature has no cross-customer analytics, cookies, or external tracking integration.

**Files and data:** Read [MeController](../src/Agora.Api/Controllers/MeController.cs), [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add `RecentlyViewedProduct` with customer ID, product ID, last-viewed timestamp, unique customer/product pair, and an index supporting customer/time lookup. Cascade deletion from customer and product.

**Implementation plan:**

1. Write the sequence A, B, A and its expected result: A first, B second, two rows. Choose a controllable clock for the tests.
2. Add the entity, relationships, indexes, and migration. History begins empty for existing customers.
3. Implement POST with active-product validation and ownership derived only from authentication, never from a customer ID in the body.
4. Serialize the upsert and retention pass inside one short SQLite write transaction. Capture the time after acquiring the transaction so a delayed older writer cannot move last-viewed backward.
5. Insert or update the unique pair, then delete rows after the first 50 in the defined stable order. Save and commit together; recognized contention must not cause duplicate rows or leak a 500.
6. Implement GET using active-product filtering before taking 20. Batch-load product summaries and do not update timestamps while reading.
7. Implement scoped clear, returning 204 even when already empty. Add no global history-clear route in this story.
8. Add `RecentlyViewedApiTests`, migration checks, and an explicit-view API example.

**Verification:** Test A/B/A, 51 distinct views retaining exactly 50, tied timestamps, product deactivation reducing visible results, product deletion cleanup, two customers viewing the same product, and simultaneous repeated views. Advancing a fake clock must be enough; tests must not sleep. Verify ordinary product GET and recent-history GET create no history writes.

**Explain it back / completion:** Explain the difference between “latest 20 active products” and “latest 20 rows, then remove inactive products.” Finish when history remains bounded, private, explicitly recorded, and deterministic.

## MS-17: Rating histograms with conditional reads

**Status:** Planned; not implemented.

**User story:** As a shopper, I want to see how many approved reviews gave each star rating, so an average rating has useful context.

**Current behavior and learning:** Product responses include approved review aggregates, and reviews have moderation states. There is no five-bucket summary endpoint with an HTTP validator. Learn aggregation and conditional GET without introducing a stored cache.

**Feature contract:** Public GET `/api/products/{productId}/reviews/summary` follows the existing public review route's product visibility rule. A missing product returns 404. Return five buckets, ordered stars 1..5, including zero-count buckets; total approved count; and average rounded to two decimal places using the existing review-average convention. With no approved reviews, total is zero and average is null. Send a strong ETag derived from the canonical response bytes. A matching `If-None-Match` on a valid existing representation returns 304 with no response body. Support a list of validators and `*` using the framework header parser and GET comparison semantics. Do not store summary rows or add a cache service.

**Files and data:** Open [ReviewsController](../src/Agora.Api/Controllers/ReviewsController.cs), [ProductsController](../src/Agora.Api/Controllers/ProductsController.cs), and [ReviewsApiTests](../tests/Agora.Tests/Integration/ReviewsApiTests.cs). No migration. Suggested types: `ReviewSummaryResponse` and `ReviewSummaryApiTests`.

**Implementation plan:**

1. Follow approval, rejection, and edit-to-pending transitions. List which transitions should change the public histogram.
2. Define a stable DTO with fixed bucket order. Decide a canonical serialization path used for both response bytes and ETag calculation.
3. Verify product existence/visibility, then query only approved reviews grouped by rating. Materialize at most five groups; do not load every review body or customer.
4. Fill absent ratings with zero. Compute the weighted average from star times count divided by total, not the average of bucket averages.
5. Serialize the deterministic representation and hash its bytes. Avoid timestamps or random values in the representation, which would change the ETag on every read.
6. Parse `If-None-Match` with framework utilities, perform the appropriate weak comparison for GET, and return either 304/no body or 200/the representation with ETag.
7. Add tests where approval changes the content and validator, and pending edits remove a formerly approved review from the result.
8. Update the API reference with one initial GET and one conditional GET; run existing review/product tests.

**Verification:** Approved ratings `[5,5,3]` yield counts `[0,0,1,0,2]`, total 3, and average 4.33. Pending/rejected ratings do not count. Cover no reviews, matching/nonmatching validators, multiple validators, wildcard, weak validator matching, nonexistent product with wildcard, and 304 having an empty body.

**Explain it back / completion:** Explain why the ETag represents response content rather than the number of database rows. Finish when moderation changes are immediately reflected and conditional requests never return a stale stored summary.

## MS-18: Report a product review

**Status:** Planned; not implemented.

**User story:** As a signed-in shopper, I want to report an inappropriate approved review, so an administrator can inspect it through a dedicated queue.

**Current behavior and learning:** Reviews already have approval/rejection moderation, but readers cannot create reports. Learn a separate workflow whose resolution does not silently mutate its source entity.

**Feature contract:** Authenticated POST `/api/reviews/{reviewId}/reports` takes reason `Spam`, `Abuse`, or `OffTopic` and optional plain-text comment up to 500 characters. Only approved reviews are reportable; self-reporting is 422. Allow one report per customer/review, enforced by a unique index; repeat submission returns 409. Admin GET `/api/admin/review-reports` filters status with bounded paging. Admin PUT `/{id}/resolution` takes `expectedVersion`, outcome `Resolved` or `Dismissed`, and an internal note up to 500 characters. A report starts Open and can resolve once. Reporting/resolution never automatically approves, rejects, deletes, or edits a review. Customers receive only their submitted receipt, with no report list or reporter identities.

**Files and data:** Read [ReviewsController](../src/Agora.Api/Controllers/ReviewsController.cs), [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs), and [ReviewsApiTests](../tests/Agora.Tests/Integration/ReviewsApiTests.cs). Add `ReviewReport` with review/customer IDs, reason/comment, status, created/resolved times, resolving admin ID, resolution note, and concurrency version. Define review deletion to cascade reports in this bounded workflow; this is not permanent moderation audit storage.

**Implementation plan:**

1. Draw the report state machine: Open -> Resolved or Dismissed; both terminal. Draw the independent Review state machine beside it.
2. Add entity/domain transitions, unique pair constraint, indexes for status/time, and migration.
3. Implement create using the authenticated customer ID and current approved review. Validate named enum values explicitly; reject undefined numeric enum values.
4. Save the report and map only receipt fields. Translate the unique-pair race to 409. Do not serialize the full Review or Customer navigation.
5. Implement the admin queue ordered oldest first then ID, applying status filtering before count/paging. Include the review excerpt and moderation status needed for assessment, batch-loaded.
6. Implement resolution with expected-version comparison and the one-way domain transition. Attribute the action from admin claims and server time.
7. Keep actual review moderation on its existing explicit endpoint; show that endpoint as a separate action in documentation.
8. Add `ReviewReportsApiTests`, state-transition unit tests, and upgrade coverage.

**Verification:** Cover successful report, duplicate report race, self-report, pending review, invalid reason, non-admin queue access, stale resolution, and second resolution attempt. Reporting and resolving must leave the review's status/body unchanged. Queue paging must count only the requested report status.

**Explain it back / completion:** Explain why “resolved report” does not necessarily mean “removed review.” Finish when the two state machines remain separate and reporter/internal-note data cannot appear in public review output.

## MS-19: Saved checkout defaults

**Status:** Planned; not implemented.

**User story:** As a repeat customer, I want to opt into a saved delivery address and shipping method, so checkout can require less repeated input.

**Current behavior and learning:** Customers can save addresses and choose a shipping method, but no account-level preference pair is applied by explicit opt-in. Learn input precedence and stale references without breaking existing clients.

**Feature contract:** Authenticated GET/PUT `/api/me/checkout-preferences` manages optional saved address ID and shipping method code plus version. No saved row reads as empty defaults with version null. PUT uses null `expectedVersion` for create-only or an integer for updating that version; null preference fields clear them. Address must currently belong to the caller, and a selected shipping method must be active. Add optional `useSavedPreferences=false` to checkout input. When true, require authentication and use this precedence separately for address and shipping: explicit valid input, then saved preference, then existing fallback/required-input behavior. Invalid explicit input must fail rather than silently falling back. A still-referenced inactive/missing method returns 422 if needed. A deleted address is cleared by its relationship and follows normal fallback rules.

**Files and data:** Open [MeController](../src/Agora.Api/Controllers/MeController.cs), [CheckoutController](../src/Agora.Api/Controllers/CheckoutController.cs), and [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs). Add `CheckoutPreference` unique per customer, nullable address FK with set-null deletion, shipping code, and concurrency version. Revalidate address ownership at use time; the FK alone cannot enforce matching owners.

**Implementation plan:**

1. Trace current inline-address, saved-address, and default-shipping resolution. Write a precedence table for supplied/absent/invalid explicit values and supplied/absent/stale preferences.
2. Add the entity, uniqueness, relationships, and migration. Existing customers begin without preferences; checkout behavior remains unchanged until opt-in.
3. Implement owned GET/PUT with create/update version semantics and current address/method validation.
4. Add the optional flag to API and application checkout inputs, preserving its false default through every mapping.
5. Resolve preferences only when opted in. Validate explicit inputs first; use saved values only for omitted fields. Do not turn another customer's address into a usable default.
6. Perform final shipping activity and address ownership checks immediately before pricing. Keep preference resolution free of saves and external calls.
7. If MS-10 is implemented, accept the same flag in quote input and reuse this resolution path; otherwise leave a named integration note for that future story.
8. Add `CheckoutPreferencesApiTests`, migration checks, and regression cases in existing address/checkout tests.

**Verification:** Test explicit address overriding saved address, explicit shipping overriding saved shipping, opt-out preserving current behavior, anonymous opt-in rejection, foreign address on save/use, deleted address, inactive saved method, stale edit, and clearing both fields. An invalid explicit shipping code must not quietly use a preference.

**Explain it back / completion:** Explain why storing a previously valid reference does not make it permanently valid. Finish when every precedence-table row is covered and existing clients receive unchanged default behavior.

## MS-20: An owned order timeline

**Status:** Planned; not implemented.

**User story:** As a signed-in customer, I want one chronological view of my order's recorded milestones, so I can understand its payment, fulfillment, and return progress.

**Current behavior and learning:** Order timestamps, fulfillment records, and return records exist in different places. There is no combined account timeline. Learn how to combine available evidence without pretending that current state is a complete historical event log.

**Feature contract:** Authenticated GET `/api/me/orders/{number}/timeline` requires `Order.CustomerId` to match the caller. Return paged entries, page size 1..100, ordered timestamp ascending then deterministic event key. Include recorded Order created/paid/fulfilled/cancelled/refunded timestamps when present; each Fulfillment.CreatedAt; each ReturnRequest.CreatedAt; and its ProcessedAt with the corresponding current terminal return status. Give events stable keys such as `order-paid:{id}` and `return-processed:{id}`. Keep separate fulfillment-created and order-fully-fulfilled milestones even when their timestamps match. Never invent timestamps from current status, expose payment tokens, return comments, support notes, or report a complete audit history. Guest orders without account ownership return 404 on this new route.

**Files and data:** Read [MeController](../src/Agora.Api/Controllers/MeController.cs), [OrdersController](../src/Agora.Api/Controllers/OrdersController.cs), [FulfillmentService](../src/Agora.Infrastructure/Services/FulfillmentService.cs), and [ReturnService](../src/Agora.Infrastructure/Services/ReturnService.cs). No migration. Suggested read helper: `OrderTimelineQuery`; tests: `OrderTimelineApiTests`.

**Implementation plan:**

1. List actual available timestamp fields. Notice that ReturnRequest uses one ProcessedAt, not a separate timestamp for every possible transition.
2. Define event DTO fields: key, type, recorded timestamp, safe related record number/ID, and a small display label. Avoid unstructured entity serialization.
3. Implement the owned order lookup first. Do not reuse email/order-number guest authorization as account ownership.
4. Project milestone sources into compatible query shapes. Count sources and fetch a bounded page from each ordered source, then merge candidates by timestamp/key; for page offset N, at most N+pageSize candidates per source are sufficient. Reject excessive offsets using a documented maximum of 10,000.
5. Add fixed order milestones, sort the combined candidates, and apply the final offset/limit. Sum source counts for total entries; do not count only materialized candidates.
6. Emit processed-return entries only when a timestamp and appropriate terminal status exist. Document missing legacy timestamps as unavailable history.
7. Add deterministic fixtures with tied timestamps, multiple fulfillment records, and an approved return.
8. Add `OrderTimelineApiTests` and verify the endpoint makes no writes or external calls.

**Verification:** Assert exact event keys/order for a paid, partially shipped, fully shipped, then returned order. Test equal timestamps across sources, page boundaries, missing optional timestamps, second-customer access, and guest-order access. Explicitly assert absence of email, payment identifiers, comments, and internal notes from the DTO.

**Explain it back / completion:** Explain why the current order status cannot reconstruct every past transition. Finish when each displayed milestone has an actual source timestamp and ownership is enforced before related data loads.

## MS-21: Repeat an order into a new cart

**Status:** Planned; not implemented.

**User story:** As a customer, I want to start a new cart from a previous order, so I can buy the same items again at today's prices.

**Current behavior and learning:** Owned order history exists, but it cannot create a repeat-purchase cart. Learn to resolve historical order-line identities against the current catalog and make partial failure explicit.

**Feature contract:** Authenticated POST `/api/me/orders/{number}/reorder` accepts no price or owner inputs. The order must belong to the caller and cannot be Pending; cancelled/refunded historical orders are allowed as shopping references. Support 1..50 distinct variant lines after grouping historical lines by ProductVariantId. Every resulting quantity must be 1..99. All variants must still exist under active products, share a currency, and have sufficient currently available stock. If any line is unusable, return 422 with snapshot SKU and reason and create no cart. Otherwise return 201 with a new owned cart using current variant data. Copy no payment information, discount, gift card, shipping address, or order status. Repeated successful requests intentionally create separate carts.

**Files and data:** Open [MeController](../src/Agora.Api/Controllers/MeController.cs), [CartsController](../src/Agora.Api/Controllers/CartsController.cs), [OrderService](../src/Agora.Infrastructure/Services/OrderService.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). No migration. Suggested helper/test class: `OrderReorderService` / `OrderReorderApiTests`.

**Implementation plan:**

1. Compare OrderItem's snapshot fields with CartItem's live variant relationship. Write which source supplies identity, quantity, display name, and price in the new cart.
2. Add the owned route and load the order by number plus authenticated customer ID. Reject Pending before starting creation.
3. Group quantities by historical variant ID using checked arithmetic. Validate the line/count limits before loading catalog details.
4. Fetch current variants, products, and inventory together. Match by immutable ID, not SKU: a new product reusing an old SKU is not automatically the same purchase.
5. Build all line-level failures first, including deleted/inactive variants, quantities over the limit, currency mismatch, and unavailable stock. Return them without persisting anything.
6. Create a new customer-owned Cart, add all valid lines with existing domain methods, and save the complete graph once.
7. Return the standard cart representation and document that price/stock are observed now and checkout still revalidates them. Do not reserve inventory during reorder.
8. Add `OrderReorderApiTests`; run cart, order-history, and checkout integration tests relevant to the touched mapping.

**Verification:** Buy at 15.00, change live price to 18.00, reorder, and assert new cart price 18.00 while original order remains 15.00. Delete one variant and prove no valid subset is saved. Test changed SKU with the same ID, reused SKU with a different ID, foreign owner, Pending, cancelled history, insufficient stock, and repeated success producing different cart tokens.

**Explain it back / completion:** Explain why a SKU match is weaker than the historical variant ID for this operation. Finish when the original order is untouched, every new cart is wholly valid at creation time, and no payment occurs.

## MS-22: An admin packing slip

**Status:** Planned; not implemented.

**User story:** As a warehouse administrator, I want a printable order packing slip, so I can identify the destination and item quantities while preparing shipments.

**Current behavior and learning:** Order and fulfillment JSON exist, but there is no printable projection. Learn safe HTML output and how operational documents should choose their data deliberately.

**Feature contract:** Admin GET `/api/admin/orders/{number}/packing-slip` returns `text/html; charset=utf-8` with order number/date, shipping-address snapshot, and ordered SKU/product/variant names, ordered quantity, fulfilled quantity, and remaining quantity. Allow Paid, PartiallyFulfilled, and Fulfilled orders; other states return 409. Unknown number returns 404. Limit to 500 order lines with a clear 422 for larger legacy records. Output is self-contained, with small inline print CSS, no scripts, remote images/fonts, tracking calls, prices, payment identifiers, gift-card codes, or customer-account metadata. HTML-encode every dynamic value. Generating the slip does not mark anything printed, shipped, or fulfilled.

**Files and data:** Read [OrdersController](../src/Agora.Api/Controllers/OrdersController.cs), [FulfillmentService](../src/Agora.Infrastructure/Services/FulfillmentService.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). No migration or PDF package. Proposed types: `PackingSlipModel`, `PackingSlipRenderer`, and `PackingSlipApiTests`.

**Implementation plan:**

1. Sketch the printed page using one order with two lines and one partial fulfillment. Identify the address fields already stored on the order.
2. Define a narrow rendering model with only allowed fields. Load by admin-authorized route, validate status/count, and avoid passing tracked entities to the renderer.
3. Aggregate fulfillment quantities by OrderItemId separately, then combine them with order-line snapshots. Remaining is ordered minus fulfilled; report an impossible negative value as a consistency error.
4. Sort lines deterministically by snapshot SKU then order-item ID. Use the historical names/address even if catalog or address-book data has changed.
5. Implement rendering with the platform HTML encoder at every text insertion. Keep markup/CSS static, and never interpolate a dynamic value into raw HTML or CSS.
6. Set the content type and appropriate private/no-store caching headers for the address-bearing document.
7. Add renderer tests for escaping and API tests for authorization, counts, status, and excluded data.
8. Open one local response in a browser's print preview and inspect page wrapping, long names, and multi-page tables. Record this manual check without adding an automated browser stack.

**Verification:** An ordered quantity of 5 with 2 fulfilled prints ordered 5, fulfilled 2, remaining 3. Insert `<script>` text in a fixture name and verify encoded text with no executable element. Test changed live address, no fulfillment, full fulfillment, missing order, non-admin, forbidden states, and the line cap.

**Explain it back / completion:** Explain why a product's current name is inappropriate for an old order's packing slip. Finish when the page prints legibly, exposes only operational fields, and performs no writes.

## MS-23: Return window and eligibility preview

**Status:** Planned; not implemented.

**User story:** As a customer, I want to see which purchased quantities are still returnable and when the return window closes, so I can make a valid request.

**Current behavior and learning:** Returns require a fully fulfilled order and subtract quantities already requested or approved. There is no configurable time window or owned eligibility preview. Learn to share policy between a preview and the command that enforces it.

**Feature contract:** Add optional `ReturnPolicy.WindowDays`; unset/null disables the window and preserves current behavior. When set, accept only 1..365 at startup. A new return is allowed only while `now < FulfilledAt + WindowDays`, in addition to existing status/quantity rules. Exactly at expiry is too late. A configured window with missing FulfilledAt is ineligible with an explicit reason. Authenticated GET `/api/me/orders/{number}/return-eligibility` requires actual account ownership and returns evaluatedAt, nullable deadline, eligibility/reasons, and remaining quantity plus estimated refund for each order line. Estimates use existing discount/tax allocation; they do not issue refunds. Previously submitted returns can still be approved after the window expires.

**Files and data:** Read [ReturnService](../src/Agora.Infrastructure/Services/ReturnService.cs), [ReturnsController](../src/Agora.Api/Controllers/ReturnsController.cs), [ReturnRequestTests](../tests/Agora.Tests/Unit/ReturnRequestTests.cs), and [ReturnsApiTests](../tests/Agora.Tests/Integration/ReturnsApiTests.cs). No migration. Add `ReturnPolicyOptions`, a policy/calculation helper, and clock registration/configuration through the existing startup structure.

**Implementation plan:**

1. Trace return creation from ownership validation through remaining-quantity aggregation and refund calculation. Write the existing formula before extracting it.
2. Define and validate nullable configuration at startup. Leave sample configuration disabled by default and show 30 days as an opt-in example.
3. Inject a clock and capture one `now` for each operation. Implement a pure time/status eligibility function with explicit reason codes.
4. Extract remaining-quantity and refund-estimate logic so preview and creation use identical rules. Count Requested and Approved returns, excluding rejected/cancelled ones.
5. Enforce the new policy inside ReturnService creation so every existing entry point receives the rule. Preserve existing authorization there; the new `/me` read uses the stronger account-owner check separately.
6. Implement the preview without writes, gateway calls, reservations, or changing return statuses.
7. Keep approval behavior unchanged for already requested returns. Document that eligibility can change after preview because another request can consume quantities.
8. Add `ReturnEligibilityApiTests`, pure boundary tests, and regression tests for existing disabled-policy behavior.

**Verification:** Fully fulfill at a fixed timestamp and test one tick before, exactly at, and one tick after the 30-day deadline. With quantity 5, requested 1, approved 2, rejected 1, remaining is 2. Test disabled policy, missing timestamp, partial fulfillment, foreign owner, invalid configuration, and approval after expiry of an earlier valid request.

**Explain it back / completion:** Explain why checking the window only in the preview would not enforce it. Finish when preview and creation share calculations and no clock-dependent test sleeps.

## MS-24: Return evidence links

**Status:** Planned; not implemented.

**User story:** As an account customer, I want to attach a few descriptive evidence links to my return request, so support can see additional context without a file-upload system.

**Current behavior and learning:** Return requests have a comment, but no structured evidence collection. Learn scoped child resources, collection limits, and keeping supplementary data separate from refund decisions.

**Feature contract:** Authenticated GET/POST `/api/me/returns/{number}/evidence` and DELETE `.../evidence/{id}` operate only when the linked order's CustomerId matches the caller. This feature does not support guest evidence access by email. Store up to five links per return: absolute HTTPS URL up to 2,000 characters, optional plain-text description up to 200, author customer ID, and server timestamp. Reject URL user-info credentials. Admin GET `/api/admin/returns/{number}/evidence` can inspect them. Evidence may be added or removed in any return state; timestamps make post-decision additions visible, and evidence edits never reopen, approve, refund, or recalculate a return. The API neither fetches URLs nor promises their contents are trusted or still available.

**Files and data:** Read [ReturnsController](../src/Agora.Api/Controllers/ReturnsController.cs), [ReturnService](../src/Agora.Infrastructure/Services/ReturnService.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add `ReturnEvidence` with return FK/cascade, author ID, URL, description, and created timestamp. Do not add a return-state concurrency token merely to protect this independent collection: that would also affect existing refund saves.

**Implementation plan:**

1. Trace how a ReturnRequest links to its Order. Write the account-owner predicate using Order.CustomerId; do not substitute matching request email.
2. Add the evidence entity, field bounds, relationship, supporting return/time index, and migration. Existing returns start with an empty collection.
3. Define DTOs and parse URLs with the platform URI parser. Require HTTPS, a host, no user-info, and valid length; never issue a probe request.
4. Implement owned POST with authorization, then serialize the count-check plus insert in a short local write transaction. Return 409 when five links already exist.
5. Implement owned GET and admin GET ordered creation time then ID. Return only evidence fields and the return identity needed by that route.
6. Implement DELETE scoped to both return and evidence ID. Unknown/foreign children are 404. Removing evidence affects no return state or monetary amount.
7. Ensure existing public/guest return responses do not start embedding evidence through entity serialization.
8. Add `ReturnEvidenceApiTests`, migration checks, and documentation explicitly showing a link added after approval as supplemental context.

**Verification:** Test valid add/list/delete, sixth-link rejection, two concurrent additions when four exist, wrong child ID, another customer's order, guest order, HTTP/relative/user-info URL rejection, and additions after approval. Snapshot refund amount/status and fake gateway call count before and after evidence edits; all must remain unchanged.

**Explain it back / completion:** Explain why storing a URL differs from uploading or verifying a file. Finish when the cap holds under competition and supplementary evidence cannot influence payment/state transitions implicitly.

## MS-25: Manual shipment tracking history

**Status:** Planned; not implemented.

**User story:** As a customer, I want to see manually recorded shipment progress, so I can distinguish warehouse fulfillment from delivery to my door.

**Current behavior and learning:** Fulfillments record carrier, tracking number, creation time, and shipped quantities. Order.Fulfilled means all ordered quantities have fulfillment coverage; it does not mean a carrier delivered them. Learn a separate state machine with an append-only local history.

**Feature contract:** Add admin GET/POST `/api/admin/fulfillments/{id}/tracking-events`, with POST taking `expectedVersion`, named status, and optional plain-text message up to 200 characters. New and migrated fulfillments start `Unknown`, version 0, no invented past events. Allowed transitions: Unknown -> InTransit or Exception; InTransit -> OutForDelivery, Delivered, or Exception; OutForDelivery -> Delivered or Exception; Exception -> InTransit, OutForDelivery, or Delivered. Delivered is terminal, and same-state submissions are conflicts. Each accepted event has server-recorded time and a sequence number. Authenticated GET `/api/me/orders/{number}/fulfillments/{id}/tracking-events` requires owned order plus matching fulfillment. No external carrier calls or automatic order-state changes.

**Files and data:** Open [FulfillmentService](../src/Agora.Infrastructure/Services/FulfillmentService.cs), [OrdersController](../src/Agora.Api/Controllers/OrdersController.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add tracking status/version to Fulfillment and a `ShipmentTrackingEvent` child with unique fulfillment/sequence, state, message, actor, and timestamp. Keep private admin actor IDs out of customer DTOs.

**Implementation plan:**

1. Draw the allowed transition table and write one rejected example per terminal/invalid move. Put the Order state diagram beside it to show they serve different purposes.
2. Add the enum, tracking transition method, revision token, event entity, and migration with Unknown backfill.
3. Implement admin GET exposing current status/revision and events ordered by sequence, with page size 1..100 for history.
4. Implement POST: validate a defined named enum, load fulfillment, compare revision, validate transition, capture time, and create the next event.
5. Save new status, advanced revision, and event in the same transaction/save. A stale writer must create neither a state change nor an orphan event.
6. Implement the customer read with both ownership and fulfillment-parent checks, mapping only customer-visible fields.
7. Keep FulfillmentService's quantity-coverage behavior intact. Do not set Order.Fulfilled when a tracking event says Delivered or downgrade it on Exception.
8. Add `ShipmentTrackingApiTests`, transition unit tests, and migration coverage; run `FulfillmentsApiTests` and order fulfillment-state tests.

**Verification:** Record InTransit -> Exception -> InTransit -> Delivered and assert sequence 1..4, current Delivered, version 4. Test Delivered -> InTransit rejection, duplicate status, stale parallel update, foreign order, fulfillment attached to another order, and legacy Unknown with empty history. Stock and Order.Status must not change.

**Explain it back / completion:** Explain why “all items assigned to shipments” and “shipment delivered” need different states. Finish when every accepted transition has exactly one saved event and no carrier integration is implied.

## MS-26: Internal order support notes

**Status:** Planned; not implemented.

**User story:** As a support administrator, I want to leave internal notes on an order, so another administrator can understand prior investigation without showing those notes to the customer.

**Current behavior and learning:** Orders contain purchase information and lifecycle fields, but no separate internal discussion record. Learn author attribution, explicit data boundaries, and immutable additions that do not require an edit-conflict workflow.

**Feature contract:** Admin GET/POST `/api/admin/orders/{number}/notes` lists or adds notes. Body is required, trimmed plain text of 1..1,000 characters. The server assigns note ID, authenticated admin ID, and creation timestamp; clients cannot supply author/time. New notes are allowed for any non-Pending order. Pending returns 409 so the feature does not attach operational records to checkout's temporary pending order. List uses page 1 by default, page size 20 with maximum 100, creation descending then ID. Notes are immutable in this slice: no edit/delete endpoint, no notifications, and no customer message sending. Never include them in public OrderResponse, owned history/timeline, packing slips, or webhooks.

**Files and data:** Read [OrdersController](../src/Agora.Api/Controllers/OrdersController.cs), [OrderService](../src/Agora.Infrastructure/Services/OrderService.cs), [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add `OrderSupportNote` with order FK/cascade, actor identifier snapshot, text, timestamp, and order/time index. Do not require a live admin navigation to display a past note if that account is later removed.

**Implementation plan:**

1. Trace pending order creation/removal in checkout and public order mapping. Mark why note creation excludes Pending and why notes need their own DTO.
2. Add the entity, length validation, relationship, and migration. Old orders begin with no notes.
3. Implement admin POST with role authorization, order lookup, state check, trimmed text validation, and server-derived actor/time.
4. Save the new note alone; avoid modifying Order totals/status/timestamps just because a note was added. Return 201 with an admin-only receipt.
5. Implement list with bounded/overflow-safe paging and stable ordering. Count before paging and return only notes for the requested order.
6. Review every order response and webhook payload mapper for explicit field selection. Do not expose a notes navigation property by switching to entity serialization.
7. Add `OrderSupportNotesApiTests` with two admins to prove correct attribution and stable history.
8. Document the intended internal use and demonstrate that customer order requests remain unchanged.

**Verification:** Add two notes at a tied timestamp and assert deterministic paging. Test blank/oversized content, Pending rejection, anonymous/customer denial, missing order, client attempts to supply an author, and a subsequent deleted admin account if supported by fixtures. Search public order, timeline, packing-slip, and webhook responses for a unique marker from the note; it must be absent.

**Explain it back / completion:** Explain why “admin-only endpoint” is insufficient if the same field later leaks through a public mapper. Finish when attribution is server-controlled and internal content stays confined to admin note DTOs.

## MS-27: A fulfillment work queue

**Status:** Planned; not implemented.

**User story:** As a warehouse administrator, I want a queue of paid orders with quantities still to fulfill, so I can identify the remaining packing work.

**Current behavior and learning:** Fulfillments can cover some or all order lines, but there is no consolidated queue of remaining quantities. Learn to derive work from ordered minus fulfilled quantities and to avoid deducting stock twice.

**Feature contract:** Admin GET `/api/admin/fulfillment-queue` supports page/pageSize (default 20, maximum 100) and optional paired `paidFrom`/`paidTo` timestamps. If omitted, include all outstanding orders; if supplied, require an increasing half-open interval of at most 90 days. Include only Paid or PartiallyFulfilled orders with at least one positive remaining line. Return order number, paid time, shipping method snapshot, and lines with order-item ID, snapshot SKU/name, ordered/fulfilled/remaining quantity. Sort oldest paid time first then order ID. Current stock is not a queue eligibility test: checkout already committed stock before fulfillment. Reading the queue does not reserve, restock, fulfill, or change an order.

**Files and data:** Start in [FulfillmentService](../src/Agora.Infrastructure/Services/FulfillmentService.cs), [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs), [AdminReportsController](../src/Agora.Api/Controllers/AdminReportsController.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). No migration is required. Suggested query/test: `FulfillmentQueueQuery` / `FulfillmentQueueApiTests`.

**Implementation plan:**

1. Trace inventory Reserve/Commit during checkout and line coverage during fulfillment. Draw two separate equations: available stock = on-hand minus reserved; remaining work = ordered minus fulfilled.
2. Define filter validation and output DTOs. Return 400 for a missing date partner, reversed/equal dates, excess window, invalid page size, or unsafe page offset.
3. Build an order query with eligible statuses/date predicates plus an existence predicate for a line with remaining quantity. Compute fulfilled sums by OrderItemId with zero for no fulfillment rows.
4. Count eligible orders, then select the ordered page of IDs and order snapshots. Do not page all paid orders and discard fully covered ones afterward.
5. Batch-load order lines and grouped fulfillment totals for those IDs. Keep the number of database commands fixed rather than making one request per order.
6. Build positive remaining-line DTOs and sort them by SKU then order-item ID. Detect over-fulfilled negative values as a consistency error; never silently turn them into more packing work.
7. Add fixtures for multiple partial shipments and inspect generated SQLite queries with logging or a command interceptor.
8. Add `FulfillmentQueueApiTests` and re-run `FulfillmentsApiTests` plus checkout stock tests.

**Verification:** Order A has quantity 5 with shipments of 2 and 1, so remaining is 2. A second fully covered line is omitted. Verify full orders disappear, Pending/Cancelled/Refunded orders do not appear, dates use the promised boundary, and equal paid times page consistently. An order with remaining work and current on-hand zero must still appear.

**Explain it back / completion:** Explain why comparing remaining work with on-hand can falsely label an already-paid order as unfulfillable. Finish when counts and lines agree, child joins do not multiply shipped quantities, and queue reads leave inventory unchanged.

## MS-28: Scheduled discount start times

**Status:** Planned; not implemented.

**User story:** As a promotion administrator, I want a discount to begin at a chosen instant, so I can configure it ahead of time without manually activating it at launch.

**Current behavior and learning:** DiscountCode has active, expiry, and usage-limit rules but no start time. Learn an additive temporal rule, exact boundaries, and a backward-compatible nullable migration.

**Feature contract:** Add nullable `StartsAt` to DiscountCode and existing create/update/read contracts. A discount remains redeemable only if all existing conditions hold and either StartsAt is null or `now >= StartsAt`. When both dates exist, require `StartsAt < ExpiresAt`. Exact start is valid; exact expiry is invalid. Omitted StartsAt at creation means no scheduled start. For the existing update DTO's replacement semantics, explicitly define omitted/null StartsAt as clearing the start; clients that want to retain it must send it. Accept timezone-qualified ISO timestamps and compare instants in UTC. No scheduler or background job is needed because eligibility is evaluated when the discount is used.

**Files and data:** Open [DiscountsController](../src/Agora.Api/Controllers/DiscountsController.cs), [DiscountContracts](../src/Agora.Api/Contracts/DiscountContracts.cs), [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs), and [RedeemabilityBoundaryTests](../tests/Agora.Tests/Unit/RedeemabilityBoundaryTests.cs). Locate DiscountCode.IsRedeemable. Add the nullable column using the existing timestamp conversion; old rows get null.

**Implementation plan:**

1. Write the existing redeemability expression in words, then add the start condition with parentheses. Keep active/usage/expiry conditions intact.
2. Add StartsAt to the entity and migration. Verify upgrading an already active, unexpired code does not disable it.
3. Add create/update DTO fields, read mapping, and cross-field start/expiry validation. Validate the final pair on updates, not only the incoming non-null value.
4. Update IsRedeemable to use its supplied `now` argument for both start and expiry. Avoid calling the real clock inside the domain method.
5. Capture checkout's eligibility time from an injected TimeProvider once and pass it into the existing rule. If the quote story exists, reuse the same clock convention there.
6. Ensure early use fails through the current invalid-discount path before charging, incrementing usage, or committing stock. Do not introduce a timer that flips IsActive.
7. Add pure start/expiry/usage-boundary tests and integration tests with a controlled clock.
8. Update API examples, including equivalent timestamps with different offsets, and run `DiscountsApiTests`, `CheckoutApiTests`, and totals tests.

**Verification:** A code starting at 12:00 UTC fails immediately before and succeeds at 12:00, unless disabled/used up/expired. Test null start, start equal to expiry, start after expiry, clearing a start, equal instants with different offsets, and a failed early checkout leaving usage unchanged. Existing expiry boundary must remain exclusive.

**Explain it back / completion:** Explain why no worker is necessary to make a time predicate become true. Finish when old codes behave as before and configured dates are evaluated from one captured instant per operation.

## MS-29: Gift-card transaction history

**Status:** Planned; not implemented.

**User story:** As an administrator, I want a history of locally recorded gift-card balance changes, so I can explain a card's current balance from issuance, redemption, and refund credits.

**Current behavior and learning:** GiftCard stores current/initial balance and advances Version on Redeem/Credit, but there are no immutable transaction rows. Learn atomic balance-plus-ledger persistence and an honest opening balance for data that predates the ledger. This is a later story: follow every mutation path before editing any of them.

**Feature contract:** Admin GET `/api/admin/gift-cards/{id}/transactions` returns paged immutable entries ordered card version ascending. An entry includes ID, card ID, recorded version, kind (`OpeningBalance`, `Issued`, `Redeemed`, `RefundCredit`), signed amount, currency, balance after, server timestamp, and a safe source reference to an order/return where applicable. Never expose the bearer gift-card code in this report. Existing cards receive one OpeningBalance equal to their current balance and current version at migration time, not fabricated past events. New issue/redemption/credit must save the ledger row and balance atomically. This records local card accounting; it is not proof an external gateway refund succeeded and does not implement payment recovery.

**Files and data:** Read [GiftCardsController](../src/Agora.Api/Controllers/GiftCardsController.cs), [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs), [OrderService](../src/Agora.Infrastructure/Services/OrderService.cs), [ReturnService](../src/Agora.Infrastructure/Services/ReturnService.cs), and [AgoraDbContext](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Add `GiftCardEntry`, a unique card/version pair, and history-preserving card relationship. Use existing cent/timestamp storage conversions.

**Implementation plan:**

1. Search all `new GiftCard`, `.Redeem`, and `.Credit` call sites. Identify issuance, checkout redemption, order cancellation/full refund credit, and return approval credit. Write a before/after balance example for each.
2. Add the entry entity, kind/sign rules, source fields, relationship, and unique card/version mapping. Reuse GiftCard's existing concurrency token; do not invent a second balance version.
3. Create a migration that inserts one opening row per existing card using stored current balance/version. Copy stored cents without converting them a second time. Use migration time as recording time and label earlier history unavailable.
4. Introduce a small helper that performs a card mutation and appends its matching tracked entry without independently saving. Keep the caller's existing local unit of work responsible for both.
5. Add Issued at version zero on issuance. After each positive redemption, append a negative Redeemed entry at the advanced version; after each credit, append positive RefundCredit with the appropriate source identity.
6. Update all identified workflows. If current code does not actually find/credit a card, do not create a fictional credit entry. Zero gift tender creates no redemption row. Deactivation changes no balance and creates no monetary entry.
7. Add the admin-only paged read with safe DTOs. Clearly show whether history starts with issuance or a migration opening record.
8. Add `GiftCardLedgerApiTests`, upgrade/backfill checks, and forced local-save failure tests. Run `GiftCardTests`, `TaxGiftCardApiTests`, `RefundTenderTests`, and return integration tests.

**Verification:** Issue 50, redeem 20, credit 5: entries +50/-20/+5 and balance-after 50/30/35. An old card with initial 100/current 35 starts with OpeningBalance +35, not +100. Test full gift coverage, inactive-card refund credit, competing redemptions, no entry on rejected redemption, and local save rollback leaving neither balance change nor ledger row. A failed save after an external action remains an existing recovery concern; never automatically retry the gateway to make this test pass.

**Explain it back / completion:** Explain why a locally saved refund credit and a successful gateway refund are separate facts. Finish when every actual local balance mutation has one atomic ledger entry and migration history is accurately labeled.

## MS-30: Webhook delivery health report

**Status:** Planned; not implemented.

**User story:** As an administrator, I want a summary of webhook delivery outcomes and exhausted retries, so I can identify subscriptions that need investigation.

**Current behavior and learning:** WebhookDelivery keeps current status, attempt count, last-attempt information, and creation time, but no row for every historical attempt. Learn to define metrics that the available data really supports.

**Feature contract:** Admin GET `/api/admin/reports/webhook-health` accepts paired `from`/`to` timestamps, optional subscription ID, and page/pageSize (default 20, maximum 100). Default window is the seven days ending at captured `asOf`; explicit intervals must increase and span at most 30 days. Select the delivery cohort by `CreatedAt` in `[from,to)`. Per subscription, return total, current Pending/Succeeded/Failed counts, exhausted failed count where AttemptCount >= MaxAttempts, total recorded attempt count for those cohort deliveries, and success ratio Succeeded/Total. Only subscriptions with cohort deliveries appear; an existing subscription with no cohort returns an empty result, while an unknown explicit ID is 404. Page by subscription ID. Include overall totals for the entire filtered cohort, not just the page. Empty overall success ratio is null. Do not return secrets, signatures, payloads, or invoke retries.

**Files and data:** Read [WebhooksController](../src/Agora.Api/Controllers/WebhooksController.cs), [WebhookService](../src/Agora.Infrastructure/Services/WebhookService.cs), [AdminReportsController](../src/Agora.Api/Controllers/AdminReportsController.cs), and [WebhookTests](../tests/Agora.Tests/Unit/WebhookTests.cs). Locate WebhookDelivery.RecordAttempt, CanRetry, and MaxAttempts. No migration. Suggested query/test: `WebhookHealthQuery` / `WebhookHealthReportApiTests`.

**Implementation plan:**

1. List fields actually retained after retries. Write down why AttemptCount cannot tell you how many individual attempts happened during a date interval.
2. Define DTO names precisely, using a name such as `cohortLifetimeAttemptCount` instead of `attemptsInWindow`. State that statuses are current at report read time.
3. Validate dates, optional subscription existence, and safe page bounds. Capture one asOf for defaults and response metadata.
4. Build the created-date cohort query, then group current counts by subscription. Exhausted is a subset of Failed, not a fourth mutually exclusive status.
5. Compute full-cohort totals and the ordered subscription page within one short read transaction if separate queries are needed, so a retry cannot make page and totals disagree mid-read.
6. Calculate ratios from integer aggregates after materialization; avoid unsupported SQLite decimal aggregate behavior. Do not average per-subscription success ratios to obtain the overall ratio.
7. Use explicit DTO projection with subscription ID and safe identifying name if the existing model has one; do not invent a name column or serialize the subscription entity.
8. Add `WebhookHealthReportApiTests`, inspect query count, and run `WebhooksApiTests` plus webhook unit tests. Document the report as a current-outcome view of a creation cohort.

**Verification:** Create one Pending delivery with 0 attempts, one Failed with 2, one Succeeded with 3, and one Failed with 5. Expect total 4, pending 1, succeeded 1, failed 2, exhausted 1, lifetime attempts 10, ratio 0.25. Test exact date boundaries, success after retry, empty cohort, multiple subscriptions/paging, invalid dates, non-admin access, and absence of payload/secret/signature text. The fake sender call count must not change.

**Explain it back / completion:** Explain why a delivery created yesterday and retried today still belongs to yesterday's creation cohort. Finish when metric names match their real meaning and reading health cannot trigger delivery work.

## A repeatable review worksheet

Copy this worksheet into your own notes or PR body for the story you implement. Revisit the same feature in three passes: explain it as a user, trace it as a request, and prove it as a set of assertions.

| Question | Your answer |
| --- | --- |
| Which story and user problem am I solving? | |
| What exact request succeeds, and what response should it return? | |
| Which current file is the entry point? | |
| Which layer owns the central rule, and why? | |
| Which values are stored, calculated, or historical snapshots? | |
| What prevents another customer from reading or changing this resource? | |
| What happens if the request arrives twice or another writer saves first? | |
| Which changes must succeed or fail together? | |
| What old rows need a migration/backfill, if any? | |
| Which assertion proves rejection left no partial changes? | |
| Which existing behavior could this accidentally change? | |
| What did my focused tests, migration checks, and final checks actually report? | |
| What remains outside this story, and why is that boundary useful? | |

Before calling a story complete, show one successful example, one ownership/validation failure, and one edge case. For writes, inspect persisted state after failure. For reads, inspect counts, ordering, query bounds, and absence of side effects. For schema changes, show an upgrade from old data as well as a fresh database. Then explain the feature without looking at the implementation, reopen the code, and correct your explanation where necessary.

It is normal to need several passes. The useful progression is: “I recognize this code,” then “I can trace this request,” then “I can explain the rule,” then “I can change the behavior and prove what stayed correct.”
