# Worked example: a plausible filter that was wrong

**Outcome:** move from a bug report to a precise contract, a regression test, and a small refactor. This example is already implemented; the exercise is to explain and extend it.

## Reproduce before proposing architecture

A product has two variants, priced 10 and 100. A customer asks for prices from 20 through 40. The previous implementation used:

```csharp
query = query.Where(p => p.Variants.Any(v => v.Price.Amount >= min));
query = query.Where(p => p.Variants.Any(v => v.Price.Amount <= max));
```

The 100 variant satisfies the first condition; the 10 variant satisfies the second. The product passes even though neither variant is in range. In logic, "there exists an A" and "there exists a B" does not mean "there exists one thing that is both A and B."

The corrected expression puts the bounds in a single `Any`. Availability and currency belong inside that same expression because they describe the variant you could buy.

## Make the contract explicit

| Input | Meaning |
| --- | --- |
| `minPrice`, `maxPrice` | Inclusive bounds from 0 through 1,000,000, at most two decimal places; minimum cannot exceed maximum |
| `currency=usd` | Normalize to USD and match the variant currency; format validation is three ASCII letters, not a currency registry lookup |
| `inStock=true` | A matching variant has on-hand minus reserved stock greater than zero |
| `inStock=false` | A matching variant has no inventory record or no available stock |
| Omitted availability | Ignore stock |
| `search` | Literal substring in name or description; escape `%`, `_`, and the escape character |
| Unknown sort | Preserve existing fallback to newest |

A product with two different variants can appear in both availability searches. Filters choose products, while the response still includes **all** their variants. Price sorting still uses the cheapest variant overall for compatibility, even if another variant matched. Without a currency filter, numeric price comparisons span currencies; this is not conversion or exchange-rate comparison. These limitations are deliberate and belong in the API reference.

## Refactor by responsibility

[ProductSearchRequest](../../src/Agora.Api/Contracts/ProductSearchRequest.cs) owns input validation, including range relationships and offset overflow. [ProductCatalogQuery](../../src/Agora.Api/Queries/ProductCatalogQuery.cs) owns SQL composition and sorting. [ProductsController](../../src/Agora.Api/Controllers/ProductsController.cs) executes the query and maps the response. This removes a long parameter list and keeps the controller readable without adding a generic repository or dependency injection interface for a stateless helper.

The query appends `ThenBy(p => p.Id)` for ties. Otherwise equal names or timestamps leave page boundaries ambiguous. This stabilizes ordering on unchanged data; concurrent inserts can still shift offset pages. See [Microsoft's pagination explanation](https://learn.microsoft.com/en-us/ef/core/querying/pagination).

The offset guard widens before multiplication: `(long)(Page - 1) * PageSize`. Casting an already-overflowed integer result would be too late. The API rejects offsets larger than `int.MaxValue`, which `Skip` cannot accept.

## Read the tests like a reviewer

Run `dotnet test --filter FullyQualifiedName~CatalogSearchApiTests`. Find these cases:

1. A split range excludes the 10/100 product but includes a 30 product.
2. A cheap unavailable variant and expensive available variant cannot jointly satisfy cheap-and-available.
3. Reserved stock makes on-hand stock unavailable.
4. A price in EUR cannot satisfy a USD search through a different variant.
5. Equal sort keys use the same unique ordering across pages.
6. Invalid input returns a validation problem before query execution.

**Exercise:** in a disposable branch, temporarily split the range predicate back into two `Any` calls. Run the price-range regression and observe it fail, then undo that edit. This is a mutation check: does the test detect the bug it claims to cover?

**Checkpoint:** give a two-minute review of the change: trigger, old result, new result, regression evidence, compatibility choices, and remaining limitations. **Stretch:** implement backlog ticket L1 without changing the returned product shape.
