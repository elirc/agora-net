# Getting started

## Prerequisites

- .NET 10 SDK
- No database to install — persistence is SQLite in a local `agora.db` file.

## Build, test, run

```bash
dotnet build
dotnet test                                # 427 tests
dotnet run --project src/Agora.Api         # http://localhost:5077
```

`dotnet run` uses the `http` launch profile (`ASPNETCORE_ENVIRONMENT=Development`,
`http://localhost:5077`). Pick another port with `--urls http://localhost:5099`.

On startup the API applies EF Core migrations. In **Development** it also runs
`AgoraDbSeeder`, which is idempotent — it skips each block whose table already
has rows, so restarting never duplicates data. To reseed from scratch, delete
`src/Agora.Api/agora.db` and restart.

## What the seeder creates

| Kind | Data |
| --- | --- |
| Admin account | `admin@agora.dev` / `AdminPass123!` (role `Admin`) |
| Categories | Apparel, Electronics, Home & Kitchen |
| Products | 8 products / 14 variants with stock and weights (e.g. `TEE-BLK-M` @ 19.99, 55 in stock; `CDL-CDR-L` seeded at **0** stock for out-of-stock paths) |
| Discounts | `WELCOME10` (10%, usage limit 100), `SAVE5` (5.00 fixed, expires in a year), `EXPIRED10` (already expired) |
| Shipping | `standard` (flat 5.99, free at 50.00, 3–7 days, **default**), `express` (flat 14.99, 1–2 days), `freight` (weighted: 4.99 + 2.00/kg, 5–10 days) |
| Tax | Categories `standard` / `reduced` / `zero`; zones `us` (US, 8% default) and `gb` (GB, 8% default, `reduced` 5%, `zero` 0%) |

Seeded products have **no** tax category, so they fall through to their zone's
default rate (8% for a US address).

## Walkthrough

Every response below is real output from this seed data. Base URL:

```bash
B=http://localhost:5077
```

### 1. Browse the catalog

```bash
curl -s "$B/api/products?search=tee&pageSize=2"
curl -s "$B/api/products/by-slug/classic-cotton-tee"
```

Catalog reads are public. Grab the `TEE-BLK-M` variant id from `variants[]` —
call it `$VARIANT`.

### 2. Sign in as admin and issue a gift card

```bash
ADMIN=$(curl -s -X POST "$B/api/auth/login" \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@agora.dev","password":"AdminPass123!"}' | jq -r .token)

curl -s -X POST "$B/api/gift-cards" \
  -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d '{"amount":25.00}'
```

```json
{ "code": "GC-CBD543A3898A4550", "currency": "USD", "initialBalance": 25.00,
  "balance": 25.00, "isActive": true, "expiresAt": null, "createdAt": "..." }
```

Keep the code as `$GC`. Issuing is admin-only; **checking a balance is public**
— the code itself is the credential.

### 3. Open a guest cart and add two tees

```bash
TOKEN=$(curl -s -X POST "$B/api/carts" | jq -r .token)

curl -s -X POST "$B/api/carts/$TOKEN/items" \
  -H 'Content-Type: application/json' \
  -d "{\"productVariantId\":\"$VARIANT\",\"quantity\":2}"
```

The cart token is the only credential a guest needs. Subtotal is now
`39.98 USD` (2 × 19.99).

### 4. Check out with a discount **and** a gift card

```bash
curl -s -X POST "$B/api/checkout" -H 'Content-Type: application/json' -d "{
  \"cartToken\": \"$TOKEN\",
  \"email\": \"ada@example.com\",
  \"shippingAddress\": {
    \"fullName\": \"Ada Lovelace\", \"line1\": \"1 Analytical Way\",
    \"city\": \"Austin\", \"region\": \"TX\", \"postalCode\": \"73301\",
    \"country\": \"US\"
  },
  \"discountCode\": \"WELCOME10\",
  \"giftCardCode\": \"$GC\",
  \"paymentToken\": \"tok_visa\",
  \"shippingMethodCode\": \"standard\"
}"
```

```json
{ "number": "ORD-20260717-950E353A", "status": "Paid",
  "subtotal": 39.98, "discountAmount": 4.00, "taxAmount": 2.88,
  "shippingAmount": 5.99, "total": 44.85, "giftCardAmount": 25.00,
  "paymentTransactionId": "txn_8e06bec4...", "discountCode": "WELCOME10" }
```

How those numbers fall out — **discounts → tax → gift card tender**:

| Step | Value |
| --- | --- |
| subtotal | `2 × 19.99` = **39.98** |
| `WELCOME10` (10%) | **4.00** → discounted subtotal 35.98 |
| tax (US zone, 8% of the *discounted* line) | `35.98 × 0.08` = **2.88** |
| shipping (`standard`, 35.98 < 50 threshold) | **5.99** |
| total | `35.98 + 2.88 + 5.99` = **44.85** |
| gift card tender (`min(25.00, 44.85)`) | **25.00** → balance now 0 |
| charged to the gateway | `44.85 − 25.00` = **19.85** |

`GET $B/api/gift-cards/$GC` now reports `"balance": 0`. Any token other than
`tok_fail` / `fail*` is approved by the `FakePaymentGateway`; a decline returns
402, releases the stock reservation, deletes the pending order and leaves both
the cart and the gift card untouched.

### 5. Fulfill — partially, then the rest

```bash
ORD=ORD-20260717-950E353A
ITEM=<order.items[0].id>

curl -s -X POST "$B/api/orders/$ORD/fulfillments" \
  -H "Authorization: Bearer $ADMIN" -H 'Content-Type: application/json' \
  -d "{\"carrier\":\"UPS\",\"trackingNumber\":\"1Z999\",
       \"items\":[{\"orderItemId\":\"$ITEM\",\"quantity\":1}]}"
```

Shipping 1 of the 2 units derives `"status": "PartiallyFulfilled"` on the
order — the status is never commanded directly. Ship the remainder:

```bash
curl -s -X POST "$B/api/orders/$ORD/fulfill" -H "Authorization: Bearer $ADMIN"
```

→ `"status": "Fulfilled"` (`/fulfill` is shorthand for "one shipment covering
everything outstanding"). Trying to ship more than remains returns 422 and
records nothing.

### 6. Return a unit and refund it

RMAs need a **fulfilled** order. A guest authenticates with the order email:

```bash
curl -s -X POST "$B/api/orders/$ORD/returns" -H 'Content-Type: application/json' \
  -d "{\"email\":\"ada@example.com\",\"reason\":\"NoLongerNeeded\",
       \"comment\":\"Wrong size\",
       \"items\":[{\"orderItemId\":\"$ITEM\",\"quantity\":1}]}"
```

```json
{ "number": "RMA-20260717-DDD2F093", "status": "Requested",
  "refundAmount": 19.43, "items": [{ "sku": "TEE-BLK-M", "quantity": 1,
  "refundAmount": 19.43 }] }
```

19.43 is the line's discount- and tax-adjusted share:
`19.99 × (1 − 4.00/39.98) × (1 + 2.88/35.98)` = `17.99 × 1.0800…`. Shipping is
never refunded through an RMA.

```bash
curl -s -X POST "$B/api/returns/$RMA/approve" -H "Authorization: Bearer $ADMIN"
```

```json
{ "status": "Approved", "refundAmount": 19.43,
  "refundTransactionId": "rfnd_987c0bd3..." }
```

Approval refunds **tender-aware** and restocks. The gateway was only charged
19.85, so the whole 19.43 drains from there (`rfnd_` id); had the refund
exceeded the gateway charge, the remainder would be credited back to the gift
card instead (recorded as a `gcref_` id). Stock confirms the restock:

```bash
curl -s "$B/api/inventory/TEE-BLK-M"
# { "quantityOnHand": 54, "quantityReserved": 0, "quantityAvailable": 54 }
```

55 seeded − 2 sold + 1 returned = 54. And a full order refund is now blocked,
because it would over-refund the already-approved RMA:

```bash
curl -s -o /dev/null -w "%{http_code}\n" -X POST "$B/api/orders/$ORD/refund"
# 409
```

## Configuration

`src/Agora.Api/appsettings.json`:

| Key | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings:Default` | `Data Source=agora.db` | any SQLite connection string |
| `Jwt:Issuer` / `Jwt:Audience` | `agora-api` / `agora-clients` | |
| `Jwt:SigningKey` | dev key | **change in production**; ≥ 32 bytes for HMAC-SHA256 |
| `Jwt:ExpiryMinutes` | `60` | |
| `RateLimiting:Checkout:PermitLimit` | `10` | per client (remote IP), fixed window |
| `RateLimiting:Checkout:WindowSeconds` | `60` | exceeding it returns 429 |

`appsettings.Testing.json` raises the checkout limit to 100 000 so integration
tests never trip it.

## Migrations

```bash
dotnet tool restore
dotnet tool run dotnet-ef -- migrations add <Name> \
  --project src/Agora.Infrastructure --startup-project src/Agora.Api
```

## Where to go next

- [architecture.md](architecture.md) — layering, Money/converters, the checkout
  pipeline, status derivation, tender ordering, webhook delivery
- [api-reference.md](api-reference.md) — every endpoint, auth, shape, error code
- [adr/](adr/) — why the design is the way it is
- [testing.md](testing.md) — the test taxonomy and harness
