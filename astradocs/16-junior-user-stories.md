# 25 junior user stories with step-by-step implementation plans

[AstraDocs home](README.md) · Preparation: [First change](12-first-change.md) · Reference: [API contracts](../docs/api-reference.md)

**Planning document only. All 25 stories below are unimplemented proposals.** The existing repository was inspected to choose small additions and fixes that fit its current structure. This document does not add their endpoints, fields, tests, or behavior. Expected results describe what you will build, not what the API already returns.

You can work on one story at a time. Each has a concrete user benefit, a fixed contract, named files, small steps, examples, and a test plan. No story requires a database migration, real payments, a background worker, a new frontend, or a new software package. Most use an existing DTO, controller, and test fixture. A couple of stories involve a new read or delete endpoint; their ownership checks are spelled out.

## Before your first story

1. Read [how tests work](11-tests-as-examples.md) and [the code map](03-find-your-way.md). Keep them open as references.
2. Run `git status --short` and inspect existing changes. Keep earlier work intact; do not reset the repository to obtain an empty diff.
3. Choose exactly one story. Write its expected behavior in your own words before editing.
4. Open only its listed files first. Find the named method or type using editor search.
5. Run that story's existing test class to establish your starting point. A restore failure is not a failed business assertion. Setup help is in [the first-hour guide](../docs/learning/01-first-hour.md).
6. Write the smallest new test for the requested behavior. Confirm it fails for the missing behavior. If adding a new property causes a compile error, add the minimum declaration first; then obtain an assertion failure before implementing its behavior.
7. Implement in the order given. Re-run the focused tests after each useful increment.
8. Before submitting, run `dotnet test Agora.slnx`, inspect your diff, and update the API reference. Record the actual result; do not copy an old test count.

The commands below run from the directory containing `Agora.slnx`. A proposed new test filename is shown as code rather than a link, because it does not exist yet. Links point to files that already exist.

## How to arrange reliable examples

Use [AgoraApiFactory.WithDbAsync](../tests/Agora.Tests/Integration/AgoraApiFactory.cs) for carefully chosen test data. Give each scenario a unique category, customer, or product so another test's data does not change your results. Use distinct GUID-based slugs/SKUs/emails when uniqueness matters. A class fixture's database is shared across that class's methods.

Use the authentication helpers in [TestAuth.cs](../tests/Agora.Tests/Integration/TestAuth.cs). Admin-only requests need an admin client. Ownership tests need customer A, customer B, and A's real resource ID; a random nonexistent ID does not test ownership. For a failed write, read the record again in a fresh request or database scope to prove it was not saved.

For a query filter, add the predicate **before both counting and paging**. For response-only fields, prefer a computed getter over a new stored column. When two stories touch the same method, keep the earlier story's behavior and tests. There are no hard story-to-story dependencies; the suggested order gradually introduces more moving parts.

## Pick a story

