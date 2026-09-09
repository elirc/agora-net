# agora-net

**New to this codebase?** [AstraDocs](astradocs/README.md) provides a gradual
onboarding with repeated explanations, stories, diagrams, annotated code,
hands-on steps, recall cards, and an answer key. Begin with one small concept.

**Learn backend engineering by improving a real C# codebase.** Start with the
[junior-to-senior learning path](docs/learning/README.md): hands-on lessons,
debugging exercises, a worked bug fix, and progressively harder feature tickets.
The emphasis is on explaining and safely changing code, then developing design
and operational judgment.

- **New to backend work?** Follow [your first hour](docs/learning/01-first-hour.md).
- **Ready to build?** Reconstruct the [catalog fix](docs/learning/04-catalog-worked-example.md), then choose a [practice ticket](docs/learning/feature-backlog.md).
- **Track your growth:** use the [roadmap and rubric](docs/learning/roadmap.md) and [progress journal](docs/learning/progress-journal.md).
- **Understand the limits:** read the [review findings](docs/learning/review-findings.md), including open order-authorization and payment-recovery work.

An e-commerce platform backend built with C# / .NET 10 and ASP.NET Core Web API — catalog, inventory, guest and account carts, checkout with stock reservation, zone-based tax, shipping methods, gift cards, discounts, reviews, wishlists, returns (RMA), partial fulfillment, webhooks, and admin reporting, all behind JWT auth with an admin role.

## Solution layout

| Project | Purpose |
| --- | --- |
| `src/Agora.Api` | ASP.NET Core Web API host: controllers, contracts (DTOs), auth, filters, health, rate limiting |
| `src/Agora.Domain` | Entities, `Money` value object, domain services/abstractions, no dependencies |
| `src/Agora.Infrastructure` | EF Core (SQLite) persistence, migrations, seeder, checkout/order/return/fulfillment/tax/webhook services, fake gateway + webhook sender |
| `tests/Agora.Tests` | xUnit: domain unit tests + `WebApplicationFactory` integration tests over in-memory SQLite |

## Documentation

| Doc | Contents |
| --- | --- |
| [astradocs/](astradocs/README.md) | Gradual junior onboarding: the same code paths explained through stories, diagrams, code, exercises, and answers |
| [Implementation bootcamp](astradocs/bootcamp/README.md) | Live 75-story tracker, build journal, worked feature lessons, exercises with answers, and a personal learning log |
| [docs/learning/](docs/learning/README.md) | Eleven lessons, roadmap, eight feature exercises, review findings, glossary, mentor prompts, and a progress journal |
| [docs/getting-started.md](docs/getting-started.md) | Run, seed data, and a verified curl walkthrough: browse → cart → checkout with discount + gift card → fulfill → RMA refund |
| [docs/architecture.md](docs/architecture.md) | Layering, `Money` + the cents/millionths converters, the checkout pipeline, order-status derivation, tender ordering, webhook delivery |
| [docs/api-reference.md](docs/api-reference.md) | Every endpoint: method, route, auth, request/response shape, error codes |
| [docs/adr/](docs/adr/) | Nine decision records — decimal-as-cents, Money, reserve→charge→commit, tender ordering, derived status, optimistic concurrency, HMAC webhooks, guest tokens |
| [docs/testing.md](docs/testing.md) | Test taxonomy, harness design, how to run |

## Getting started

```bash
dotnet build
dotnet test
dotnet run --project src/Agora.Api
```

On startup the API applies EF Core migrations and (in Development) seeds a demo catalog — 3 categories, 8 products, 14 weighted variants with stock, 3 discount codes (`WELCOME10`, `SAVE5`, expired `EXPIRED10`), 3 shipping methods (`standard`/`express`/`freight`), US/GB tax zones with `standard`/`reduced`/`zero` tax categories, and an admin account `admin@agora.dev` / `AdminPass123!` — into `agora.db`.

## API surface

### Auth & accounts
| Endpoint | Description |
| --- | --- |
| `POST /api/auth/register`, `POST /api/auth/login` | Customer registration/login; returns a JWT (sub/email/role claims) |
| `GET /api/auth/me` | Authenticated profile |
| `GET /api/me/orders`, `GET /api/me/returns` | Paged order/RMA history |
| `GET/POST/PUT/DELETE /api/me/addresses`, `POST /api/me/addresses/{id}/default` | Address book (first address auto-defaults) |
| `GET/POST /api/me/wishlists`, `GET /api/me/wishlists/default`, item add/remove, `.../move-to-cart` | Default + named wishlists, back-in-stock flags |

