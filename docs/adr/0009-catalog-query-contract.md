# 0009: Explicit catalog query contract and same-variant filtering

Status: Accepted

## Context

Product listing combined HTTP validation, filtering, sorting, paging, and materialization in one action. Separate price-bound predicates incorrectly allowed different variants to satisfy each bound. New availability and currency filters would amplify that ambiguity. Tied sort values and unchecked offset multiplication also weakened the page contract.

## Decision

Use `ProductSearchRequest` for model and cross-field validation and a stateless `ProductCatalogQuery` helper in the API project to compose deferred EF expressions. Keep query execution and response mapping in the controller. Require one variant to satisfy all supplied price, currency, and availability conditions. Escape literal search text for LIKE. Add a unique ID sort tie-breaker and reject offsets that exceed the integer range.

Retain the established product response, all-variant price sorting, unknown-sort fallback, and page-number interface. Availability false means an unavailable matching variant, not that every variant of the product is unavailable. Currency matching does not perform conversion.

## Alternatives

Keeping everything in the controller requires fewer files but makes HTTP and query logic harder to examine separately. A generic repository or query framework would add abstractions beyond this endpoint's needs. Cursor pagination can improve deep traversal but changes caller navigation and needs its own contract and evidence; it is a follow-up exercise.

## Consequences

The feature has a clear validation boundary and a small extension point. Real HTTP/SQLite tests verify provider translation, same-variant semantics, literal search, and ordering. Prices must be within the product-creation range of 0 through 1,000,000 and have at most two decimal places, preventing query-boundary rounding and converter overflow. Invalid price ranges, negative prices, overlong searches, and overflowing offsets now return 400, and wildcard search characters now match literally; these are observable behavior changes.

Offset paging still shifts under concurrent writes. Count and page are separate queries. Price sorting may use a different variant from the one matching filters, and numeric amounts across currencies are not directly comparable economic values. Revisit if clients need matched-variant response data, converted prices, snapshot consistency, or large-catalog cursor traversal.