| ID | User benefit | Main practice |
| --- | --- | --- |
| [JS-01](#js-01-page-navigation-flags) | Know whether another page exists | Computed response properties |
| [JS-02](#js-02-active-and-saved-cart-line-counts) | Show separate cart badges | Counts versus quantities |
| [JS-03](#js-03-set-and-read-variant-weight) | Enter shipping weight when creating a product | Optional input and mapping |
| [JS-04](#js-04-a-predictable-primary-product-image) | Display a consistent product thumbnail | Deterministic selection |
| [JS-05](#js-05-find-a-category-by-slug) | Look up a category from its URL name | A small public read endpoint |
| [JS-06](#js-06-search-category-names) | Find a category in a long list | Literal text filtering |
| [JS-07](#js-07-browse-root-or-child-categories) | Build a category navigation menu | Optional filters and combinations |
| [JS-08](#js-08-reject-an-unknown-category-parent) | Get a clear error for an invalid parent | A validation bug fix |
| [JS-09](#js-09-safe-and-stable-category-pages) | Browse category pages reliably | Overflow and ordering |
| [JS-10](#js-10-find-a-product-by-exact-sku) | Find a specific purchasable choice | Same-variant predicates |
| [JS-11](#js-11-find-products-with-or-without-images) | Find incomplete catalog entries | Boolean query filters |
| [JS-12](#js-12-stable-variant-order-in-product-responses) | Display choices in a consistent order | Response mapping |
| [JS-13](#js-13-product-variant-count) | Know how many choices a product has | A derived field |
| [JS-14](#js-14-filter-product-reviews-by-minimum-rating) | Read higher-rated reviews | Filtered paging |
| [JS-15](#js-15-read-product-reviews-oldest-first) | Follow feedback over time | Validated sorting |
| [JS-16](#js-16-an-in-stock-flag-on-inventory-responses) | Display an availability badge | Reusing an existing invariant |
| [JS-17](#js-17-filter-shipping-methods-by-delivery-time) | Find sufficiently fast shipping | An optional numeric filter |
| [JS-18](#js-18-reject-undefined-shipping-rate-types) | Avoid accidentally misconfigured rates | Enum input validation |
| [JS-19](#js-19-search-my-wishlist-names) | Find a named wishlist | Ownership-preserving filtering |
| [JS-20](#js-20-wishlist-stock-summary-counts) | See which saved choices are available | Counting mapped data |
| [JS-21](#js-21-clear-one-wishlist-without-deleting-it) | Reuse an empty wishlist | A scoped delete action |
| [JS-22](#js-22-read-one-of-my-saved-addresses) | Open one saved address | Resource ownership |
| [JS-23](#js-23-filter-my-address-book-by-country) | Find addresses for a destination | Normalization and filtering |
| [JS-24](#js-24-filter-my-order-history-by-status) | Find orders at a particular stage | Validated enum filtering |
| [JS-25](#js-25-reject-reversed-top-product-report-dates) | Avoid misleading empty reports | A small report validation fix |

## JS-01: Page navigation flags

**Status:** Planned; not implemented. **Starting knowledge:** properties, booleans, one unit test.

**User story:** As a client application developer, I want the paged response to tell me whether previous and next pages exist, so I can enable navigation buttons without duplicating the calculation.

**Current behavior:** `PagedResult<T>` exposes items, page, page size, total count, and total pages. It has no navigation flags.

**Acceptance criteria**

- Every paged response includes `hasPreviousPage` and `hasNextPage` as JSON booleans.
- Previous means the requested page is greater than 1. Next means the requested page is less than `TotalPages`.
- Five matches with size two: page 1 is false/true; page 2 is true/true; page 3 is true/false.
- An empty first page is false/false. A requested page beyond the final page has next=false and previous=true. These are navigation hints, not a guarantee that the previous page has items.
- Existing fields, constructor arguments, and endpoint validation stay intact.

**Files to open:** [PagedResult.cs](../src/Agora.Api/Contracts/PagedResult.cs), [ProductsApiTests.cs](../tests/Agora.Tests/Integration/ProductsApiTests.cs). Proposed new file: `tests/Agora.Tests/Unit/PagedResultTests.cs`.

**Implementation plan**

1. Read the existing `TotalPages` getter. Calculate the five-match examples on paper.
2. Create `PagedResultTests` in the existing test project. Construct `PagedResult<int>` objects with explicit metadata; no database is needed.
3. Add getter-only boolean properties in the record body. Use the existing `Page` and `TotalPages`; do not add constructor parameters or stored fields.
4. Write a small theory covering the three normal pages, empty page 1, and an out-of-range page.
5. Add one HTTP assertion to the product-list tests that reads the JSON and checks the two camel-case keys. This checks serialization as well as the C# values.
6. Run the focused checks below. Update the shared pagination description in the API reference because the change affects every `PagedResult` endpoint.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~PagedResultTests|FullyQualifiedName~ProductsApiTests"`. For a manual demo, request `/api/products?page=1&pageSize=2` and compare the flags to the returned count.

**Common trap:** using `Items.Count` to decide whether another page exists. A full last page can still have no next page.

**Done when:** the table cases and JSON assertion pass, the pagination reference includes both fields, and you can explain why these values do not need database columns.

## JS-02: Active and saved cart line counts

**Status:** Planned; not implemented. **Starting knowledge:** response DTOs and collection counts.

**User story:** As a shopper, I want separate counts of active cart lines and saved-for-later lines, so a client can show clear badges for both lists.

**Current behavior:** `CartResponse` contains `Items`, `SavedItems`, and `TotalQuantity`. Total quantity counts active units, which is different from counting lines.

**Acceptance criteria**

- Responses include `activeLineCount` and `savedLineCount`, derived from the corresponding response collections.
- Two active lines with quantities two and three mean activeLineCount=2 and existing totalQuantity=5.
- One saved line with quantity four means savedLineCount=1; it adds nothing to active quantity or subtotal.
- Empty collections produce zero. Saving or activating a line changes the counts in the next response.
- This adds no reservations, pricing rules, or database fields.

**Files to open:** [CartContracts.cs](../src/Agora.Api/Contracts/CartContracts.cs), [CartsApiTests.cs](../tests/Agora.Tests/Integration/CartsApiTests.cs), [SavedForLaterApiTests.cs](../tests/Agora.Tests/Integration/SavedForLaterApiTests.cs).

**Implementation plan**

1. Locate `CartResponse` and its `Items`, `SavedItems`, and `TotalQuantity` members. Write down which measures lines and which measures units.
2. Add two public computed integer getters in the response record body. Count the mapped collections; leave `Cart` and the response constructor unchanged.
3. Extend the empty-cart API test to require both values to be zero.
4. Arrange a fresh cart with two different variants, quantities two and three. Check two active lines and five total units.
5. Save one line through the existing save-for-later endpoint. Check one active and one saved line, then activate it and check the counts again.
6. Preserve existing subtotal assertions so the new work does not accidentally change pricing. Inspect JSON at least once to confirm the field names.
7. Document that these are line counts, not sums of quantities.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~CartsApiTests|FullyQualifiedName~SavedForLaterApiTests"`. Use your own fresh cart; do not clear a seeded or unrelated cart to simplify a test.

**Common trap:** calculating savedLineCount by summing saved quantities. One line containing four units still counts as one line.

**Done when:** empty, mixed, saved, and reactivated cases pass, old totals stay correct, and the API reference distinguishes line counts from quantity.

## JS-03: Set and read variant weight

**Status:** Planned; not implemented. **Starting knowledge:** request validation and mapping an input into an entity.

**User story:** As a catalog administrator, I want to enter a variant's weight when creating a product and see that weight in the response, so the existing weighted-shipping calculation can use accurate data.

**Current behavior:** `ProductVariant` already has persisted `WeightGrams`, and checkout uses it. Product-creation requests and `VariantResponse` do not expose it.

**Acceptance criteria**

- Each create-variant input accepts optional `weightGrams`, defaulting to 0 for old callers.
- Accepted integers are 0 through 1,000,000 inclusive. Negative, above-limit, or non-integer values return 400.
- Variant responses include the saved `weightGrams` on create, list, ID lookup, and slug lookup.
- Creating a 250-gram variant and reading it again returns 250. Omitting the field preserves the existing zero default.
- Product creation stays admin-only; checkout's formula and existing rows are unchanged.

**Files to open:** [ProductContracts.cs](../src/Agora.Api/Contracts/ProductContracts.cs), [ProductsController.cs](../src/Agora.Api/Controllers/ProductsController.cs) (`Create`), [ProductVariant.cs](../src/Agora.Domain/Entities/ProductVariant.cs), [ProductsApiTests.cs](../tests/Agora.Tests/Integration/ProductsApiTests.cs).

**Implementation plan**

1. Confirm the existing entity property and find the `new ProductVariant` block in `Create`. Notice it currently does not assign weight from input.
2. Add the optional integer parameter at the end of `CreateVariantRequest`, with default zero and the agreed range annotation. Keeping it at the end preserves existing positional call sites.
3. Assign that input to the entity's existing property during creation. Do not create a migration: this is already stored data.
4. Add weight to `VariantResponse` and its `From` mapper. Search for direct `new VariantResponse` calls and update any constructor users identified by the compiler.
5. Add an admin integration test posting a new product with a 250-gram variant. Assert the create response and a subsequent GET, so an omitted save assignment cannot pass unnoticed.
6. Add omitted, zero, maximum, negative, above-maximum, and malformed-value cases. Invalid cases must not create a product.
7. Update the create and response examples in the API reference. State explicitly that grams are integer units.

**Test and demo:** `dotnet test --filter FullyQualifiedName~ProductsApiTests`. Also run `ShippingMethodTests` after the focused product checks to confirm the unchanged shipping domain behavior remains intact.

**Common trap:** adding a response field but forgetting the input-to-entity assignment. The follow-up GET distinguishes a saved value from a response-only illusion.

**Done when:** weight round-trips, old requests still work, invalid inputs are rejected, and no new database schema was introduced.

## JS-04: A predictable primary product image

**Status:** Planned; not implemented. **Starting knowledge:** optional values and ordering a small collection.

**User story:** As a client application developer, I want a product's primary image identified consistently, so product cards can display one thumbnail without inventing their own selection rule.

**Current behavior:** products return an image array ordered by `SortOrder`. There is no primary-image field, and equal sort orders have no explicit tie-breaker in the mapper.

**Acceptance criteria**

- Add nullable `primaryImage` containing an existing `ImageResponse` shape.
- Choose the image with the smallest `SortOrder`; break ties by ascending image ID using the same in-memory ordering for the full image array.
- A product without images returns `primaryImage: null` and an empty image array.
- Primary image is the first image under that defined ordering. Its selection is independent of insertion order.
- Image creation and product permissions stay the same; this does not download or transform images.

**Files to open:** [ProductContracts.cs](../src/Agora.Api/Contracts/ProductContracts.cs) (`ProductResponse.From`), [ProductImage.cs](../src/Agora.Domain/Entities/ProductImage.cs), [ProductsApiTests.cs](../tests/Agora.Tests/Integration/ProductsApiTests.cs).

**Implementation plan**

1. Find the existing image ordering in `ProductResponse.From`. Add an ID tie-breaker to that in-memory sort.
2. Add a getter-only `PrimaryImage` to `ProductResponse`. Select by the same `SortOrder`/ID rule so manually constructed DTOs also behave consistently; do not store another image in the database.
3. Arrange three images with deliberately shuffled insertion order and sort orders 2, 0, and 1. Assert the sort-order-zero image is primary.
4. Arrange two images with the same sort order and known IDs; insert them in reverse ID order. Check the primary choice and the image-array order.
5. Include a no-image case. Read the actual HTTP JSON and check that the property is present with null, matching the contract.
6. Exercise list and detail responses because both use the mapper. Keep existing image metadata unchanged.
7. Document the selection rule, including the tie-breaker, so clients know which image to expect.

**Test and demo:** `dotnet test --filter FullyQualifiedName~ProductsApiTests`. Request a product with multiple images and compare `primaryImage.id` to `images[0].id`.

**Common trap:** testing only images with different sort orders. That cannot detect an unstable tie.

**Done when:** normal, tied, and empty examples pass and the API reference identifies the field as derived response data.

## JS-05: Find a category by slug

**Status:** Planned; not implemented. **Starting knowledge:** an existing GET action and 200/404 responses.

**User story:** As a client application developer, I want to retrieve a category by its slug, so a category URL can load its data without first fetching the complete category list.

**Current behavior:** categories have a slug and ID lookup, but no dedicated slug lookup. Products already demonstrate the route pattern.

**Acceptance criteria**

- Add public `GET /api/categories/by-slug/{slug}` returning the existing `CategoryResponse`.
- Trim surrounding whitespace from the route value; match the stored slug exactly and case-sensitively, consistent with the current stored-slug comparison approach.
- Existing slug returns 200; unknown or case-different slug returns 404. A missing path segment is an unmatched route, also 404.
- No rows are written, and ID lookup continues to work.

**Files to open:** [CategoriesController.cs](../src/Agora.Api/Controllers/CategoriesController.cs) (`GetById`), [ProductsController.cs](../src/Agora.Api/Controllers/ProductsController.cs) (`GetBySlug`), [CategoriesApiTests.cs](../tests/Agora.Tests/Integration/CategoriesApiTests.cs).

**Implementation plan**

1. Read the two existing lookup actions. Notice the product route has a literal `by-slug` segment and a constrained GUID route for IDs.
2. Add a category action named `GetBySlug` with the literal route above, a string argument, and cancellation token. Keep it public like category ID lookup.
3. Normalize only the surrounding whitespace, then query `db.Categories.AsNoTracking()` for one matching slug. Do not use `Contains` or generate a new slug from the input.
4. Return `NotFound` for no match; otherwise return `CategoryResponse.From(category)` through `Ok`.
5. In an integration test, create one uniquely named category as admin, then use an anonymous client for slug lookup. Compare its ID with the created category.
6. Test unknown slug, upper/lowercase mismatch against a deliberately lowercase fixture, and the original ID route. For whitespace, URL-encode the spaces in the test request.
7. Add the route and exact-match policy to the API reference, plus one `.http` example if helpful.

**Test and demo:** `dotnet test --filter FullyQualifiedName~CategoriesApiTests`. On ordinary seed data, `/api/categories/by-slug/electronics` should return the electronics category after this story is implemented.

**Common trap:** copying the admin authorization attribute from a neighboring mutation action. This lookup is public like the existing category reads.

**Done when:** anonymous lookup works, not-found cases are clear, and existing ID lookup still passes.

## JS-06: Search category names

**Status:** Planned; not implemented. **Starting knowledge:** a query parameter and a `Where` filter.

**User story:** As a shopper, I want to filter category names by a short search phrase, so I can find a relevant category without paging through every category.

**Current behavior:** the category list supports paging but does not filter names.

**Acceptance criteria**

- `GET /api/categories?search=kitchen` matches a literal substring of `Name` only.
- Missing, empty, or whitespace-only search leaves the existing list behavior unchanged. Nonblank input is trimmed.
- Raw input longer than 200 characters returns 400. `%`, `_`, and backslash are literal characters, not wildcard instructions.
- Use SQLite LIKE's existing ASCII case-insensitive behavior; do not promise full Unicode linguistic matching.
- `TotalCount` reflects the filtered set. Existing paging and name ordering remain in force.

**Files to open:** [CategoriesController.cs](../src/Agora.Api/Controllers/CategoriesController.cs) (`List`), [ProductCatalogQuery.cs](../src/Agora.Api/Queries/ProductCatalogQuery.cs) for literal LIKE escaping, [CategoriesApiTests.cs](../tests/Agora.Tests/Integration/CategoriesApiTests.cs).

**Implementation plan**

1. Add an optional `search` query argument to `List`. Add length validation using the endpoint's existing validation style or a `MaxLength` annotation with the needed import.
2. Start with an unordered `IQueryable<Category>` from `AsNoTracking`; keep ordering separate until all filters are applied. This avoids assigning a filtered query back into an inferred `IOrderedQueryable` variable.
3. For nonblank input, trim it. Escape backslash first, then `%` and `_`, following the product helper's convention.
4. Add a name-only `EF.Functions.Like` predicate with the explicit escape character. Put it before `CountAsync` and page loading.
5. Reapply the existing name ordering, including an ID tie-breaker if JS-09 has already been completed. Preserve any JS-07 parent filters.
6. Arrange uniquely identifiable categories where two names match and one does not. Use page size one and require totalCount=2, not one.
7. Add literal-percent and literal-underscore cases, blank input, no match, case variation, and length 201. Include a category whose description matches but name does not; it must be excluded.
8. Document the name-only and literal-substring behavior.

**Test and demo:** `dotnet test --filter FullyQualifiedName~CategoriesApiTests`. After implementation, `/api/categories?search=kitchen&pageSize=1` demonstrates search plus paging.

**Common trap:** filtering the already-loaded page. That can create an empty page even though matching categories exist later in the database.

**Done when:** results and counts agree, wildcard characters stay literal, and requests without search preserve prior behavior.

## JS-07: Browse root or child categories

**Status:** Planned; not implemented. **Starting knowledge:** nullable values and combining query predicates.

**User story:** As a client application developer, I want to request root categories or the immediate children of one category, so I can build a simple category navigation menu.

**Current behavior:** category responses have `ParentCategoryId`, but list requests cannot select a particular level.

**Acceptance criteria**

- `rootOnly=true` returns categories whose parent ID is null.
- `parentCategoryId=<guid>` returns immediate children of that parent, not all descendants.
- Omitted or false `rootOnly` adds no root filter. Supplying true together with a parent ID returns 400 for contradictory input.
- An unknown parent GUID produces a successful empty list. Malformed GUID or boolean input returns 400 through binding.
- Filtered counts and paging stay correct. If JS-06 exists, search combines with these filters using AND.

**Files to open:** [CategoriesController.cs](../src/Agora.Api/Controllers/CategoriesController.cs), [Category.cs](../src/Agora.Domain/Entities/Category.cs), [CategoriesApiTests.cs](../tests/Agora.Tests/Integration/CategoriesApiTests.cs).

**Implementation plan**

1. Add optional nullable GUID `parentCategoryId` and boolean `rootOnly` with default false to `List`.
2. At the beginning of the action, reject `rootOnly && parentCategoryId.HasValue` with a 400 problem response. Do this before querying data.
3. Build the category query as an `IQueryable`. If rootOnly is true, add `ParentCategoryId == null`. If a parent ID is present, add equality to that ID.
4. Leave both filters absent for the default request. Apply all filters before the shared count/order/skip/take sequence.
5. Arrange root A, root B, child A1, and grandchild A1a. Use unique slugs; save parent records before dependent children when arranging through HTTP.
6. Assert root mode includes the roots and excludes children. Assert parent A returns A1 but excludes A1a and anything under B.
7. Test the contradictory query, unknown parent, malformed inputs, and default request. Avoid exact global seed counts; assert inclusion/exclusion or use a unique search prefix when available.
8. Document that this is one-level navigation rather than recursive traversal.

**Test and demo:** `dotnet test --filter FullyQualifiedName~CategoriesApiTests`. Compare `?rootOnly=true` with `?parentCategoryId=<your-created-root-id>`.

**Common trap:** using an empty GUID to mean "roots." A missing parent value and a GUID containing zeros are different inputs.

**Done when:** roots, immediate children, contradictory input, and no-match behavior are covered, with all earlier category filters preserved.

## JS-08: Reject an unknown category parent

**Status:** Planned; not implemented. **Starting knowledge:** an existing update action and an early validation return.

**User story:** As a catalog administrator, I want a clear validation error when I move a category under a nonexistent parent, so I can correct the selection without encountering a database error.

**Current behavior:** category creation checks whether a supplied parent exists. Update checks self-parenting but does not perform the equivalent existence check before saving.

**Acceptance criteria**

- Updating an existing category with an unknown non-null parent returns 422 with a problem response.
- Rejection leaves its saved name, slug, description, and original parent unchanged.
- A known parent still works; null still moves it to the root level. Existing self-parent rejection stays 422.
- Updating an unknown category stays 404. Admin authorization stays in place.
- This narrowly fixes missing-parent validation; ancestor-cycle detection is outside this story.

**Files to open:** [CategoriesController.cs](../src/Agora.Api/Controllers/CategoriesController.cs) (`Create` and `Update`), [CategoryContracts.cs](../src/Agora.Api/Contracts/CategoryContracts.cs), [CategoriesApiTests.cs](../tests/Agora.Tests/Integration/CategoriesApiTests.cs).

**Implementation plan**

1. Compare `Create` and `Update` side by side. Identify the parent-existence check already used during creation.
2. Write an integration test that creates a child with a known name, then tries to update both its name and parent using an unknown parent GUID.
3. In `Update`, after loading the category and the existing self-parent check, check whether a non-null parent ID exists. Place this before assigning any updated entity fields.
4. Return `UnprocessableEntity` with a clear parent-not-found problem when the check fails. Reuse the existing creation wording if appropriate.
5. Make a separate GET after the failed PUT and compare all original fields. A status assertion alone cannot show that the rejected write left data unchanged.
6. Add success tests for changing to a known parent and changing to null. Retain existing self-parent and unknown-category tests.
7. Add the missing-parent case to the update row in the API reference. Do not add recursive traversal or a new abstraction for this small fix.

**Test and demo:** `dotnet test --filter FullyQualifiedName~CategoriesApiTests`. Submit a normal update payload with a random nonexistent parent GUID using an admin client, then reload the category.

**Common trap:** validating after field assignment and later accidentally saving those assignments. Put rejection checks before mutation to keep the method easy to reason about.

**Done when:** the invalid-parent regression passes, a reload proves unchanged data, and null/known-parent behavior remains available.

## JS-09: Safe and stable category pages

**Status:** Planned; not implemented. **Starting knowledge:** integer arithmetic and primary/secondary ordering.

**User story:** As a client application developer, I want category paging to reject impossible offsets and order tied names consistently, so navigation is predictable on unchanged data.

**Current behavior:** category paging validates page and page size independently, multiplies them as integers, and orders only by name. Products already demonstrate safer arithmetic and a unique tie-breaker.

**Acceptance criteria**

- Keep default page 1, size 50, and size range 1–100.
- After validating positive inputs, calculate offset in a 64-bit integer. Offset above `int.MaxValue` returns 400 rather than overflowing.
- Accepted requests order by name, then ID ascending, before skipping and taking.
- A valid page beyond the results returns 200 with empty items and the correct total count.
- This does not introduce cursor paging or promise consistency across concurrent catalog writes.

**Files to open:** [CategoriesController.cs](../src/Agora.Api/Controllers/CategoriesController.cs), [ProductSearchRequest.cs](../src/Agora.Api/Contracts/ProductSearchRequest.cs), [CategoriesApiTests.cs](../tests/Agora.Tests/Integration/CategoriesApiTests.cs).

**Implementation plan**

1. Keep the current page/pageSize validation at the top of `List`. Write down why positive integers can still overflow when multiplied.
2. Compute the offset by widening before multiplication. Check it against `int.MaxValue`; only cast back after the guard succeeds.
3. Pass the validated offset to `Skip`. Do not recompute the old unchecked expression elsewhere in the method.
4. Add `ThenBy` on category ID after name ordering. Apply this to the final query after any search/parent filters from previous stories.
5. Test page zero, size zero, size 101, and page 2147483647 with size 100. The last case distinguishes offset overflow from ordinary size validation.
6. Use a fresh `AgoraApiFactory` for the tie scenario. Add three categories with the same name and distinct IDs/slugs, inserting them in a different order from their IDs. Through `WithDbAsync`, obtain all category IDs ordered by name then ID using SQLite. Fetch API pages of size one from page 1 through that expected list's length, concatenate the returned IDs, and compare the complete sequence. The small seeded category set makes this manageable without needing JS-06's search filter.
7. Test an ordinary empty page and default metadata. Document overflow rejection and deterministic ordering.

**Test and demo:** `dotnet test --filter FullyQualifiedName~CategoriesApiTests`. Request `?page=2147483647&pageSize=100`; after the fix it should return 400.

**Common trap:** casting the multiplication result after it has already overflowed. Widen an operand first.

**Done when:** boundary inputs, ties, and normal pages pass without changing the category response envelope.

## JS-10: Find a product by exact SKU

**Status:** Planned; not implemented. **Starting knowledge:** the single-variant filter in the catalog walkthrough.

**User story:** As a shopper or support agent, I want to find the product containing an exact SKU, so I can locate a known variant and combine that lookup with price or availability requirements.

**Current behavior:** catalog search matches names/descriptions and supports price, currency, stock, and category filters. It has no SKU query parameter.

**Acceptance criteria**

- Add optional `sku` to `GET /api/products`. Trim nonblank input; blank input means no SKU filter. Raw maximum length is 64.
- Match the entire stored SKU, case-sensitively. `TEE-A` must not match `TEE-AB`, and `tee-a` must not match stored `TEE-A`.
- SKU, price, currency, and availability must all match the same variant inside one `Any` expression.
- No match returns 200 with empty items. Response products still include all their variants.
- Existing filters and counts continue working; no new endpoint or SKU normalization policy is added to product creation.

**Files to open:** [ProductSearchRequest.cs](../src/Agora.Api/Contracts/ProductSearchRequest.cs), [ProductCatalogQuery.cs](../src/Agora.Api/Queries/ProductCatalogQuery.cs), [CatalogSearchApiTests.cs](../tests/Agora.Tests/Integration/CatalogSearchApiTests.cs).

**Implementation plan**

1. Add nullable `Sku` to the request contract with the 64-character length limit. Read how optional search/currency values are currently handled.
2. In `Apply`, prepare a trimmed SKU local that is null for blank input. Do not call `ToUpperInvariant`: this story intentionally preserves exact case.
3. Include a non-null SKU in the condition that decides whether any variant filters need applying. Otherwise SKU-only requests could accidentally skip filtering.
4. Add SKU equality inside the existing variant `Any`, beside price/currency/stock conditions. Do not add a separate product-level `Any` for SKU.
5. Arrange a product with target SKU unavailable and another SKU available. Query the target with `inStock=true`; that product must be excluded.
6. Test a matching available target, a prefix-only SKU, case mismatch, surrounding whitespace, blank input, no match, and length 65.
7. Check that a matched product still returns both variants, and its total count reflects filtering. Update the API reference with exact-match and same-variant semantics.

**Test and demo:** `dotnet test --filter FullyQualifiedName~CatalogSearchApiTests`. After implementation, try `?sku=TEE-BLK-M&inStock=true` against suitable local stock.

**Common trap:** using `search` as an implementation shortcut. Name/description substring search has a different contract.

**Done when:** the cross-variant counterexample fails to match, exact lookup works, and old catalog tests remain green.

## JS-11: Find products with or without images

**Status:** Planned; not implemented. **Starting knowledge:** nullable booleans and `Any`.

**User story:** As a catalog administrator, I want to list products lacking images, so I can find entries that need product photography. The filter remains on the existing public catalog endpoint.

**Current behavior:** product responses contain images, but list requests cannot filter by whether any images exist.

**Acceptance criteria**

- Optional `hasImages=true` selects products with at least one image; false selects products with zero images.
- Omitted `hasImages` preserves the unfiltered behavior. A malformed boolean returns 400.
- The image condition combines with existing product and variant filters using AND.
- Filtered `TotalCount` and paging remain correct. Returned images are not reduced or edited.
- This checks image records, not whether their remote URLs are reachable.

**Files to open:** [ProductSearchRequest.cs](../src/Agora.Api/Contracts/ProductSearchRequest.cs), [ProductCatalogQuery.cs](../src/Agora.Api/Queries/ProductCatalogQuery.cs), [CatalogSearchApiTests.cs](../tests/Agora.Tests/Integration/CatalogSearchApiTests.cs).

**Implementation plan**

1. Add nullable boolean `HasImages` to the request type. Keep null distinct from false.
2. In the query helper, place this filter near the other product-level filters, outside the variant `Any` block. Images belong to the product.
3. If true, require `p.Images.Any()`. If false, require no images. If null, add neither condition.
4. Keep filtering on the query before the controller counts or loads results; do not first include all image objects just to calculate a count in memory.
5. Arrange a private category with product A having one image, B having two, and C having none. Assert true returns A/B, false returns C, and omitted returns all three.
6. With true and page size one, require one item and totalCount=2. Add a price or active-state condition that excludes one imaged product to prove filter composition.
7. Test `hasImages=maybe` and a no-match combination. Record the new option in the API reference.

**Test and demo:** `dotnet test --filter FullyQualifiedName~CatalogSearchApiTests`. After implementation, `?hasImages=false` is a catalog-maintenance view over the same endpoint.

**Common trap:** treating false as "ignore the filter." A nullable boolean has three meaningful states here.

**Done when:** true, false, omitted, invalid, and combined-filter cases pass, and the docs avoid implying URL validation.

## JS-12: Stable variant order in product responses

**Status:** Planned; not implemented. **Starting knowledge:** ordering a mapped collection.

**User story:** As a client application developer, I want variants listed in a consistent SKU order, so a product's choice list does not depend on insertion or database materialization order.

**Current behavior:** `ProductResponse.From` maps the variant collection in its existing order without an explicit response-order contract.

**Acceptance criteria**

- Every `ProductResponse.variants` array sorts by SKU using ordinal, case-sensitive string ordering, then by variant ID for a tie.
- Apply ordering in the in-memory response mapper, where an explicit string comparer can be used.
- Create, list, ID lookup, and slug lookup agree on the same variant sequence.
- Product-level sort/filter behavior and variant values are unchanged. All variants are still returned.

**Files to open:** [ProductContracts.cs](../src/Agora.Api/Contracts/ProductContracts.cs) (`ProductResponse.From`), [ProductsController.cs](../src/Agora.Api/Controllers/ProductsController.cs), [ProductsApiTests.cs](../tests/Agora.Tests/Integration/ProductsApiTests.cs).

**Implementation plan**

1. Find `product.Variants.Select(VariantResponse.From)` in the mapper. This is the common place used by product responses.
2. Order the loaded collection by SKU with `StringComparer.Ordinal`, followed by ID, before mapping it to a list. Do not place this comparer inside an EF-translated SQL expression.
3. Create a product whose variants are submitted in suffix order Z, A, M under a unique common SKU prefix. Give names and prices a different order so the test cannot accidentally validate sorting by the wrong field.
4. Assert A, M, Z in the create response. Fetch by ID and slug and assert the same sequence.
5. Fetch the product through a category-filtered list request and assert its variant sequence there as well.
6. Preserve assertions on quantities of returned variants, IDs, prices, and options. This story changes order, not data.
7. Document the new array ordering. Explain that ordinal ordering is a technical, predictable order, not localized alphabetical sorting.

**Test and demo:** `dotnet test --filter FullyQualifiedName~ProductsApiTests`. Compare the same product across create, detail, and list responses.

**Common trap:** only ordering the query used by `GetById`. That would leave other response paths with different behavior.

**Done when:** the four response paths agree, existing product sorts remain intact, and the response-order contract is documented.

## JS-13: Product variant count

**Status:** Planned; not implemented. **Starting knowledge:** a computed DTO field and JSON assertions.

**User story:** As a shopper, I want a product response to say how many variants are available as choices, so a client can display a simple "three options" badge.

**Current behavior:** clients can count the returned variant array themselves; no explicit `variantCount` field exists.

**Acceptance criteria**

- `ProductResponse` includes integer `variantCount`, equal to the number of entries in its `Variants` response collection.
- This counts listed choices, not stock units and not just variants matching a search filter.
- A product containing three variants reports three, including when only one variant makes the product match a price or stock filter.
- The field is computed without a database column, extra query, or constructor parameter.
- List and both detail routes serialize the field.

**Files to open:** [ProductContracts.cs](../src/Agora.Api/Contracts/ProductContracts.cs), [ProductsApiTests.cs](../tests/Agora.Tests/Integration/ProductsApiTests.cs), [CatalogSearchApiTests.cs](../tests/Agora.Tests/Integration/CatalogSearchApiTests.cs).

**Implementation plan**

1. Add a public getter-only integer property in the `ProductResponse` record body, based on `Variants.Count`.
2. Avoid computing it from stock or adding a `db.ProductVariants.CountAsync` call. The response already has the needed data.
3. Create a product with three variants with distinct prices; choose bounds that match exactly one of them.
4. Assert the list still returns all three variants and `variantCount` is three. This records the existing product-filter contract.
5. Read raw JSON or a `JsonDocument` to require the `variantCount` key. Merely deserializing into the new C# DTO and reading its getter could hide an absent server field.
6. Check ID and slug lookup, plus a one-variant product. If you unit-test an empty manually constructed response, expect zero without adding an API route to create variant-less products.
7. Add the field and its meaning to the product-response documentation.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~ProductsApiTests|FullyQualifiedName~CatalogSearchApiTests"`. Compare `variantCount` to the full array length on a narrowly filtered product.

**Common trap:** interpreting "options" as units available for sale. Three variants may each have very different stock.

**Done when:** JSON contains the field, filtered responses count all returned variants, and no persistence changes are introduced.

## JS-14: Filter product reviews by minimum rating

**Status:** Planned; not implemented. **Starting knowledge:** filtering before counting and preserving an existing visibility rule.

**User story:** As a shopper, I want to view reviews at or above a chosen rating, so I can narrow the feedback I read for one product.

**Current behavior:** the public product-review list shows approved reviews, newest first, with paging. It has no rating filter.

**Acceptance criteria**

- Add optional integer `minRating` to `GET /api/products/{productId}/reviews`.
- Values 1–5 are accepted and inclusive; 0, 6, and malformed values return 400. Omitted means no rating filter.
- Only approved reviews for the requested product may appear, regardless of rating.
- Counts reflect the approved, product-specific, rating-filtered set before paging.
- Existing product-not-found behavior and reviewer response mapping remain intact. The admin moderation queue is unchanged.

**Files to open:** [ReviewsController.cs](../src/Agora.Api/Controllers/ReviewsController.cs) (`ListForProduct`), [Review.cs](../src/Agora.Domain/Entities/Review.cs), [ReviewsApiTests.cs](../tests/Agora.Tests/Integration/ReviewsApiTests.cs).

**Implementation plan**

1. Add nullable `minRating` and its 1–5 validation to `ListForProduct`, not the admin `Moderate` action.
2. Keep the product existence check. Start the review query with the existing product-ID and Approved predicates.
3. Add the rating predicate to that base query only when the input is present. Then retain ordering, customer joining, counting, and paging. Keep the query type composable before ordering/joining.
4. Arrange a read-test fixture directly through `WithDbAsync`: use a real product and distinct registered customers, then create reviews using the domain constructor. Use `Approve(fixedTime)` or `Reject(note, fixedTime)` for status setup. This avoids making payment workflows prerequisites for a read-filter test.
5. Create approved ratings 3, 4, and 5 plus pending/rejected rating-5 reviews. Query minRating=4 with page size one; totalCount must be two and no non-approved review may appear.
6. Add another product's approved review and ensure it never leaks into this product's results. Test omitted, exact rating-five boundary, invalid inputs, and an unknown product ID.
7. Document the filter on the public product-review route only. Do not weaken the real review-creation eligibility checks to simplify fixtures.

**Test and demo:** `dotnet test --filter FullyQualifiedName~ReviewsApiTests`. Try a product with known approved ratings using `?minRating=4`.

**Common trap:** rebuilding the query for the new filter and forgetting `Status == Approved`. That would expose unmoderated content.

**Done when:** the rating boundary works, counts are correct, and tests explicitly exclude other products and unapproved reviews.

## JS-15: Read product reviews oldest first

**Status:** Planned; not implemented. **Starting knowledge:** sorting and allowed-value validation.

**User story:** As a shopper, I want to read a product's reviews from oldest to newest, so I can follow how feedback has developed over time.

**Current behavior:** the public product-review list always orders newest first, with no explicit unique tie-breaker.

**Acceptance criteria**

- Optional `sort` accepts `newest` and `oldest`, ignoring case and trimming whitespace.
- Omitted or blank means newest. Any other value returns 400, including numeric text.
- Newest orders by descending CreatedAt, then ascending ID; oldest orders by ascending CreatedAt, then ascending ID.
- Product scope, Approved visibility, pagination, and any JS-14 minimum-rating filter stay intact.
- The admin moderation queue's order is unchanged.

**Files to open:** [ReviewsController.cs](../src/Agora.Api/Controllers/ReviewsController.cs) (`ListForProduct`), [ReviewsApiTests.cs](../tests/Agora.Tests/Integration/ReviewsApiTests.cs), [Review.cs](../src/Agora.Domain/Entities/Review.cs).

**Implementation plan**

1. Normalize the optional sort input near the beginning of the public list action. Check the two allowed names and return a 400 problem for any other nonblank value.
2. Build the filtered review query first. Apply the chosen CreatedAt ordering and the ID tie-breaker before pagination.
3. Keep ordering effective through the projection/join: construct the joined row query with an explicit final order on `row.Review.CreatedAt` and `row.Review.Id` before `Skip`/`Take` if the existing join structure makes placement unclear.
4. Arrange approved reviews with three fixed, different timestamps; do not use sleeps to create order. Use valid distinct customer IDs and the existing product relationship.
5. Assert newest and oldest return reverse chronological sequences. Test omitted, blank, mixed-case, and unknown sort values.
6. Add two reviews with the same timestamp and known IDs. Compare the tie order against a SQLite ID ordering and verify it across page boundaries on your isolated product.
7. Add a pending review at an extreme timestamp to prove sorting did not remove visibility filtering. If JS-14 exists, include one request combining both options.
8. Document defaults, allowed values, and the fact that stable ordering on unchanged data does not prevent page shifts under concurrent inserts.

**Test and demo:** `dotnet test --filter FullyQualifiedName~ReviewsApiTests`. Compare the same product with `?sort=newest` and `?sort=oldest`.

**Common trap:** sorting only after `ToListAsync` has loaded one page. That rearranges a page instead of selecting the correct page from the complete ordered set.

**Done when:** chronology, ties, validation, and approved-only visibility all have explicit assertions.

## JS-16: An in-stock flag on inventory responses

**Status:** Planned; not implemented. **Starting knowledge:** available stock is on-hand minus reserved.

**User story:** As a client application developer, I want an explicit in-stock boolean on an inventory response, so I can display an availability badge using the API's existing stock calculation.

**Current behavior:** `InventoryResponse` exposes on-hand, reserved, and available quantities, but no boolean flag.

**Acceptance criteria**

- Inventory responses include `inStock`, true exactly when `QuantityAvailable > 0`.
- On-hand 5/reserved 0 is true; 5/5 is false; 0/0 is false; 5/4 is true.
- GET and successful admin stock-update responses both serialize the field.
- A missing inventory record still yields the existing 404. The flag does not replace authorization, reserve stock, or guarantee later availability.
- Existing numeric fields and entity behavior stay intact.

**Files to open:** [InventoryContracts.cs](../src/Agora.Api/Contracts/InventoryContracts.cs), [InventoryController.cs](../src/Agora.Api/Controllers/InventoryController.cs), [InventoryItem.cs](../src/Agora.Domain/Entities/InventoryItem.cs), [InventoryApiTests.cs](../tests/Agora.Tests/Integration/InventoryApiTests.cs).

**Implementation plan**

1. Add a public computed boolean getter to `InventoryResponse`, based on the existing available-quantity field. Do not add another state property to `InventoryItem`.
2. Read the `From` mapper and confirm both controller actions already use it. That shared response type should make the new field available on both paths.
3. Arrange a unique variant/inventory fixture. For reserved cases, call the domain's `Reserve` method in a test database scope and save; do not try to bypass private setters.
4. Test all four stock combinations from the acceptance criteria. The fully reserved example is especially important because on-hand stock is still positive.
5. In at least one GET test, inspect raw JSON and require `inStock` with a boolean type and expected value.
6. Make an authorized stock update and check its response includes the field. Keep anonymous update rejection and missing-record tests passing.
7. Add the field and formula to the inventory response documentation.

**Test and demo:** `dotnet test --filter FullyQualifiedName~InventoryApiTests`. Fetch a variant whose inventory is fully reserved in the test fixture and show why positive on-hand stock does not imply true.

**Common trap:** checking `QuantityOnHand > 0`. That would offer units already reserved by checkout.

**Done when:** JSON presence and all stock boundaries pass, and no new writes or columns are needed to calculate the flag.

## JS-17: Filter shipping methods by delivery time

**Status:** Planned; not implemented. **Starting knowledge:** an optional integer query parameter.

**User story:** As a shopper, I want to list active shipping methods whose maximum delivery estimate fits my time limit, so I can choose a sufficiently fast option.

**Current behavior:** the public shipping-method list returns active methods ordered by base rate; it has no delivery-time filter.

**Acceptance criteria**

- Add optional integer `maxDeliveryDays` to `GET /api/shipping-methods`.
- Accept 0 through 365. Missing input preserves the existing active list. Negative, above-limit, and malformed values return 400.
- A method matches when its `MaxDays` is less than or equal to the requested value, not merely when its minimum estimate fits.
- Inactive methods remain excluded. No matches returns 200 with an empty array.
- Preserve base-rate ordering; use code as an ascending tie-breaker for equal base rates. Response shape and checkout selection behavior remain unchanged.

**Files to open:** [ShippingMethodsController.cs](../src/Agora.Api/Controllers/ShippingMethodsController.cs) (`List`), [ShippingMethod.cs](../src/Agora.Domain/Entities/ShippingMethod.cs), [ShippingApiTests.cs](../tests/Agora.Tests/Integration/ShippingApiTests.cs).

**Implementation plan**

1. Add the nullable query parameter and range validation to `List`. Keep the endpoint public.
2. Start with the existing `AsNoTracking` query and `IsActive` predicate. Add `MaxDays <= requested` only when the value is present.
3. Apply base-rate ordering plus code tie-breaker, then materialize and use the existing response mapper.
4. Arrange active methods with min/max estimates 1–2, 2–5, and 0–0 days, plus an inactive 0–1-day method. Give fixtures unique codes and valid other fields.
5. Query for two days and assert the 1–2 and 0–0 options qualify; the 2–5 and inactive options do not. This catches accidentally filtering on `MinDays`.
6. Test zero, 365, omitted input, -1, 366, and a non-integer string. When other seed methods also match, assert fixture membership instead of assuming a global result count.
7. With two same-rate methods, compare their relative code order. Update the list endpoint's documentation and show a `?maxDeliveryDays=2` request.

**Test and demo:** `dotnet test --filter FullyQualifiedName~ShippingApiTests`. Explain why a method estimated at 2–5 days fails a two-day maximum request.

**Common trap:** interpreting the field as an exact guaranteed arrival date. It filters the stored estimate's maximum; it does not calculate a calendar arrival date.

**Done when:** boundaries, inactive exclusion, min-versus-max distinction, and default behavior all pass.

## JS-18: Reject undefined shipping rate types

**Status:** Planned; not implemented. **Starting knowledge:** allowed values and validating before mutation.

**User story:** As a shipping administrator, I want invalid rate-type names rejected clearly, so accidental numeric or unsupported values cannot create a misleading shipping configuration.

**Current behavior:** create and update use `Enum.TryParse`. Enum parsing can accept numeric text even when it does not represent one of the intended named options.

**Acceptance criteria**

- Create and update accept only the names `Flat` and `Weighted`, case-insensitively, after trimming surrounding whitespace.
- Unsupported nonblank names, numeric strings such as `0`, `1`, `2`, and `999`, and comma-separated names return 422 with the existing rate-type problem response.
- Missing/empty required input keeps its existing model-validation 400 behavior.
- A rejected create saves no method; a rejected update leaves all fields unchanged.
- Admin authorization and existing day/rate validation are preserved.

**Files to open:** [ShippingMethodsController.cs](../src/Agora.Api/Controllers/ShippingMethodsController.cs) (`Create` and `Update`), [ShippingMethod.cs](../src/Agora.Domain/Entities/ShippingMethod.cs) (`ShippingRateType`), [ShippingApiTests.cs](../tests/Agora.Tests/Integration/ShippingApiTests.cs).

**Implementation plan**

1. Read both parsing blocks. Write down the accepted public names independently of the enum's integer values.
2. Add an admin API test using `rateType: "999"` with otherwise valid fields. This isolates the parsing problem from unrelated validation.
3. Normalize the nonblank string, require it to match one of the two allowed names, and only then map it to the enum. A small private helper is acceptable if it keeps both actions consistent.
4. Do not rely solely on `Enum.IsDefined` after parsing: that would still accept the numeric strings for defined values, which this contract rejects.
5. Apply the same check to create and update before entity mutation or default-method changes.
6. Use a theory for the invalid strings. For each update rejection, reload a uniquely created method and compare its original name, rates, day range, active/default flags, and rate type.
7. Test valid lowercase/mixed-case names and surrounding whitespace, as well as the existing required-input and authorization paths.
8. Document that the input contract accepts names rather than numeric enum values.

**Test and demo:** `dotnet test --filter FullyQualifiedName~ShippingApiTests`. Send an invalid update that also attempts to change the method name; confirm the error and the unchanged saved name.

**Common trap:** fixing creation but overlooking update, which contains a separate parsing path.

**Done when:** both mutation routes enforce the same named-value contract, rejected writes leave no changes, and normal shipping tests pass.

## JS-19: Search my wishlist names

**Status:** Planned; not implemented. **Starting knowledge:** a text filter that preserves customer ownership.

**User story:** As a signed-in shopper, I want to search my wishlist names, so I can quickly find a particular gift or shopping list.

**Current behavior:** `GET /api/me/wishlists` returns the caller's summaries and ensures a default list exists. Rename and delete already exist; this story adds only name filtering.

**Acceptance criteria**

- Add optional `search` for a literal substring of the wishlist name, using the existing SQLite LIKE ASCII case behavior.
- Trim nonblank input; missing/blank means no filter. Reject raw input longer than 100 characters with 400.
- Escape `%`, `_`, and backslash. Matching another customer's list name never grants access to it.
- Keep default-first, then creation-time ordering, adding ID as the final tie-breaker.
- Preserve the existing get-or-create-default behavior even when the filter hides that default from the response. No matches returns an empty array, not 404.

**Files to open:** [WishlistsController.cs](../src/Agora.Api/Controllers/WishlistsController.cs) (`List`), [ProductCatalogQuery.cs](../src/Agora.Api/Queries/ProductCatalogQuery.cs) for literal escaping, [WishlistsApiTests.cs](../tests/Agora.Tests/Integration/WishlistsApiTests.cs).

**Implementation plan**

1. Add the optional string input and maximum-length validation to `List`. Keep the class's `[Authorize]` behavior.
2. Preserve `GetOrCreateDefaultAsync` at the existing point. Do not silently change the endpoint's current first-use behavior while adding search.
3. Build the summaries query with the current customer-ID predicate first. Add the escaped name predicate to that same query before ordering and projection.
4. Retain the current item-count projection and response type. This story does not add pagination or modify wishlist contents.
5. Create separate customers A and B, both with similarly named lists. As A, search the shared phrase and require only A's list ID in the results.
6. Add blank, no-match, literal wildcard, case-variation, and too-long input cases. Verify anonymous access remains 401.
7. For a newly registered customer, search a term that excludes `Favorites`; expect an empty response, then call the unfiltered list twice and confirm one default list, not repeated creation.
8. Document both filtering and the preserved default-list behavior.

**Test and demo:** `dotnet test --filter FullyQualifiedName~WishlistsApiTests`. Search for `?search=gift` using the token for a customer with a matching named list.

**Common trap:** starting a new query for search and omitting `CustomerId == currentCustomerId`.

**Done when:** only the caller's matching lists appear, literal search works, and first-use default handling still behaves as before.

## JS-20: Wishlist stock summary counts

**Status:** Planned; not implemented. **Starting knowledge:** counting items based on an existing boolean.

**User story:** As a shopper viewing a wishlist, I want counts of in-stock and out-of-stock entries, so I can see how much of the list is currently available without counting every item.

**Current behavior:** detailed wishlist items already expose `InStock` and `BackInStock`. The detailed `WishlistResponse` has no aggregate availability counts.

**Acceptance criteria**

- Add `inStockItemCount` and `outOfStockItemCount` to detailed wishlist responses, derived from mapped `Items[i].InStock`.
- Empty lists return zero/zero. Three entries with true, false, true return two/one.
- The counts sum to the number of entries. They count entries, not physical units or historical restock events.
- Missing or fully reserved inventory counts as out of stock under the existing item mapper's rule.
- Preserve existing stock-observation side effects, `BackInStock`, summary-list shape, and ownership rules.

**Files to open:** [WishlistContracts.cs](../src/Agora.Api/Contracts/WishlistContracts.cs) (`WishlistResponse`, `WishlistItemResponse.From`), [WishlistsController.cs](../src/Agora.Api/Controllers/WishlistsController.cs) (`ToResponseWithObservationAsync`), [WishlistsApiTests.cs](../tests/Agora.Tests/Integration/WishlistsApiTests.cs).

**Implementation plan**

1. Find the existing `InStock` calculation on each item and the detailed response's `Items` collection. Use those mapped booleans as the count source.
2. Add computed integer getters to `WishlistResponse`; do not add fields to `Wishlist`, alter its constructor, or query inventory again.
3. Create a fresh customer's list with three unique variants, two available and one unavailable. Prefer new fixture variants so other stock tests cannot change the example.
4. Read the list and assert two/one counts, their sum, and JSON presence of both properties. Keep existing item-level flags asserted too.
5. In a separate fixture, reserve all units of a variant through its inventory domain method, save, and check that it counts as unavailable. Add an empty-list case.
6. Read via the default-list and ID routes where applicable; both should use the same detailed response type. A normal item-add response should also include the getters automatically.
7. Update only the detailed wishlist-response documentation. The summary-list endpoint still has its existing `ItemCount` field and different response shape.

**Test and demo:** `dotnet test --filter FullyQualifiedName~WishlistsApiTests`. Compare the two new counts to the existing item-level `inStock` values.

**Common trap:** using `BackInStock` for the available count. A currently available item need not have been observed out of stock before.

**Done when:** empty, mixed, and fully reserved cases pass and no duplicate inventory-query or observation logic has been introduced.

## JS-21: Clear one wishlist without deleting it

**Status:** Planned; not implemented. **Starting knowledge:** the existing remove-item action and ownership filtering.

**User story:** As a signed-in shopper, I want to empty one of my wishlists in one request while keeping its name and identity, so I can reuse the list for a new occasion.

**Current behavior:** the API can delete one item or delete a non-default list. It does not provide an action to clear every item while retaining the list.

**Acceptance criteria**

- Add authenticated `DELETE /api/me/wishlists/{id}/items` returning 204 for an owned list.
- Remove that list's items while retaining the wishlist row, ID, name, and default flag.
- Clearing an already-empty owned list also returns 204. Default lists can be cleared even though they cannot be deleted as lists.
- Unknown or another customer's list returns 404; anonymous access remains 401.
- Other lists, cart contents, product inventory, and orders are unaffected.

**Files to open:** [WishlistsController.cs](../src/Agora.Api/Controllers/WishlistsController.cs) (`RemoveItem`, `LoadAsync`, `Delete`), [AgoraDbContext.cs](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs) (`WishlistItems`), [WishlistsApiTests.cs](../tests/Agora.Tests/Integration/WishlistsApiTests.cs).

**Implementation plan**

1. Add an action with the route above inside the already-authorized controller. Name it `ClearItems` to distinguish it from deleting the list.
2. Obtain the current customer ID from claims. Call the existing `LoadAsync(id, customerId, ct)` so ownership is included in the database lookup.
3. Return 404 when no owned list is found. Do not look up by ID alone or accept a customer ID in the body.
4. Mark only the loaded wishlist's item entities for deletion through `db.WishlistItems.RemoveRange`. Do not call `db.Wishlists.Remove` or reuse the whole-list deletion action.
5. Save once, then return `NoContent`. An empty collection should naturally result in the same successful response.
6. Test with A's populated list, A's second list, and B's populated list. Clear only the target and verify all three lists in fresh requests with their respective owners.
7. Assert the target's ID/name/default flag still exist and its items are empty. Repeat the clear to check 204, then test default-list clearing, unknown ID, wrong owner, and anonymous access.
8. Compare inventory before and after for the target variants and verify an existing cart is untouched. Add the route and semantics to the API reference.

**Test and demo:** `dotnet test --filter FullyQualifiedName~WishlistsApiTests`. Create a practice wishlist under your own test customer, clear it, and GET the same ID again.

**Common trap:** interpreting "clear" as deleting the parent row. The same wishlist must remain usable after the operation.

**Done when:** fresh reads prove scoped deletion, retained list identity, repeatability, and owner isolation.

## JS-22: Read one of my saved addresses

**Status:** Planned; not implemented. **Starting knowledge:** a read endpoint and an ownership predicate.

**User story:** As a signed-in shopper, I want to retrieve one of my saved addresses by ID, so a client can open its details without fetching my entire address book.

**Current behavior:** the API lists, adds, updates, deletes, and selects default addresses. It has no individual address GET action.

**Acceptance criteria**

- Add authenticated `GET /api/me/addresses/{id}` returning the existing `CustomerAddressResponse`.
- The lookup must include both the requested ID and the signed-in customer's ID.
- An owned record returns 200; unknown or another customer's ID returns 404; anonymous access returns 401.
- Admin identity does not create a cross-customer exception on this `/me` route: it still means the caller's own data.
- Reading does not update label, address fields, default selection, or timestamps.

**Files to open:** [MeController.cs](../src/Agora.Api/Controllers/MeController.cs) (`Addresses`, `UpdateAddress`), [AddressBookContracts.cs](../src/Agora.Api/Contracts/AddressBookContracts.cs), [AddressBookApiTests.cs](../tests/Agora.Tests/Integration/AddressBookApiTests.cs).

**Implementation plan**

1. Add `GetAddress` with `[HttpGet("addresses/{id:guid}")]` inside `MeController`. The existing class-level authorization remains in force.
2. Obtain the current customer ID exactly as neighboring address methods do. Do not add a customer-ID request parameter.
3. Query `CustomerAddresses.AsNoTracking()` with both ID and customer predicates and pass the cancellation token to the asynchronous lookup.
4. Return `NotFound` or `Ok(CustomerAddressResponse.From(address))`. There is no need for `SaveChangesAsync` on this read.
5. Register A and B with distinct test emails. As A, create a saved address, then GET its real ID and compare every returned field to creation.
6. As B, request that same actual ID and expect 404. Use a separate unauthenticated client for 401. Include an unknown GUID and confirm the original list route still works.
7. Re-read the address after GET and compare its `IsDefault` and `CreatedAt` values. This catches an accidental call to a mutating helper.
8. Document the new route. Changing the existing create action's Location header is a separate enhancement, so leave it alone in this story.

**Test and demo:** `dotnet test --filter FullyQualifiedName~AddressBookApiTests`. Use the address ID returned by a creation request, not an invented ID.

**Common trap:** testing only an unknown GUID and assuming that proves ownership. The critical test uses A's existing resource while signed in as B.

**Done when:** owner, other-customer, anonymous, and missing-resource behavior are explicit and the read causes no saved changes.

## JS-23: Filter my address book by country

**Status:** Planned; not implemented. **Starting knowledge:** input normalization and filtering owned data.

**User story:** As a signed-in shopper, I want to filter my saved addresses by country code, so I can find the right destination more quickly.

**Current behavior:** the address list returns all of the caller's addresses. `AddressDto.ToAddress` copies country text as supplied, so stored codes can have mixed casing.

**Acceptance criteria**

- Add optional `country` to `GET /api/me/addresses`; trim input and compare two-letter ASCII codes case-insensitively.
- Missing or blank input means no filter. Nonblank input that is not exactly two ASCII letters after trimming returns 400.
- This validates code format only, not membership in a country registry; a well-formed unused code returns an empty list.
- Match stored `us` and `US` for a `country=us` query. Never include another customer's matching address.
- Keep default-first, then CreatedAt ordering, with ID as a final tie-breaker. Do not rewrite stored country values.

**Files to open:** [MeController.cs](../src/Agora.Api/Controllers/MeController.cs) (`Addresses`), [OrderContracts.cs](../src/Agora.Api/Contracts/OrderContracts.cs) (`AddressDto.ToAddress`), [AddressBookApiTests.cs](../tests/Agora.Tests/Integration/AddressBookApiTests.cs).

**Implementation plan**

1. Add nullable country input to `Addresses`. Convert blank input to no filter; otherwise trim, uppercase invariantly, and validate length two plus letters A–Z.
2. Return a 400 problem for invalid nonblank input before executing the query.
3. Keep the customer-ID predicate in the base `AsNoTracking` query. Add a country comparison only when the normalized input exists.
4. Normalize the stored column inside the SQL expression using EF-translatable `Trim()` and `ToUpper()`, compared to the normalized local value. Do not call a custom C# normalization helper inside the database expression or materialize the whole address book first.
5. Apply existing default/date ordering and ID tie-breaker, then map the filtered results. No entity assignment or save is needed.
6. As A, create addresses in `US`, `us`, and `GB`; as B, create a `US` address. Query as A using `us`, uppercase, and URL-encoded surrounding spaces. Require A's two US entries only.
7. Test blank, no match, one letter, three letters, digits, non-ASCII letters, and anonymous access. Include one default matching address to check its ordering.
8. Re-read saved data to show casing was not rewritten. Document normalization and format-only validation.

**Test and demo:** `dotnet test --filter FullyQualifiedName~AddressBookApiTests`. Compare filtered and unfiltered lists for a customer with multiple destination countries.

**Common trap:** assuming stored countries were already uppercased by `ToAddress`; the current mapper does not do that.

**Done when:** mixed-case storage, invalid inputs, no-match behavior, and ownership isolation pass using actual SQLite queries.

## JS-24: Filter my order history by status

**Status:** Planned; not implemented. **Starting knowledge:** optional filtering, enum names, and owner-scoped queries. Start this after a simpler filter story feels comfortable.

**User story:** As a signed-in shopper, I want to view only orders in a selected status, so I can find paid, shipped, cancelled, or refunded orders without scanning my full history.

**Current behavior:** `MeController.Orders` lists the caller's orders newest first with paging, but offers no status filter.

**Acceptance criteria**

- Optional `status` accepts the named values `Pending`, `Paid`, `PartiallyFulfilled`, `Fulfilled`, `Cancelled`, and `Refunded`, ignoring case and surrounding whitespace.
- Missing/blank means all of the caller's orders. Unknown names, numeric strings, and comma-separated values return 400.
- Filtering happens before counting/paging and retains the current-customer predicate. Guest orders and other customers' orders are excluded.
- Keep newest-first CreatedAt order and use ID as a unique ascending tie-breaker.
- The endpoint remains a read; it does not change order state, call a gateway, or alter guest order-access routes.

**Files to open:** [MeController.cs](../src/Agora.Api/Controllers/MeController.cs) (`Orders`), [Order.cs](../src/Agora.Domain/Entities/Order.cs) (`OrderStatus` and transition methods), [AuthApiTests.cs](../tests/Agora.Tests/Integration/AuthApiTests.cs) (`OrderHistory_DoesNotIncludeOtherCustomersOrGuestOrders`).

**Implementation plan**

1. Add optional status input to `Orders`. Normalize it and treat whitespace-only input as omitted.
2. Validate against the actual named enum values before parsing. Do not rely on `Enum.TryParse` alone, because numeric text is not part of this public contract.
3. Keep the customer predicate and item loading. Add equality against the parsed `OrderStatus` only when a status was supplied.
4. Apply final CreatedAt/ID ordering after filtering, then count and load the requested page. Keep response mapping unchanged.
5. Reuse the existing order-history test setup. For additional read-only fixtures, use `WithDbAsync` with unique order numbers, real customer IDs, required address data, and fixed timestamps. Obtain statuses through domain transitions: Pending is initial; Paid uses `MarkPaid`; cancellation can follow Pending; partial/fulfilled/refunded cases can follow Paid through their respective methods.
6. Arrange A with at least two Paid orders and a Cancelled order, B with a Paid order, and a guest Paid order. Query A's Paid history with page size one and require totalCount=2 and only A's matching IDs across pages.
7. Use small separate fixtures or a theory to cover all six accepted names. Test lowercase, blank, `999`, `1`, unknown text, mixed names, and anonymous access.
8. Keep the real checkout and fulfillment services unchanged: fixture setup tests the history query, not those workflows. Document the option only on `/api/me/orders`.

**Test and demo:** `dotnet test --filter FullyQualifiedName~AuthApiTests`. Compare `/api/me/orders` and `/api/me/orders?status=paid` using the same customer's identity.

**Common trap:** a status-only database predicate can leak other customers' matching orders. Keep status and ownership in the same query.

**Done when:** every supported name, filtered counts, pagination, and customer/guest isolation is covered without introducing any mutation behavior.

## JS-25: Reject reversed top-product report dates

**Status:** Planned; not implemented. **Starting knowledge:** comparing two optional date inputs after defaults are resolved.

**User story:** As an administrator, I want the top-products report to reject a start date later than its end date, so a mistaken date range is not presented as a valid empty sales report.

**Current behavior:** `Sales` validates its resolved range. `TopProducts` resolves the same style of defaults but does not perform the corresponding reversed-range check.

**Acceptance criteria**

- `GET /api/admin/reports/top-products` returns 400 with a clear problem response when resolved `from > to`.
- Equal instants are accepted. Existing inclusive range comparisons, limit validation, response shape, and revenue calculations stay unchanged.
- With dates omitted, preserve the last-30-days default. With only `to`, derive `from` from that `to` as the current code does.
- Compare actual `DateTimeOffset` instants, not their formatted strings. Different timezone offsets can describe the same moment.
- The endpoint remains admin-only and performs no writes.

**Files to open:** [AdminReportsController.cs](../src/Agora.Api/Controllers/AdminReportsController.cs) (`Sales`, `TopProducts`), [AdminReportsApiTests.cs](../tests/Agora.Tests/Integration/AdminReportsApiTests.cs).

**Implementation plan**

1. Read the default-range and validation code in both actions side by side. Locate the gap before `TopProducts` builds its order-item query.
2. Add a regression test with otherwise valid input where `from=2026-02-02T00:00:00Z` and `to=2026-02-01T00:00:00Z`. Authenticate as admin. Expect 400 rather than a successful empty array.
3. After calculating `rangeTo` and `rangeFrom` in `TopProducts`, add the same greater-than validation style used in `Sales`.
4. Return a problem response before the database aggregation. Do not refactor both report methods or change their money calculations for this small story.
5. Test a valid increasing range, equal timestamps, omitted dates, and `to` alone. Existing report fixture helpers can supply known orders where a nonempty result is needed.
6. Add equivalent-instant inputs such as `2026-02-01T02:00:00+02:00` and `2026-02-01T00:00:00Z`; they should be accepted. URL-encode each timestamp with `Uri.EscapeDataString` when constructing the test URL so `+` is preserved.
7. Test `from` well after the current time with omitted `to` to verify validation applies to resolved defaults. Use a comfortably future value rather than a millisecond boundary.
8. Keep existing invalid-limit and authorization tests passing. Update the report's error documentation with reversed-range 400 behavior.

**Test and demo:** `dotnet test --filter FullyQualifiedName~AdminReportsApiTests`. Show the previously misleading reversed-range request and its new error response, then show an ordinary valid report still works.

**Common trap:** checking whether the raw nullable `from` is greater than raw nullable `to` before applying defaults. That can miss an invalid range when one input is omitted.

**Done when:** reversed, equal, equivalent-offset, partial-default, and normal requests behave as specified, with existing report calculations untouched.

## A completion checklist to reuse for any story

Copy this into your own task notes after choosing one story. These boxes are intentionally unchecked; no story has been completed by this document.

- [ ] I can explain the user benefit in one sentence.
- [ ] I found the listed existing methods and ran the starting tests.
- [ ] My fixture isolates the scenario from other tests' mutable data.
- [ ] I added an assertion that distinguishes the requested behavior from the old behavior.
- [ ] I implemented only the chosen story and preserved any earlier story work.
- [ ] I covered its invalid inputs, empty/boundary cases, and permissions where applicable.
- [ ] For a new JSON field, I checked actual JSON presence; for a write, I checked saved state with a fresh read.
- [ ] Focused tests pass, and I recorded the full-suite result before submitting.
- [ ] The API reference describes the new behavior and its defaults accurately.
- [ ] I reviewed the diff and can explain one implementation choice and one limit.

## When a step is unclear

Pause at that step, not at the entire story. Write: "I am on JS-__, step __. I expected __. I observed __. I inspected __." Ask a teammate for one concrete example or use [the mentor guide](15-mentor-guide.md). If a story grows into a migration, payment integration, broad authorization redesign, or a repository-wide refactor, stop and compare your work to its stated scope: those are separate projects, not hidden prerequisites here.
