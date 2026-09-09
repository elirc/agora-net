# Architecture

For a guided introduction, use the [code tour](learning/02-code-tour.md).
Product listing now separates HTTP input validation (`ProductSearchRequest`),
deferred SQL composition (`ProductCatalogQuery`), and controller execution/mapping.
[ADR-0009](adr/0009-catalog-query-contract.md) explains this choice and its limits.

agora-net is a three-project ASP.NET Core Web API backed by EF Core on SQLite,
with a fourth project of xUnit tests that boot the whole API in memory.

```
┌─────────────────────────────────────────────────────────────┐
│ Agora.Api            controllers, contracts (DTOs), auth,   │
│                      filters, health, rate limiting         │
├─────────────────────────────────────────────────────────────┤
│ Agora.Infrastructure EF Core (SQLite) persistence,          │
│                      migrations, seeder, application        │
│                      services, fake gateway/webhook sender  │
├─────────────────────────────────────────────────────────────┤
│ Agora.Domain         entities, Money value object, domain   │
│                      service abstractions — no dependencies │
└─────────────────────────────────────────────────────────────┘
              Agora.Tests exercises all three via
              WebApplicationFactory + in-memory SQLite
```

## Layering

- **`Agora.Domain`** has no project or package dependencies. It holds the
  entities (`Order`, `Cart`, `InventoryItem`, `GiftCard`, …), the `Money`
  value object, domain exceptions, and the abstractions the application layer
  needs (`IPaymentGateway`, `IWebhookSender`, `IPasswordHasher`). The order
  and RMA state machines live on the entities themselves as guarded methods —
  `Order.MarkPaid` throws `InvalidOrderStateException` rather than letting a
  caller set an illegal status, and `ReturnRequest.Approve/Reject/Cancel` all
  require `Requested`. (Review moderation is deliberately *not* guarded:
  `Review.Approve`/`Reject` are idempotent from any status, so an admin can
  re-approve a rejected review without an intermediate transition.)
- **`Agora.Infrastructure`** owns `AgoraDbContext` (mappings, value
  converters, concurrency tokens), migrations, the development seeder, and
  the orchestrating services: `CheckoutService`, `OrderService`,
  `ReturnService`, `FulfillmentService`, `TaxService`, `WebhookService`, plus
  the deterministic fakes (`FakePaymentGateway`, `FakeWebhookSender`).
- **`Agora.Api`** is the HTTP shell: controllers bind request DTOs
  ("contracts"), call a service or the DbContext, and map results to response
  DTOs. A single `DomainExceptionFilter` translates every domain exception
  into an RFC 7807 ProblemDetails response, so controllers never catch
  domain errors themselves.

