# 11: Read tests as little stories

[Home](README.md) · Previous: [Debugging](10-debugging.md) · Next: [First change](12-first-change.md)

**Small outcome:** read one test and say what it proves and what it does not prove.

## A test with no server and no database

Open [CartTests](../tests/Agora.Tests/Unit/CartTests.cs) and find `AddItem_ExistingVariant_MergesQuantity`:

```csharp
_cart.AddItem(_variantId, 2);
_cart.AddItem(_variantId, 3);

Assert.Single(_cart.Items);
Assert.Equal(5, _cart.Items[0].Quantity);
```

The class supplies a fresh cart and variant ID for each test instance. Read the snippet as a story: add two, add three of the same variant, expect one line totaling five. `Assert.Single` checks the number of lines. `Assert.Equal` checks the quantity.

Run only this test from the repository root:

```powershell
dotnet test --filter FullyQualifiedName~CartTests.AddItem_ExistingVariant_MergesQuantity
```

This verifies the cart object's rule. It does not demonstrate HTTP routing, JSON validation, inventory availability, or database persistence. The domain method does not need a real product row to merge identifiers in memory.

## A test through HTTP and SQLite

Open [CartsApiTests](../tests/Agora.Tests/Integration/CartsApiTests.cs) and find `AddItem_AddsLineWithPricing`. It creates a cart through HTTP, obtains a seeded variant ID, posts two units, and checks the HTTP status and response data.

Its [AgoraApiFactory](../tests/Agora.Tests/Integration/AgoraApiFactory.cs) boots the application with the `Testing` environment and a private in-memory SQLite database. The factory creates the schema and seeds it. The test does not call the manually running server on port 5077.

Run:

```powershell
dotnet test --filter FullyQualifiedName~CartsApiTests.AddItem_AddsLineWithPricing
```

The factory is shared by tests in a class using `IClassFixture`. Its database is not reset before every method. Separate factory instances have separate databases. Be careful about assumptions when tests mutate shared fixture state.

## The same distinction as a table

| Question | Cart unit test | Cart API integration test |
| --- | --- | --- |
| Calls the domain method? | Directly | Through the endpoint's handling |
| Exercises HTTP and binding? | No | Yes |
| Uses real SQLite? | No | Yes |
| Requires terminal A's development server? | No | No |
| Proves a real external payment works? | No | No |

## Reading a failure

If expected quantity is five and actual is three, inspect whether adding replaced rather than merged the quantity. If restore fails, the test did not get that far. Distinguish environment failures from failed assertions.

If a filtered run reports no matching tests, it is not a passing result for your scenario. Check the name or use `dotnet test --list-tests`.

**Q16:** Which test boundary can expose a SQLite query-translation problem: an object-only test or the HTTP/SQLite integration test?

**Stop:** explain one assertion and one thing the test cannot prove. [Answer](14-answer-key.md).
