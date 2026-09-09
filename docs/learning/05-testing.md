# Tests that make your change safer

**Outcome:** choose a test that distinguishes a correct implementation from a plausible wrong one. Read [the harness reference](../testing.md) after this lesson.

## Choose the boundary at risk

| Risk | Useful test | Why |
| --- | --- | --- |
| Inventory accepts a negative reservation | Domain unit test | No HTTP or database is needed to exercise the rule |
| LINQ cannot translate or matches the wrong variants | HTTP integration test over SQLite | A mocked list cannot expose provider translation behavior |
| A stale inventory snapshot overwrites new stock | Two-context persistence test | One object cannot simulate competing database snapshots |
| A customer can read another customer's record | Two-identity HTTP test | Being authenticated does not establish ownership |
| Gateway accepted a request but response was lost | Controlled gateway failure test | A simple decline fake does not model an ambiguous outcome |

The existing `Unit` folder also includes persistence and infrastructure tests. Test behavior and cost matter more than folder labels.

## Arrange evidence, not incidental seed state

The new catalog tests create a unique category per scenario and filter requests to it. That prevents another test's catalog changes from altering the result. A class fixture shares its database across methods; it does not reset before every test. Prefer explicit prices, currencies, stock, and timestamps for boundary tests.

For a stock test, asserting that every returned item is available can pass when the result is empty. Also assert which item must be present and the expected count. For an error test, assert the status and response type before inspecting fields. Avoid exact prose comparisons unless the prose is contractual.

## Practice red, green, review

Write a test that fails because of the intended missing behavior. A compile error or broken restore does not prove a business regression. Implement the smallest fix. Run the focused suite, then the full suite before delivering. Read the diff for unintended changes even when tests pass.

```powershell
dotnet test --filter FullyQualifiedName~InventoryItemTests
dotnet test --filter FullyQualifiedName~CatalogSearchApiTests
dotnet test Agora.slnx
```

**Exercise:** design tests for `minPrice=0&maxPrice=0`: a free variant, a paid-only product, and a product containing both. Decide whether the response contains only matched variants or all variants before writing assertions.

**Second exercise:** describe an implementation that would pass your test while still violating the requirement. Strengthen the test to distinguish it. This habit is more useful than chasing a coverage percentage.

**Checkpoint:** submit one regression with a clear counterexample and explain why its chosen boundary is necessary. **Stretch:** use a synchronization gate to coordinate two writers; avoid timing-based `Task.Delay` guesses for concurrency tests.