Request DTOs carry DataAnnotations validation (400 on failure); semantic
failures (unknown discount code, over-quantity return) surface as typed
domain exceptions mapped to 404/409/422 — see the [error contract](#error-contract).

## Money and the SQLite value converters

`Money` (`Agora.Domain.Common.Money`) is an immutable record of a `decimal`
amount plus a 3-letter ISO currency code:

- amounts are rounded to 2 decimal places, `MidpointRounding.AwayFromZero`;
- amounts can never be negative — `Subtract` clamps at zero, which is exactly
  the behavior wanted when a discount exceeds the subtotal;
- `Add`/`Subtract` throw on currency mismatch.

SQLite has no native `decimal` or `DateTimeOffset` affinity — both would be
stored as TEXT, which breaks ordering and range queries. `SqliteValueConverters.cs`
therefore stores both as ordered-comparable `long` columns:

| Converter | Applies to | Stored as | Why |
| --- | --- | --- | --- |
| `DateTimeOffsetToUtcTicksConverter` | every `DateTimeOffset` (global convention) | UTC ticks | `ORDER BY` matches chronological order exactly |
| `DecimalToCentsConverter` | every `decimal` (global convention) | integer cents (× 100, away-from-zero) | exact money arithmetic, sortable, no TEXT comparisons |
| `DecimalRateToMillionthsConverter` | `TaxZone.DefaultRate`, `TaxZoneRate.Rate` (per-property override) | integer millionths (× 1 000 000) | **cents would destroy fractional rates**: a 9.5% rate (0.095) rounds to 0.10 at cent precision. Rates need sub-cent resolution, so they get a dedicated converter with 6 decimal places |

The cents/millionths split is the key design point: one global convention for
money-like values, and an explicit opt-out for the handful of columns that
are *rates*, not amounts.

## Checkout pipeline

`CheckoutService.CheckoutAsync` turns a cart into a paid order in a strict
order chosen so that failures are side-effect free until stock is reserved:

```
1. load cart, reject empty carts and deactivated products         400
2. resolve shipping address (inline or saved) + shipping method   400/404/422
3. validate discount code and gift card (redeemable, currency)    422
   ── everything above is side-effect free ──
4. reserve stock for every line (InsufficientStock -> 409)
5. compute totals: discounts -> tax -> shipping
6. persist the Pending order together with the reservations
7. charge IPaymentGateway for (total - gift card tender)
   ├─ declined: release reservations, delete the pending order,
   │            keep the cart, gift card untouched          -> 402
   └─ approved (or nothing to charge):
8. redeem gift card, mark order Paid, commit reservations,
   register discount use, remove the cart's active lines    -> 201
9. dispatch order.created + order.paid webhooks
```

Two zero-charge shapes skip the gateway entirely: a gift card covering the
whole total (`PaymentTransactionId = "gift_<code>"`) and a total discounted
to zero (`"free_<order#>"`).

### Totals: discounts → tax → gift card tender

```
discount           = code.CalculateDiscount(subtotal)        (percent or fixed, clamped)
discountedSubtotal = subtotal - discount
tax                = Σ line.discountedAmount × zoneRate(line.taxCategory)
shipping           = method.CalculateCharge(discountedSubtotal, totalWeight)
total              = discountedSubtotal + tax + shipping
giftCardTender     = min(card.Balance, total)
gatewayCharge      = total - giftCardTender
```

- The discount is prorated across lines by rate (`discount / subtotal`), so
  per-category tax is computed on each line's *discounted* amount.
- Tax zones resolve from the shipping address: a region-specific zone beats a
  country-wide one; no matching zone means no tax. Each zone has a default
  rate plus optional per-tax-category overrides (e.g. GB `reduced` = 5%).
- Shipping is flat or weighted (base + per-kg on `ProductVariant.WeightGrams`);
  a method's `FreeThreshold` compares against the **discounted** subtotal and
  is inclusive (exactly 50.00 ships free).
- The gift card is tender, not a discount: it applies after tax to the final
  total, and only the remainder reaches the gateway.

The invariant `total = subtotal − discount + tax + shipping` holds to the
cent on every order and is pinned by `TotalsPipelineTests`.

## Order status derivation from fulfillments

`Order.Status` moves `Pending → Paid → PartiallyFulfilled → Fulfilled`, but
the fulfillment states are *derived*, not commanded. `FulfillmentService`
records shipments (`Fulfillment` + `FulfillmentItem` line quantities), then
recomputes cumulative coverage:

- every order line fully covered → `MarkFulfilled`
- anything shipped but not everything → `MarkPartiallyFulfilled`
- over-shipping a line (`quantity > remaining`) → 422, nothing recorded

`POST /api/orders/{n}/fulfill` is sugar for "one fulfillment covering
everything still outstanding". `Cancelled` is reachable only from
Pending/Paid — once any shipment exists, cancellation returns 409 and the
escape hatch is a refund (`Refunded` is reachable from Paid,
PartiallyFulfilled, and Fulfilled). The full 6-state × 5-action matrix is
pinned in `OrderStateMatrixTests`.

## Tender ordering and refunds

Every refund path returns each tender to its source:

- **Full refund / cancellation of a paid order** (`OrderService`): the
  gateway is refunded `total - giftCardAmount` and the gift card is credited
  `giftCardAmount`. Orders with an approved partial return refuse a full
  refund (409) to prevent over-refunding; remaining lines go through RMAs.
- **RMA approval** (`ReturnService.ApproveAsync`): refund amounts are
  discount- and tax-prorated per returned line. The refund drains the actual
  gateway charge first — counting refunds already issued for earlier approved
  RMAs on the same order — and credits any remainder back to the gift card.
  A pure gift-card refund records a `gcref_` transaction id instead of a
  gateway `rfnd_` id.
- Shipping is never refunded through an RMA; only merchandise + its tax
  share.

## Optimistic concurrency

Three row types carry an `int Version` concurrency token bumped by every
domain mutation and mapped with `.IsConcurrencyToken()`:

| Row | Protects against |
| --- | --- |
| `InventoryItem` | two checkouts reserving/committing the same units |
| `Cart` | interleaved cart edits (and cart-vs-checkout races) |
| `GiftCard` | double redemption of the same balance |

The losing writer's `SaveChangesAsync` throws
`DbUpdateConcurrencyException`, which `DomainExceptionFilter` maps to a 409
("Concurrency conflict") — clients retry by re-reading. Checkout also guards
stock logically (`Reserve` throws `InsufficientStockException` when
available < requested), so the version token is the backstop for genuinely
interleaved writers, not the primary check.

## Webhook delivery design

`WebhookService.DispatchAsync(eventType, payload)` is called inline by
checkout, fulfillment, and refund flows for `order.created`, `order.paid`,
`order.fulfilled`, and `order.refunded`:

1. Active subscriptions matching the event (CSV event list, case-insensitive)
   each get a `WebhookDelivery` row.
2. The payload is serialized once (`{ id, event, createdAt, data }`, where
   `data` is `{ orderNumber, email, status, currency, total, createdAt }`),
   signed with **hex HMAC-SHA256** over the exact payload bytes using the
   subscription's secret, and sent via `IWebhookSender` (the signature travels
   as `X-Agora-Signature`).
