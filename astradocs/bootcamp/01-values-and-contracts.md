# Module 1: values, counts, and API contracts

[Bootcamp home](README.md) | [Curriculum](curriculum.md) | [Journal](journal.md)

**Status:** module under construction alongside implementation. Examples below explain the intended contracts; consult the tracker for completion evidence.

## One fact, several useful views

Imagine a cart with two active lines: three blue shirts and one mug. A saved line contains five hats.

| Question | Answer | Reason |
| --- | --- | --- |
| How many active lines? | 2 | Count the distinct active entries |
| How many active units? | 4 | Add their quantities: 3 + 1 |
| How many saved lines? | 1 | One parked entry, regardless of its quantity |
| How many active plus saved lines? | 3 | Count entries in both groups |

Say it another way: a line is a row on a shopping list; quantity says how many of that row's item you want. Counting rows and summing quantities answer different questions.

Read [CartResponse](../../src/Agora.Api/Contracts/CartContracts.cs). Its Items and SavedItems are response lists, while Cart.Items in the domain holds both active and saved entries. The mapper separates them. That is why a response's active line count should use its already-separated Items list rather than rediscovering the domain filtering rule.

## Stored value or computed getter?

Variant weight is a physical property that cannot be recovered from name or price. It belongs in persisted variant data. An in-stock flag can be recovered from available quantity. A page's next-page hint can be recovered from page number and total pages. Those do not need new database columns.

Before adding a field, ask: **If I already know all the other fields, can I calculate this answer exactly?** If yes, storing it may create a second value that can drift out of sync. This is a useful default, not a ban on deliberately cached or historical values in later modules.

Read [PagedResult](../../src/Agora.Api/Contracts/PagedResult.cs) and [InventoryResponse](../../src/Agora.Api/Contracts/InventoryContracts.cs). Find the facts each already carries. Draw arrows from those facts to the flags a client wants to display.

## Predict page navigation

Five matches with page size two means three pages: 2, 2, and 1 item. Previous means requested page > 1; next means requested page < total pages.

| Requested page | Previous | Next |
| --- | --- | --- |
| 1 | false | true |
| 2 | true | true |
| 3 | true | false |
| 8 | true | false |

The last row is intentional. A navigation hint does not promise the previous page contains data. An empty first page has both flags false. Explain why before implementing the getters.

## Follow a new input through three layers

For variant weight, follow `CreateVariantRequest -> ProductsController.Create -> ProductVariant -> VariantResponse`. A response field alone cannot save input; an input field alone cannot expose it on the next read. A database column already exists for WeightGrams, so this feature connects existing persisted data to the API rather than requiring a migration.

Read [ProductContracts](../../src/Agora.Api/Contracts/ProductContracts.cs), [ProductsController](../../src/Agora.Api/Controllers/ProductsController.cs), and [ProductVariant](../../src/Agora.Domain/Entities/ProductVariant.cs). Put one breakpoint at request receipt and another after mapping. Predict what omitted weight should become, then compare with a deliberately supplied weight and an invalid negative weight.

## Why ordering needs a second key

Two images can have the same SortOrder. Two variants can have the same display name. A primary image or first variant must still be predictable. Sort by the meaningful business field, then by a stable unique ID where appropriate. Do not use the accidental order rows were inserted or returned by SQLite.

A useful counterexample has two tied records inserted in the reverse of their intended ID order. If a test only inserts already-sorted data, a broken implementation can look correct.

## Exercises

1. A cart has active quantities 2, 2, 7 and saved quantities 9, 1. Predict active units, active lines, and saved lines.
2. Inventory has on-hand 7 and reserved 7. Is it in stock? What changes when one reservation is released?
3. Why would a stored HasNextPage column be the wrong place to represent page navigation?
4. An HTTP response includes a field, but constructing the C# DTO in a unit test does not check its JSON name. What additional test observes that boundary?
5. Why should an empty image list produce a null primary image rather than an indexing exception?

## Answer explanations

1. Active units = 11; active lines = 3; saved lines = 2. Quantities do not change the number of entries.
2. Available = 7 - 7 = 0, so false. Releasing one gives available 1, so true; on-hand need not change.
3. A page is a request-specific view of a filtered result, not a stored entity. Different filters/page sizes produce different answers from the same rows.
4. An integration request that reads the JSON property. C# getter tests prove arithmetic; HTTP tests prove the exposed contract.
5. No image is a normal state. Null communicates absence while preserving the rest of the product response.

## Review checkpoint

Explain one value as a user question, one getter as an equation, one input as a mapping path, and one test as a counterexample. The implementation journal will record the actual checks once the module's code is verified.
