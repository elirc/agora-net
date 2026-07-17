# agora-net

An e-commerce platform backend built with C# / .NET 10 and ASP.NET Core Web API — catalog, inventory, guest carts, checkout with stock reservation, order lifecycle, discounts, and a fake payment gateway behind a clean abstraction.

## Solution layout

| Project | Purpose |
| --- | --- |
| `src/Agora.Api` | ASP.NET Core Web API host: controllers, contracts (DTOs), exception filter |
| `src/Agora.Domain` | Entities, `Money` value object, domain services/abstractions, no dependencies |
| `src/Agora.Infrastructure` | EF Core (SQLite) persistence, migrations, seeder, checkout/order services, fake gateway |
| `tests/Agora.Tests` | xUnit: domain unit tests + `WebApplicationFactory` integration tests over in-memory SQLite |

## Getting started

```bash
dotnet build
dotnet test
dotnet run --project src/Agora.Api
```

On startup the API applies EF Core migrations and (in Development) seeds a demo catalog — 3 categories, 8 products, 14 variants with stock, and 3 discount codes (`WELCOME10`, `SAVE5`, and the intentionally expired `EXPIRED10`) — into `agora.db`.

## API surface

### Catalog
| Endpoint | Description |
| --- | --- |
| `GET /health` | Liveness probe |
| `GET/POST /api/categories`, `GET/PUT/DELETE /api/categories/{id}` | Category CRUD (delete blocked while in use) |
| `GET /api/products` | Search/filter/paginate — see below |
| `GET /api/products/{id}`, `GET /api/products/by-slug/{slug}` | Product with variants + images |
| `POST /api/products` | Create with variants (SKU, price, options) and image URLs |
| `PUT/DELETE /api/products/{id}` | Update core fields / delete |

`GET /api/products` query parameters: `search` (name/description), `categoryId`, `categorySlug`, `minPrice`/`maxPrice` (matches any variant), `isActive`, `sort` (`name`, `name_desc`, `price`, `price_desc`, `newest`, `oldest`), `page`, `pageSize` (≤ 100). Returns `{ items, page, pageSize, totalCount, totalPages }`.

### Inventory & carts
| Endpoint | Description |
| --- | --- |
| `GET /api/inventory/{sku}` | On-hand / reserved / available |
| `PUT /api/inventory/{sku}` | Set absolute stock (rejected below reserved) |
| `POST /api/carts` | Mint a guest cart (opaque token) |
| `GET /api/carts/{token}` | Cart with live pricing + subtotal |
| `POST /api/carts/{token}/items` | Add item (merges duplicate variants; quantity 1–99; 409 beyond available stock) |
| `PUT /api/carts/{token}/items/{itemId}` | Change quantity (0 removes) |
| `DELETE /api/carts/{token}/items/{itemId}` / `DELETE /api/carts/{token}` | Remove line / clear cart |

### Checkout, orders & discounts
| Endpoint | Description |
| --- | --- |
| `POST /api/checkout` | Cart → paid order (see pipeline below) |
| `GET /api/orders/{number}` | Order with items and totals |
| `POST /api/orders/{number}/fulfill` | Paid → Fulfilled |
| `POST /api/orders/{number}/cancel` | Pending/Paid → Cancelled (paid orders refunded + restocked) |
| `POST /api/orders/{number}/refund` | Paid/Fulfilled → Refunded (restocked) |
| `GET/POST /api/discounts`, `GET/PUT/DELETE /api/discounts/{code}` | Discount CRUD (percent or fixed, expiry, usage limits) |

### Checkout pipeline

```
validate cart + discount   (side-effect free; 400/404/422)
        │
reserve stock per line     (InsufficientStock -> 409, nothing persisted)
        │
persist pending order + reservations
        │
charge IPaymentGateway ────── declined ──> release reservations, drop order,
        │                                  keep cart, respond 402
commit reservations, mark Paid, register discount use, clear cart -> 201
```

Totals: `total = (subtotal − discount) + tax + shipping`. Tax and shipping are pluggable calculators (`ITaxCalculator`, `IShippingCalculator`) with flat-rate defaults configured in `appsettings.json` (`Checkout` section: 8% tax, $5.99 shipping, free at $50). The `FakePaymentGateway` approves everything except tokens equal to `tok_fail` or prefixed `fail`.

Order lifecycle: `Pending → Paid → Fulfilled`, with `Cancelled` reachable from Pending/Paid and `Refunded` from Paid/Fulfilled; illegal transitions return 409.

## Error contract

All failures are RFC 7807 `application/problem+json`: model validation → 400 with `errors`, domain rule violations → 400, missing resources → 404, payment declines → 402, stock/state conflicts → 409, semantic rejections (unknown/expired discount, bad references) → 422.

## Tech notes

- **Money** is a `decimal` amount + ISO 4217 currency code value object with banker's-safe rounding (away-from-zero, 2 dp) and currency-mismatch guards.
- **SQLite gotcha**: SQLite cannot order/compare `DateTimeOffset` (or `decimal`) natively. The DbContext registers global value converters — `DateTimeOffset` → UTC ticks (`long`) and `decimal` → cents (`long`) — so `ORDER BY`/range queries translate correctly.
- **Stock reservation** is a first-class inventory concept: `Reserve` holds units without deducting, `CommitReservation` converts holds to deductions on payment success, `ReleaseReservation` frees them on failure, and cancellations/refunds `Restock`.
- Variant options (e.g. `{"Color":"Red","Size":"M"}`) are persisted as JSON.
- Integration tests boot the whole API against a private in-memory SQLite connection per test class.

## Development

```bash
dotnet test                                   # 160 tests, unit + integration
dotnet tool run dotnet-ef -- migrations add <Name> \
  --project src/Agora.Infrastructure --startup-project src/Agora.Api
```

Built as six sprint PRs: scaffold → domain & persistence → catalog API → inventory & carts → checkout/orders/payments/discounts → hardening.