3. Every attempt is recorded on the delivery row (status, HTTP status code,
   attempt count, timestamps) — the delivery log is the audit trail, and the
   stored signature can be re-verified against the stored payload.
4. Failed deliveries can be retried manually
   (`POST /api/webhooks/deliveries/{id}/retry`) up to a cap of **5 attempts**;
   retrying an exhausted delivery returns 409, and retrying a *succeeded*
   delivery also returns 409 so an event is never fired at a receiver twice.

Secrets are write-only: subscription responses never echo them. The default
`IWebhookSender` is `FakeWebhookSender` (URLs containing `fail` are rejected
with a 500, everything else succeeds), mirroring `FakePaymentGateway`
(`tok_fail` / `fail…` tokens decline).

## Auth

- PBKDF2-SHA256 password hashes in a self-describing format
  (`Pbkdf2PasswordHasher`), per-hash random salt.
- HMAC-SHA256 JWTs (`Jwt` config section: issuer `agora-api`, audience
  `agora-clients`, 60-minute expiry by default) carrying `sub`, `email`, and
  `role` claims. `[Authorize(Roles = "Admin")]` gates catalog, inventory,
  discount, shipping, tax, gift card, fulfillment, webhook, report, and
  moderation mutations. Catalog reads stay public; the back-office *reads*
  (moderation queue, RMA queue, webhook subscriptions and their delivery log,
  gift-card list, all reports) are admin-only too.
- Guest flows never require an account: carts are bearer tokens, checkout
  takes an email, and RMAs authenticate by order email. A signed-in customer
  can claim a guest cart (`POST /api/carts/{token}/claim`).

## Error contract

All failures are RFC 7807 `application/problem+json`:

| Status | Meaning |
| --- | --- |
| 400 | model validation (`errors` dictionary) or domain rule violation |
| 401 / 403 | missing/invalid token / role not allowed |
| 402 | payment declined |
| 404 | resource not found (also used to avoid leaking others' resources) |
| 409 | duplicate, illegal state transition, insufficient stock, concurrency conflict, exhausted webhook retry |
| 422 | semantically invalid reference: unknown discount/gift card/shipping method/tax category, over-quantity return or shipment |
| 429 | checkout rate limit (per-client fixed window, default 10/min) |
