# Module 2: queries and counterexamples

[Bootcamp home](README.md) | [Module 1](01-values-and-contracts.md) | [Journal](journal.md)

**Implementation status:** the junior query changes are present and undergoing acceptance/regression verification. This lesson describes those changes; the tracker records when evidence is complete.

## A query is a recipe until it is executed

Open [ProductCatalogQuery](../../src/Agora.Api/Queries/ProductCatalogQuery.cs). The Where calls build an expression for EF to translate into SQL. They do not each load a new list. CountAsync and ToListAsync are execution points.

Write this sequence on paper:

```text
all products
  -> category/name/image filters
  -> one matching-variant condition
  -> stable order
  -> count the filtered set
  -> skip/take the requested page
  -> load/map response data
```

The count is taken before skip/take. If there are seven matches and page size is two, totalCount stays seven on every page. A page with one result does not imply there is only one matching record.

Say the same idea as a shop task: first choose all the books that fit your criteria; count that stack; then hand the customer its second packet of two. Counting only the packet answers the wrong question.

## The two-variant trap

One product has these choices:

| Variant | SKU | Price | Available |
| --- | --- | --- | --- |
| A | TEE-A | 10.00 USD | 0 |
| B | TEE-B | 20.00 USD | 10 |

A request asks for SKU TEE-A **and** in-stock items. Should the product match? No. The requested variant is sold out. The fact that a different variant is available does not make TEE-A available.

These are different statements:

```text
There is a variant with SKU TEE-A,
and there is a variant with available stock.

There is one variant with both SKU TEE-A and available stock.
```

The first statement is true in our example. The second is false. The API contract requires the second. That is why SKU, minimum/maximum price, currency, and availability belong inside the same Any expression.

Follow [Sku_price_and_stock_must_match_the_same_variant_and_keep_all_choices](../../tests/Agora.Tests/Integration/BootcampJuniorApiTests.cs). It deliberately creates this counterexample. A test with only one variant would not reveal the difference.

The returned product still lists all its variants. The matching condition selects products; it does not redefine a product's complete list of choices. This is why variantCount can be two when only one choice satisfied the filter.

## Literal search versus wildcard search

SQL LIKE treats `%` as any sequence and `_` as any single character. Our search contract treats user input as literal text. Searching for a name containing `50%` should not silently become a wildcard expression.

[QueryRules.LiteralContains](../../src/Agora.Api/Queries/QueryRules.cs) escapes backslash first, then percent and underscore, and surrounds the result with `%` for substring matching. The EF call explicitly uses backslash as its escape character. The order matters: escape the escape character before adding new escape sequences.

This is search semantics. It is not a claim that the old parameterized LIKE query was SQL injection. Parameter binding and literal-wildcard handling solve different problems.

Category search uses Name only. A matching description does not qualify. Wishlist search also preserves CustomerId in its database predicate. Think of each Where as another condition on the same set, not a replacement for earlier ownership filtering.

## Stable pages and overflow

Two categories can have the same name. Sorting by name alone leaves their relative order unspecified. Add ID as a unique tie-breaker before paging. The database then has a complete ordering for an unchanged dataset.

The offset formula is `(page - 1) * pageSize`. Valid positive integers can overflow when multiplied. Cast to a wider type **before** multiplication, then check the result fits the integer accepted by Skip. Casting the already-overflowed result is too late.

The [shared page rule](../../src/Agora.Api/Queries/QueryRules.cs) checks positivity, maximum page size, and widened offset together. This is a small reusable function with several actual callers, not a general query framework.

Stable ordering does not freeze data between requests. New rows can still shift offset pages. A later cursor story teaches a different traversal contract; it does not change what this simpler module promises.

## Parsing names is not the same as parsing enum values

Enum.TryParse can accept numeric text and comma-separated names. That is useful in some programs, but the shipping API promises only Flat or Weighted. The order-history filter likewise promises named states.

QueryRules.TryNamedEnum first checks the input against actual enum names, then parses the matched name. Normalizing case/outer whitespace does not mean accepting numbers. Compare requests with `Flat`, ` flat `, `0`, and `Flat, Weighted`; explain why only the first two should succeed.

## Read a test as an experiment

The junior acceptance tests use distinct generated IDs so one scenario's rows do not alter another scenario's count. They still share one factory database. Isolation comes from deliberately scoped fixtures, not from assuming each Fact starts with an empty shop.

For each test, identify:

1. The dataset that distinguishes correct from incorrect behavior.
2. The request that selects the interesting boundary.
3. The totalCount assertion as well as the returned-item assertion.
4. The existing behavior that must remain intact.

## Exercises

1. Variant A costs 5 and has no stock; B costs 50 and has stock. Does `maxPrice=10&inStock=true` match? Explain using one-variant logic.
2. Seven matching categories exist. Page size is two and page is four. What are totalCount, item count, and hasNextPage?
3. A category's name is “Kitchen” and description is “50% off.” Should name search for `%` match it?
4. Why can a count query and a page query disagree if another writer changes data between them?
5. Why does `ThenBy(id)` belong before Skip rather than after ToListAsync?
6. Two DateTimeOffset strings have different clock times but represent the same instant. Should the top-products date guard reject them as a reversed range?

## Answer explanations

1. No. Neither single variant satisfies both predicates. Separate Any calls would incorrectly let A satisfy price and B satisfy stock.
2. TotalCount seven, one item, hasNextPage false. Count describes the set; item count describes the selected packet.
3. No. The feature searches Name only. Neither searching another column nor treating percent as a wildcard is part of the contract.
4. They are separate executions without a shared snapshot guarantee. Deterministic ordering is not transaction isolation.
5. Ordering the already-loaded page cannot decide which tied rows should have belonged to that page in the first place.
6. No. Compare the instants, not formatted strings. Equal instants are accepted by the existing inclusive report contract.

## Small independent lab

Choose the category wildcard theory, predict what happens if escaping is removed, and temporarily make that change on your own branch. Run just that theory and read the failing count/ID assertion. Restore the correct implementation and explain why the counterexample was stronger than a plain “kitchen” search.
