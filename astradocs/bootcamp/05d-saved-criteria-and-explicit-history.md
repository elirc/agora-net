# Workshop 5d: saving a question versus recording a view

Stories: MS-13 and MS-16. These account features store different things: a saved search stores a reusable question; recent-product history records an explicit viewing event's latest time. [The tracker](story-tracker.md) shows current verification status.

## Start with a simple contrast

Save the search “active products containing the literal text `50%_special`, priced from 9 to 15 USD, currently in stock.” Tomorrow a new matching product appears. Running the saved search should include it, because the search stores criteria, not yesterday's results.

Now explicitly record views of A, B, then A again. The recent list should contain A then B, with two unique history rows. The second A updates its last-viewed time; it does not create another copy of A.

| Stored value | Saved search | Recent product |
| --- | --- | --- |
| Owner ID | Yes | Yes |
| Typed filter definition | Yes | No |
| Catalog result snapshot | No | No |
| Catalog reference | Optional category ID or slug in criteria | Viewed product ID |
| Latest explicit view time | No | Yes |
| Current product response | Calculated when run | Loaded when listed |

The category reference in a search is a category criterion, not a viewed-product identity. A saved search does not keep a list of product IDs.

## Whitelist the stored language

Read [CustomerCatalogContracts](../../src/Agora.Api/Contracts/CustomerCatalogContracts.cs). Version 1 accepts only search text, category ID/slug, min/max price, currency, in-stock, active flag, and sort. It deliberately does not persist paging, raw SQL, arbitrary property names, SKU, image, or tag filters.

`SavedSearchDefinition.ToRequest` constructs the existing ProductSearchRequest. Its validation delegates to that request's property and cross-field validation, including price precision and min/max ordering. The stored format does not acquire a second, looser search validator.

Unknown definition properties are rejected by the JSON contract. The application serializes the typed definition itself and bounds the resulting JSON to 8,192 characters. The database never receives an unchecked query language supplied by the customer.

Say the same idea without framework names: allow a small set of knobs, save their values, and later turn those same knobs on the ordinary search engine. Do not save arbitrary instructions for the database to execute.

## One query path for both entry points

Follow [SavedSearchesController](../../src/Agora.Api/Controllers/SavedSearchesController.cs) into [ProductReadQueries.Page](../../src/Agora.Api/Queries/ProductReadQueries.cs), then [ProductCatalogQuery](../../src/Agora.Api/Queries/ProductCatalogQuery.cs). Ordinary ProductsController.List now uses that same page method.

The shared path filters and counts, takes a stable page, loads the response relationships, and computes approved ratings. Literal `%` and `_` keep their literal meaning. Current public visibility semantics are preserved: saving criteria does not secretly add an active-only rule that ordinary search does not have.

Paging belongs to the results request, not the stored definition. You can request page 2 with size 1 today without rewriting the saved search. Removing a referenced category does not delete the stored definition; the current query simply finds no matching category data.

## Schema version is about interpreting stored input

The version is not a catalog revision or a cache timestamp. It tells the reader which stored definition format it understands.

For version 1, the application deserializes and revalidates the definition before execution. An unknown future version returns metadata with `canRun=false` and no interpreted definition; attempting to run it returns a clear 409. A malformed or no-longer-valid version-1 payload is also refused. It is safer to show that the definition cannot be interpreted than to silently run different criteria.

## Count limits need a transaction before the count

Each customer can store at most 50 searches. Consider two requests when the current count is 49. If both count outside a write transaction, both can observe 49 and insert, leaving 51.

Creation acquires SQLite's local write transaction before counting. One request obtains the final slot; the next sees 50 and conflicts. The test coordinates two independent connections before their transactions begin, making this an actual competing-writer experiment rather than a sleep-based guess.

There is no unique-name rule: two searches may share a display name. Capacity is per customer, and deleting one owned search frees one slot.

## Viewing is an explicit write

Read [RecentProductsController](../../src/Agora.Api/Controllers/RecentProductsController.cs). Only authenticated POST `/api/me/recent-products/{productId}` records a view. Product GET, catalog GET, and recent-history GET do not create tracking rows.

The POST checks that the product currently exists and is active, uses the authenticated owner, and captures server time after obtaining the write transaction. It then inserts or updates the unique customer/product pair, keeps the first 50 rows under the defined ordering, deletes the rest, and commits together.

Capturing time before waiting for the transaction could let a delayed writer save an older timestamp after a newer writer. Placing the clock read inside the serialized section avoids that request-order mistake. Tests use a controllable clock rather than sleeping.

## Filter before the visible limit

Storage retains at most 50 unique product rows per customer. The visible response returns the latest 20 **currently active** products. These are different limits.

Suppose the newest five history rows now refer to inactive products. Taking 20 rows and then removing those five would show only 15, even when older active products exist. The query applies activity before Take(20), then batch-loads the current product responses and restores the history ordering.

Tied timestamps use product ID as the stable secondary order. Repeated views at a genuinely later time move the product forward; a tied timestamp follows the documented tie-breaker. Customer/product deletion cascades history, and scoped clear affects only the caller, returning 204 even when already empty.

## Read the tests as counterexamples

- [SavedSearchesApiTests](../../tests/Agora.Tests/Integration/SavedSearchesApiTests.cs): ordinary-versus-saved result equivalence, literal wildcard characters, later catalog matches, changed paging, unknown payload fields/versions, removed categories, owner isolation, and capacity.
- [RecentlyViewedApiTests](../../tests/Agora.Tests/Integration/RecentlyViewedApiTests.cs): A/B/A, no tracking from reads, per-owner clear, 51 distinct identities with retention, activity before the visible limit, ties, and deletion cleanup.
- [CustomerCatalogPersistenceTests](../../tests/Agora.Tests/Integration/CustomerCatalogPersistenceTests.cs): independent-connection barriers for the search cap and repeated views, plus actual schema upgrades.

## Exercises and answers

1. A new product matches yesterday's saved criteria. Should it appear? **Yes; results are current.**
2. Does a saved definition's version increase when stock changes? **No; it versions the input format.**
3. Can a GET of a product record recent history? **No; recording requires explicit POST.**
4. Two creates at count 49: why is a normal count check insufficient? **Both can observe the same old count unless the check and insertion are serialized.**
5. Newest five history entries are inactive; thirty older entries are active. Visible result count? **20, because activity is filtered before the limit.**
6. Why keep a search after its category is removed? **The search is owned intent, not a dependent catalog child; it can remain understandable and return no matches.**

In your learning log, write one sentence beginning “A saved search remembers…” and another beginning “Recent history remembers…”. Then trace one request from authentication to the exact database rows it may change.
