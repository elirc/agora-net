# Testing

Learning to choose tests? Start with [tests that make your change safer](learning/05-testing.md).
The [catalog worked example](learning/04-catalog-worked-example.md) connects a real defect
to HTTP/SQLite regressions in `CatalogSearchApiTests`.

```bash
dotnet test                                    # whole suite
dotnet test --filter FullyQualifiedName~Unit   # domain only (fast, no HTTP)
dotnet test --filter FullyQualifiedName~Integration
dotnet test --filter FullyQualifiedName~TotalsPipelineTests
```

One test project — `tests/Agora.Tests` (xUnit) — referencing `Agora.Api`,
which transitively pulls in `Agora.Domain` and `Agora.Infrastructure`. There
are no mocking libraries: the two ports that would otherwise reach the outside
world (`IPaymentGateway`, `IWebhookSender`) already have deterministic fakes
in `Agora.Infrastructure`, and everything else runs for real.

## Taxonomy

### `Unit/` — domain rules, no I/O

Plain object tests over `Agora.Domain`. They construct entities directly and
assert on guarded methods and value semantics.

| Area | Files |
| --- | --- |
| Money & rounding | `MoneyTests` |
| Stock transitions | `InventoryItemTests` |
| Cart rules & saved-for-later | `CartTests`, `CartSavedForLaterTests` |
| Order state machine | `OrderLifecycleTests`, `OrderStateMatrixTests`, `OrderFulfillmentStateTests` |
| RMA / review state | `ReturnRequestTests`, `ReviewTests` |
| Pricing & redeemability | `DiscountCodeTests`, `GiftCardTests`, `ShippingMethodTests`, `RedeemabilityBoundaryTests` |
| Infrastructure bits | `Pbkdf2PasswordHasherTests`, `FakePaymentGatewayTests`, `WebhookTests`, `SlugGeneratorTests`, `PersistenceTests` |

Two of these carry more weight than their size suggests:

- **`OrderStateMatrixTests`** enumerates the *entire* 6-state × 5-action
  transition matrix (30 pairs) from a declared `LegalFrom` table: every pair is
  pinned as either legal or an `InvalidOrderStateException`. A new order status
  or action fails the suite until the table is updated deliberately.
- **`PersistenceTests`** pins the SQLite mappings against a real SQLite file:
  that `DateTimeOffset` round-trips through UTC ticks *and orders
  chronologically*, that decimal prices round-trip and answer range queries,
  that variant options survive the JSON converter, that the seeder is
  idempotent, and that a duplicate SKU trips the unique index. These are the
  claims ADR-0001 rests on, so they are pinned rather than assumed.

### `Integration/` — the real API over real SQL

Every integration class boots the whole app through
`WebApplicationFactory<Program>` and talks to it over `HttpClient` — real
routing, model binding, auth, filters, EF Core and SQL. Named by surface
(`CartsApiTests`, `CheckoutApiTests`, `ReturnsApiTests`, …), plus these
cross-cutting suites:

| Suite | What it pins |
| --- | --- |
| `TotalsPipelineTests` | the discounts → tax → gift-card identity, cent-for-cent, across discount/tender combinations (threshold boundaries, 100%-off, rounding conservation across lines) |
| `RefundTenderTests` | each tender returns to its source on cancels, refunds and RMAs |
| `OrderStateApiTests` | the state machine as seen through HTTP (409s on illegal transitions) |
| `ConcurrencyEdgeTests` | interleaved writers on stock, gift-card balances and webhook redelivery must fail loudly, never silently lose an update |
| `StockReservationEdgeTests` | reserve → commit/release around payment outcomes |
| `AuthzMatrixTests` | anonymous / customer / admin against guarded routes |
| `BoundaryValidationTests`, `ApiHardeningTests` | pagination caps, quantity caps, negative and malformed input |
| `ProductionReadinessTests` | health probes, rate limiting, pagination audit |

## Harness design

`AgoraApiFactory` is the whole harness (~50 lines):

```csharp
public sealed class AgoraApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    // UseEnvironment("Testing") -> Program skips Migrate() and the seeder
    // swap the registered DbContext for one on _connection
    // _connection.Open(); EnsureCreated(); AgoraDbSeeder.SeedAsync(db)
}
```

Four decisions worth knowing:

1. **In-memory SQLite, not the EF in-memory provider.** Real SQL, real
   constraints, real value converters, real concurrency tokens. The tests use the same provider as the local API.
2. **One database per factory, held open by one connection.** An in-memory
   SQLite database lives exactly as long as its connection, so the field on the
   factory *is* the database's lifetime. Classes take
   `IClassFixture<AgoraApiFactory>`, so each test class gets a private database
   and classes can run in parallel without interfering.
3. **Seeded, not empty.** Every class starts from the dev catalog, so tests read
   like the walkthrough (`CartWith("TEE-BLK-S", 2) // 2 x 19.99, stock 40`)
   instead of building fixtures by hand. Tests that need a specific stock level
   set it explicitly via the admin API first.
4. **`Program` is `public partial`** (bottom of `Program.cs`) purely so
   `WebApplicationFactory<Program>` can find the entry point.

Helpers:

- `factory.WithDbAsync(db => …)` — a fresh `DbContext` scope for arranging or
  asserting straight against the database.
- `TestAuth` — `client.AuthenticateAsAdminAsync()` (logs in the seeded admin),
  `TestAuth.RegisterAsync(client, email)`, `client.UseBearer(token)`.

The `Testing` environment also loads `appsettings.Testing.json`, which raises
the checkout rate limit to 100 000/window so unrelated tests never trip 429 —
`ProductionReadinessTests` verifies the limiter itself with its own policy.

## Conventions

- Name tests `Subject_Condition_ExpectedOutcome`
  (`ReservingTheLastUnit_FromTwoSnapshots_SecondWriterConflicts`).
- Assert the **status code** first, then the body — a 422 that should have been
  a 409 is a contract break even when the message reads fine.
- Prefer a `[Theory]` with a declared table over near-duplicate `[Fact]`s when
  covering a matrix (see `OrderStateMatrixTests`).
- Money assertions compare decimals exactly. Never assert on a rounded string.