Catalog, inventory, discount, shipping-method, tax, gift-card, fulfillment, webhook, and report **mutations require the Admin role**; reads stay public. 401/403 surface as ProblemDetails.

### Catalog
| Endpoint | Description |
| --- | --- |
| `GET /health`, `GET /health/ready` | Liveness / readiness (DB probe) |
| `GET/POST /api/categories`, `GET/PUT/DELETE /api/categories/{id}` | Category CRUD (paged list; delete blocked while in use) |
| `GET /api/products` | Search/filter/sort/paginate; carries `averageRating`/`reviewCount` |
| `GET /api/products/{id}`, `GET /api/products/by-slug/{slug}` | Product with variants + images + rating aggregate |
| `POST/PUT/DELETE /api/products/{id}` | Admin CRUD; optional `taxCategoryCode` |
| `GET /api/products/{id}/reviews`, `POST` | Approved reviews (paged) / submit verified-purchase review |
| `PUT/DELETE /api/reviews/{id}`, `POST .../helpful` | Edit (back to moderation), delete, helpful votes |
| `GET /api/reviews?status=`, `POST /api/reviews/{id}/approve|reject` | Admin moderation queue |

`GET /api/products` query parameters: `search`, `categoryId`, `categorySlug`, `minPrice`/`maxPrice`, `currency`, `inStock`, `isActive`, `sort` (`name`, `name_desc`, `price`, `price_desc`, `newest`, `oldest`), `page`, `pageSize` (≤ 100). Price, currency, and availability must match the same variant. See the [worked example](docs/learning/04-catalog-worked-example.md) for semantics and tests.

### Inventory & carts
| Endpoint | Description |
| --- | --- |
| `GET /api/inventory/{sku}` / `PUT` (admin) | On-hand / reserved / available; set absolute stock |
| `POST /api/carts`, `GET /api/carts/{token}` | Mint cart (attached to account when signed in) / read with live pricing |
| `POST /api/carts/{token}/claim` | Attach a guest cart to the signed-in account |
| `POST/PUT/DELETE /api/carts/{token}/items...` | Add (merge, 1–99, stock-checked), update, remove, clear |
| `POST /api/carts/{token}/items/{id}/save-for-later` / `.../activate` | Saved-for-later lines (out of totals/checkout) |

### Checkout, orders, shipping & tender
| Endpoint | Description |
| --- | --- |
| `POST /api/checkout` | Cart → paid order (rate limited; see pipeline below) |
| `GET /api/shipping-methods` (+ admin CRUD) | Flat or weight-based rates, free thresholds, delivery estimates |
| `GET /api/tax-zones`, `GET /api/tax-categories` (+ admin CRUD) | Country/region tax zones with per-category rates |
| `POST /api/gift-cards` (admin), `GET /api/gift-cards/{code}` | Issue / balance check; partial tender at checkout |
| `GET /api/orders/{number}` | Order with items, totals, shipping method, delivery estimate, tender |
| `POST /api/orders/{number}/fulfill` (admin) | Ship everything remaining in one fulfillment |
| `POST /api/orders/{number}/fulfillments` (admin), `GET` | Partial shipments with line quantities, carrier, tracking |
| `POST /api/orders/{number}/cancel` | Pending/Paid → Cancelled (blocked once anything shipped; refunds + restocks) |
| `POST /api/orders/{number}/refund` | Full refund (each tender returns to its source; blocked after partial returns) |
| `POST /api/orders/{number}/returns` | Open an RMA for fulfilled order lines (owner or guest email) |
| `GET /api/returns/{number}`, `POST .../cancel` | RMA status tracking / requester cancellation |
| `GET /api/returns?status=`, `POST /api/returns/{n}/approve|reject` | Admin queue; approval refunds partially + restocks |
| `GET/POST/PUT/DELETE /api/discounts...` | Discount CRUD (paged list; percent or fixed, expiry, usage limits) |

### Admin reporting & webhooks
| Endpoint | Description |
| --- | --- |
| `GET /api/admin/reports/sales?from&to&interval=day|week|month` | Orders/items/revenue bucketed by payment date |
| `GET /api/admin/reports/top-products` | Best sellers by revenue |
| `GET /api/admin/reports/low-stock?threshold=` | Variants at/below available threshold (paged) |
| `GET /api/admin/reports/discount-usage` | Redemptions, orders, discounted totals per code |
| `GET/POST/PUT/DELETE /api/webhooks` | Subscriptions for `order.created/paid/fulfilled/refunded` (secret is write-only) |
| `GET /api/webhooks/{id}/deliveries`, `POST /api/webhooks/deliveries/{id}/retry` | Signed delivery log + retry (max 5 attempts) |

