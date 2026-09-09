# Learn to reason about SQL and performance

**Outcome:** identify where data is filtered, how much is loaded, and what evidence supports an optimization.

Agora stores monetary decimals as integer cents and tax rates using a different scale. Read [SqliteValueConverters](../../src/Agora.Infrastructure/Persistence/SqliteValueConverters.cs) and [ADR-0001](../adr/0001-decimal-as-cents.md). A change that works on C# objects may translate differently against these mapped columns. The catalog integration tests therefore use the actual SQLite provider.

## Inspect one query

Put a breakpoint after `ProductCatalogQuery.Apply` and inspect `query.ToQueryString()` with EF Core available in the debugger. Alternatively, temporarily log generated SQL in local development. Never copy credentials or customer payloads into your journal. Look for the product filter, the variant subquery, the inventory join, ordering, and LIMIT/OFFSET on the executed page query. The base query's debug SQL does not include paging added later.

`CountAsync` and the page fetch are separate database operations. The count can disagree with a later page if another writer changes the catalog between them. Decide whether approximate browsing consistency is acceptable before adding a transaction. Loading variants and images together can multiply joined rows, even when only a small product page is requested. A split query or projection has tradeoffs; measure first. See [EF Core efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying).

## Measure a question

Question: does deep offset pagination become costly with a larger catalog? Create disposable test data, record its size and distribution, run repeated shallow and deep page requests, and report median and slow-tail timings separately. Record SDK, database, machine, and whether caches were warm. A single fast request against eight products is not evidence for production scale.

Then inspect the query plan. An index is useful only if the executed predicate and ordering can use it; a leading-wildcard substring search deserves investigation rather than an automatic name index. Compare before and after with the same data and requests.

## Exercise: review a reporting query

Open `AdminReportsController.Sales`. It loads matching orders and their items before bucketing in memory. Identify which input controls the amount of work. Propose a bounded date range or a database aggregation, including how UTC weeks and money conversion remain correct. Do not assume product-list pagination solved reporting cost.

**Checkpoint:** produce one SQL excerpt, one measurement table, and one recommendation with a limitation. **Stretch:** design keyset pagination using the sort key plus unique ID; discuss why it changes arbitrary page-jump behavior. [Pagination documentation](https://learn.microsoft.com/en-us/ef/core/querying/pagination) explains this tradeoff.