### Checkout pipeline

```
validate cart, address (inline or saved), shipping method, discount, gift card
        │                     (side-effect free; 400/404/422)
reserve stock per line        (InsufficientStock -> 409, nothing persisted)
        │
persist pending order + reservations
        │
charge IPaymentGateway for (total - gift card tender) ── declined ──> release
        │                                     reservations, drop order, keep cart,
        │                                     gift card untouched, respond 402
redeem gift card, commit reservations, mark Paid,
register discount use, clear active cart lines -> 201
        │
dispatch order.created + order.paid webhooks
```

Totals follow **discounts → tax → gift card tender**: `total = (subtotal − discount) + zone tax + shipping`, then any gift card is applied to the final total and only the remainder is charged. Tax comes from the shipping address's zone (region-specific beats country-wide; no zone = no tax) at the rate for each product's tax category. Shipping methods price flat or by weight (`ProductVariant.WeightGrams`) with optional free thresholds and min/max-day delivery estimates.

Order lifecycle: `Pending → Paid → PartiallyFulfilled → Fulfilled` (status derives from shipment coverage), with `Cancelled` reachable from Pending/Paid only (never after a shipment) and `Refunded` from Paid/PartiallyFulfilled/Fulfilled; illegal transitions return 409. Returns (RMA) require full fulfillment, refund partially through the gateway with discount/tax-adjusted amounts, and restock on approval.

The `FakePaymentGateway` approves everything except tokens equal to `tok_fail` or prefixed `fail`; the `FakeWebhookSender` rejects URLs containing `fail`. Webhook payloads are signed with hex HMAC-SHA256 (`X-Agora-Signature` material) using the subscription secret.

## Error contract

All failures are RFC 7807 `application/problem+json`: model validation → 400 with `errors`, domain rule violations → 400, missing resources → 404, auth → 401/403, payment declines → 402, stock/state/concurrency conflicts → 409, semantic rejections (unknown discount/gift card/shipping method/tax category, over-quantity returns or shipments) → 422, checkout rate limit → 429.

## Operational foundations

These facilities support learning and local operation. They do not establish
production readiness; the [review findings](docs/learning/review-findings.md)
describe open authorization, payment recovery, and durable delivery concerns.

- **Request logging** via built-in HTTP logging (method, path, status, duration).
- **Health checks**: `/health` liveness, `/health/ready` readiness with a database probe.
- **Pagination**: many list endpoints return `{ items, page, pageSize, totalCount, totalPages }` with `pageSize ≤ 100`. Some sublists and reports remain unpaged; ownership alone does not bound response size or query work.
- **Optimistic concurrency**: `InventoryItem`, `Cart` and `GiftCard` carry a `Version` token bumped on every mutation; competing writes fail with 409 (`DbUpdateConcurrencyException` mapped to ProblemDetails).
- **Rate limiting**: checkout uses a per-client fixed window (`RateLimiting:Checkout`, default 10/min) returning 429.

## Tech notes

- **Money** is a `decimal` amount + ISO 4217 currency code value object (away-from-zero, 2 dp, non-negative).
- **SQLite gotcha**: `DateTimeOffset` → UTC ticks and `decimal` → integer cents via global converters so ordering/range queries translate; fractional tax rates use a dedicated millionths converter (cents would round 9.5% to 10%).
- **Stock reservation**: `Reserve` → `CommitReservation`/`ReleaseReservation` around payment, `Restock` on cancels/refunds/returns.
- **Auth**: PBKDF2-SHA256 password hashes (self-describing format), HMAC-SHA256 JWTs, `Admin` role guards mutations, guest flows (carts, checkout, returns by order email) keep working without accounts.
- Variant options are persisted as JSON; webhook event lists as CSV.
- Integration tests boot the whole API against a private in-memory SQLite connection per test class (rate limits relaxed via `appsettings.Testing.json`).

## Development

```bash
dotnet test                                   # unit + integration suites
dotnet tool run dotnet-ef -- migrations add <Name> \
  --project src/Agora.Infrastructure --startup-project src/Agora.Api
```

Built as fifteen sprint PRs: v1 (1–6) scaffold → domain → catalog → inventory & carts → checkout/orders/payments/discounts → hardening; v2 (7–15) auth & accounts → addresses & shipping → reviews → wishlists & saved carts → returns → tax zones & gift cards → fulfillment → reporting & webhooks → production readiness.
