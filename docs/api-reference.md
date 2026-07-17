# API reference

Base URL: `http://localhost:5077` (the `http` launch profile; see launch
output). All request/response bodies are JSON. All errors are RFC 7807
`application/problem+json` — see [Error codes](#error-codes).

**Auth column**: `—` public · `JWT` any signed-in customer (`Authorization:
Bearer <token>`) · `Admin` requires the `Admin` role claim. Unauthenticated
calls to guarded endpoints return 401; non-admin customers get 403.

**Common shapes**

- `PagedResult<T>` — `{ items: T[], page, pageSize, totalCount, totalPages }`.
  Every paged list accepts `page` (≥ 1) and `pageSize` (1–100; the default
  differs per endpoint and is noted below); out-of-range values return 400.
- `MoneyDto` — `{ amount: decimal, currency: "USD" }`
- `AddressDto` — `{ fullName, line1, line2?, city, region, postalCode, country }`
  (`country` is a 2-letter ISO code; `fullName`, `line1`, `city`, `region`,
  `postalCode`, `country` are all required)
- `OrderResponse` — `{ number, status, email, shippingAddress, currency,
  subtotal, discountAmount, taxAmount, shippingAmount, total, discountCode?,
  giftCardCode?, giftCardAmount, paymentTransactionId?, shippingMethodCode?,
  shippingMethodName?, estimatedDeliveryFrom?, estimatedDeliveryTo?,
  createdAt, paidAt?, fulfilledAt?, cancelledAt?, refundedAt?, items[] }`
  where each item is `{ id, productVariantId, sku, productName, variantName,
  unitPrice, quantity, lineTotal }`. `status` is one of `Pending`, `Paid`,
  `PartiallyFulfilled`, `Fulfilled`, `Cancelled`, `Refunded`.

## Health

| Method | Route | Auth | Response | Errors |
| --- | --- | --- | --- | --- |
| GET | `/health` | — | `{ status: "healthy", service: "agora-net", utcNow }` (liveness) | |
| GET | `/health/ready` | — | `Healthy` text (readiness, DB probe) | 503 `Unhealthy` when the DB probe fails |

## Auth & profile

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/auth/register` | — | `{ email, password (8–128 chars), fullName? }` | 201 `AuthResponse` `{ token, expiresAt, customer: { id, email, fullName, role, createdAt } }` | 400 validation, 409 duplicate email |
| POST | `/api/auth/login` | — | `{ email, password }` | 200 `AuthResponse` | 400 validation, 401 bad credentials |
| GET | `/api/auth/me` | JWT | | `CustomerResponse` | 401 |

Emails are lower-cased on register/login. Tokens carry `sub`, `email`, `role`
and expire after `Jwt:ExpiryMinutes` (default 60).

## Account (`/api/me`)

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/me/orders` | JWT | `page`, `pageSize` (default 20) | `PagedResult<OrderResponse>` (own orders, newest first) | 400 pagination, 401 |
| GET | `/api/me/returns` | JWT | `page`, `pageSize` (default 20) | `PagedResult<ReturnResponse>` (newest first) | 400 pagination, 401 |
| GET | `/api/me/addresses` | JWT | | `CustomerAddressResponse[]` `{ id, label, address, isDefault, createdAt }` (default first) | 401 |
| POST | `/api/me/addresses` | JWT | `{ label?, address: AddressDto, isDefault? }` (first address auto-defaults) | 201 `CustomerAddressResponse` | 400 |
| PUT | `/api/me/addresses/{id}` | JWT | same | 200 `CustomerAddressResponse` | 400, 404 not yours/unknown |
| DELETE | `/api/me/addresses/{id}` | JWT | | 204 | 404 |
| POST | `/api/me/addresses/{id}/default` | JWT | | 200 `CustomerAddressResponse` (clears previous default) | 404 |

## Wishlists (`/api/me/wishlists`)

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/me/wishlists` | JWT | | 200 `WishlistSummaryResponse[]` `{ id, name, isDefault, itemCount, createdAt }` (auto-creates the default list) | 401 |
| POST | `/api/me/wishlists` | JWT | `{ name }` | 201 `WishlistResponse` | 400, 409 duplicate name |
| GET | `/api/me/wishlists/default` | JWT | | 200 `WishlistResponse` `{ id, name, isDefault, items[], createdAt }` | 401 |
| GET | `/api/me/wishlists/{id}` | JWT | | 200 `WishlistResponse` | 404 |
| PUT | `/api/me/wishlists/{id}` | JWT | `{ name }` (rename) | 200 `WishlistResponse` | 400, 404, 409 duplicate name |
| DELETE | `/api/me/wishlists/{id}` | JWT | | 204 | 404, 409 default list |
| POST | `/api/me/wishlists/{id}/items` | JWT | `{ productVariantId }` | 200 `WishlistResponse` | 404 unknown/not yours, 409 duplicate item, 422 unknown variant |
| POST | `/api/me/wishlists/default/items` | JWT | `{ productVariantId }` | 200 `WishlistResponse` | 409, 422 |
| DELETE | `/api/me/wishlists/{id}/items/{itemId}` | JWT | | 200 `WishlistResponse` | 404 |
| POST | `/api/me/wishlists/{id}/items/{itemId}/move-to-cart` | JWT | `{ cartToken }` | 200 `CartResponse` (adds quantity 1, removes from the wishlist) | 404 (incl. someone else's cart), 409 out of stock |

Wishlist items carry `{ id, productVariantId, sku, productName, variantName,
price, inStock, backInStock, addedAt }`. Reading a list records an
out-of-stock observation per item, so `backInStock` becomes true once an item
seen out of stock is available again. The default list is named `Favorites`.

## Categories

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/categories` | — | `page`, `pageSize` (default 50) | `PagedResult<CategoryResponse>` `{ id, name, slug, description, parentCategoryId? }` (by name) | 400 pagination |
| GET | `/api/categories/{id}` | — | | `CategoryResponse` | 404 |
| POST | `/api/categories` | Admin | `{ name, slug?, description?, parentCategoryId? }` (slug derived from name when omitted) | 201 | 400, 409 duplicate slug, 422 unknown parent |
| PUT | `/api/categories/{id}` | Admin | `{ name, slug, description?, parentCategoryId? }` | 200 `CategoryResponse` | 400, 404, 409 duplicate slug, 422 self-parent |
| DELETE | `/api/categories/{id}` | Admin | | 204 | 404, 409 while products **or child categories** reference it |

## Products & reviews

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/products` | — | `search`, `categoryId`, `categorySlug`, `minPrice`, `maxPrice`, `isActive`, `sort` (`name`, `name_desc`, `price`, `price_desc`, `newest`, `oldest`), `page`, `pageSize` (default 20) | `PagedResult<ProductResponse>` `{ id, categoryId, name, slug, description, isActive, createdAt, variants[], images[], averageRating?, reviewCount, taxCategoryCode? }`; variants carry `{ id, sku, name, price: MoneyDto, options }`, images `{ id, url, altText?, sortOrder }` | 400 pagination |
| GET | `/api/products/{id}` | — | | `ProductResponse` | 404 |
| GET | `/api/products/by-slug/{slug}` | — | | `ProductResponse` | 404 |
| POST | `/api/products` | Admin | `{ categoryId, name, slug?, description?, isActive?, variants: [{ sku, name?, price, currency?, options? }] (≥ 1), images?, taxCategoryCode? }` | 201 | 400, 409 duplicate slug / existing SKU, 422 unknown category / duplicate SKUs in request / unknown tax category |
| PUT | `/api/products/{id}` | Admin | `{ categoryId, name, slug, description?, isActive, taxCategoryCode? }` | 200 `ProductResponse` | 400, 404, 409 duplicate slug, 422 unknown category / tax category |
| DELETE | `/api/products/{id}` | Admin | | 204 | 404 |
| GET | `/api/products/{id}/reviews` | — | `page`, `pageSize` (default 20) | `PagedResult<ReviewResponse>` (approved only, newest first) | 400 pagination, 404 unknown product |
| POST | `/api/products/{id}/reviews` | JWT | `{ rating (1–5), title?, body }` | 201 `ReviewResponse` (status `Pending`) | 400, 404 unknown product, 409 already reviewed, 422 not a verified purchaser |
| PUT | `/api/reviews/{id}` | JWT (author) | `{ rating, title?, body }` | 200 `ReviewResponse` (back to `Pending`, note cleared) | 400, 404 not yours |
| DELETE | `/api/reviews/{id}` | JWT (author) | | 204 | 404 |
| POST | `/api/reviews/{id}/helpful` | JWT | | 200 `ReviewResponse` (one vote per customer) | 404 unknown or not-approved review, 409 already voted |
| DELETE | `/api/reviews/{id}/helpful` | JWT | | 200 `ReviewResponse` | 404 no vote of yours |
| GET | `/api/reviews` | Admin | `status` (`pending` default, `approved`, `rejected`, `all`), `page`, `pageSize` (default 20) | `PagedResult<ReviewResponse>` moderation queue (oldest first) | 400 bad status/pagination, 401/403 |
| POST | `/api/reviews/{id}/approve` | Admin | | 200 `ReviewResponse` | 404 |
| POST | `/api/reviews/{id}/reject` | Admin | `{ note? }` | 200 `ReviewResponse` | 404 |

`ReviewResponse` — `{ id, productId, reviewerName, rating, title, body,
status, moderationNote?, helpfulCount, createdAt, updatedAt }`. `reviewerName`
is the customer's full name, falling back to the local part of their email.
Approve/reject are idempotent: they do **not** 409 on an already-moderated
review.

Verified purchase = the customer has an order in `Paid`,
`PartiallyFulfilled`, or `Fulfilled` containing a variant of the product.

## Inventory

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/inventory/{sku}` | — | | `{ sku, productVariantId, quantityOnHand, quantityReserved, quantityAvailable }` | 404 |
| PUT | `/api/inventory/{sku}` | Admin | `{ quantityOnHand (0–1 000 000) }` (absolute stock take) | 200 `InventoryResponse` | 400 out of range or below current reservations, 404 |

## Carts

Carts are addressed by an opaque bearer `token` — no auth required, but a
cart created (or claimed) while signed in is attached to that account.

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/carts` | — | | 201 `CartResponse` `{ token, createdAt, updatedAt, items[], savedItems[], totalQuantity, subtotal }` | |
| GET | `/api/carts/{token}` | — | | 200 `CartResponse` with live pricing | 404 |
| POST | `/api/carts/{token}/claim` | JWT | | 200 `CartResponse` (attaches a guest cart to the account) | 404, 409 owned by another account |
| POST | `/api/carts/{token}/items` | — | `{ productVariantId, quantity (1–99) }` (same variant merges, re-activating a saved line) | 200 `CartResponse` | 400 quantity out of range, 400 merged quantity over 99, 404 unknown cart, 409 insufficient stock, 422 unknown variant or inactive product |
| PUT | `/api/carts/{token}/items/{itemId}` | — | `{ quantity (0–99) }` (0 removes the line) | 200 `CartResponse` | 400, 404 unknown cart/item, 409 insufficient stock |
| POST | `/api/carts/{token}/items/{itemId}/save-for-later` | — | | 200 `CartResponse` (line leaves totals/checkout) | 404 |
| POST | `/api/carts/{token}/items/{itemId}/activate` | — | | 200 `CartResponse` (line restored to totals) | 404, 409 insufficient stock |
| DELETE | `/api/carts/{token}/items/{itemId}` | — | | 200 `CartResponse` | 404 |
| DELETE | `/api/carts/{token}` | — | | 204 (cart emptied, token stays valid) | 404 |

`items` are the active lines (counted in `totalQuantity`/`subtotal`);
`savedItems` are saved-for-later lines, excluded from both and from checkout.

## Checkout

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/checkout` | — (JWT attaches the order to the account) | `{ cartToken, email, shippingAddress?: AddressDto, discountCode?, paymentToken, shippingMethodCode?, shippingAddressId?, giftCardCode? }` | 201 `OrderResponse` | 400 empty cart / missing address / deactivated product / variant gone, 402 payment declined, 404 unknown cart or saved address, 409 insufficient stock, 422 invalid discount / gift card / shipping method, 429 rate limited |

Notes: `shippingAddressId` (address book) requires being signed in (400
otherwise) and wins over the inline address; the order stores its own copy.
Omitting `shippingMethodCode` uses the active default method (422 if none is
configured). A gift card must match the cart's currency. The
`FakePaymentGateway` declines `tok_fail` and any token prefixed `fail`. Rate
limit: per-client fixed window keyed by remote IP (`RateLimiting:Checkout`,
default 10 per 60 s).

## Orders & fulfillment

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/orders/{number}` | — | | 200 `OrderResponse` (order numbers are unguessable) | 404 |
| POST | `/api/orders/{number}/fulfill` | Admin | | 200 `OrderResponse` — ships everything outstanding | 404, 409 wrong state, 422 nothing left to fulfill |
| POST | `/api/orders/{number}/fulfillments` | Admin | `{ carrier?, trackingNumber?, items?: [{ orderItemId, quantity }] }` (omit `items` = ship all remaining) | 201 `FulfillmentResponse` `{ number, carrier, trackingNumber?, createdAt, items[] }` | 400, 404, 409 wrong state, 422 over-shipment / duplicate lines / foreign lines / nothing outstanding |
| GET | `/api/orders/{number}/fulfillments` | — | | 200 `FulfillmentResponse[]` (oldest first) | 404 |
| POST | `/api/orders/{number}/cancel` | — | | 200 `OrderResponse` (`Cancelled`; paid orders are refunded per tender + restocked) | 404, 409 once anything shipped |
| POST | `/api/orders/{number}/refund` | — | | 200 `OrderResponse` (`Refunded`; gateway + gift card each refunded their share, items restocked) | 404, 409 wrong state or approved partial returns exist |

Fulfillment items are `{ orderItemId, productVariantId, sku, quantity }`.
`carrier` defaults to `"Manual"`. Shipping does not touch inventory —
checkout already committed the stock.

## Returns (RMA)

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/orders/{number}/returns` | — (owner account or order email) | `{ email?, reason (Damaged/WrongItem/NotAsDescribed/NoLongerNeeded/Other), comment?, items: [{ orderItemId, quantity }] (≥ 1) }` | 201 `ReturnResponse` | 400, 404 wrong email/unknown order, 409 order not `Fulfilled`, 422 unknown reason / over-quantity / duplicate / foreign lines |
| GET | `/api/returns/{number}` | — | | 200 `ReturnResponse` (status tracking) | 404 |
| POST | `/api/returns/{number}/cancel` | — (owner or email) | `{ email? }` | 200 `ReturnResponse` (`Cancelled`) | 404, 409 not `Requested` |
| GET | `/api/returns` | Admin | `status` (`requested` default, `approved`, `rejected`, `cancelled`, `all`), `page`, `pageSize` (default 20) | `PagedResult<ReturnResponse>` queue (oldest first) | 400 bad status/pagination, 401/403 |
| POST | `/api/returns/{number}/approve` | Admin | | 200 `ReturnResponse` (`Approved`) — refunds tender-aware (gateway first, gift card remainder) and restocks | 402 refund declined, 404, 409 not `Requested` |
| POST | `/api/returns/{number}/reject` | Admin | `{ note? }` | 200 `ReturnResponse` (`Rejected`, no refund/restock) | 404, 409 not `Requested` |

`ReturnResponse` — `{ number, orderNumber, status, reason, comment,
rejectionNote?, refundAmount, currency, refundTransactionId?, createdAt,
processedAt?, items[] }` where items are `{ orderItemId, productVariantId,
sku, quantity, refundAmount }`.

Refund amounts are computed at creation: per line,
`unitPrice × qty × (1 − orderDiscountRate) × (1 + orderTaxRate)`, rounded
per line. Quantities already tied up in `Requested` or `Approved` RMAs count
against what is still returnable. Shipping is not refunded via RMA.

## Discounts

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/discounts` | — | `page`, `pageSize` (default 50) | `PagedResult<DiscountResponse>` `{ id, code, type, value, currency, expiresAt?, usageLimit?, timesUsed, isActive }` (by code) | 400 pagination |
| GET | `/api/discounts/{code}` | — | | `DiscountResponse` | 404 |
| POST | `/api/discounts` | Admin | `{ code, type: "Percentage"\|"FixedAmount", value (0.01–1 000 000), currency?, expiresAt?, usageLimit?, isActive? }` (code is uppercased) | 201 | 400, 409 duplicate code, 422 bad type / percentage > 100 |
| PUT | `/api/discounts/{code}` | Admin | `{ expiresAt?, usageLimit?, isActive }` | 200 `DiscountResponse` | 400, 404 |
| DELETE | `/api/discounts/{code}` | Admin | | 204 | 404 |

Codes are looked up case-sensitively (they are stored uppercased). A code is
redeemable while active, unexpired, and under its usage limit.

## Shipping methods

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/shipping-methods` | — | | 200 active methods `{ code, name, rateType: "Flat"\|"Weighted", baseRate, perKgRate, freeThreshold?, minDays, maxDays, isActive, isDefault }` (cheapest first) | |
| GET | `/api/shipping-methods/{code}` | — | | `ShippingMethodResponse` (active or not) | 404 |
| POST | `/api/shipping-methods` | Admin | `{ code, name, rateType, baseRate, perKgRate, freeThreshold?, minDays, maxDays, isActive?, isDefault? }` (a new default clears the old one) | 201 | 400, 409 duplicate code, 422 bad rate type / minDays > maxDays |
| PUT | `/api/shipping-methods/{code}` | Admin | same minus `code` | 200 `ShippingMethodResponse` | 400, 404, 422 bad rate type / minDays > maxDays |
| DELETE | `/api/shipping-methods/{code}` | Admin | | 204 | 404 |

Codes are lower-cased on write and lookup.

## Tax

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/tax-categories` | — | | 200 `{ id, code, name }[]` (by code) | |
| POST | `/api/tax-categories` | Admin | `{ code, name }` (code lower-cased) | 201 | 400, 409 duplicate code |
| GET | `/api/tax-zones` | — | | 200 `TaxZoneResponse[]` `{ id, code, name, country, region?, defaultRate, isActive, rates: [{ taxCategoryCode, rate }] }` (by code) | |
| POST | `/api/tax-zones` | Admin | `{ code, name, country (ISO-2), region?, defaultRate (0–1), isActive?, rates? }` | 201 | 400, 409 duplicate code, 422 unknown tax category |
| PUT | `/api/tax-zones/{code}` | Admin | same (rates are replaced wholesale) | 200 `TaxZoneResponse` | 400, 404, 422 unknown tax category |
| DELETE | `/api/tax-zones/{code}` | Admin | | 204 | 404 |

Zone codes are lower-cased; `country`/`region` are upper-cased.

## Gift cards

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/gift-cards` | Admin | `{ amount (0.01–100 000), currency?, expiresAt? }` | 201 `GiftCardResponse` `{ code ("GC-…"), currency, initialBalance, balance, isActive, expiresAt?, createdAt }` | 400 |
| GET | `/api/gift-cards/{code}` | — | | 200 `GiftCardResponse` (balance check; the code is the credential) | 404 |
| GET | `/api/gift-cards` | Admin | `page`, `pageSize` (default 20) | `PagedResult<GiftCardResponse>` (newest first) | 400 pagination, 401/403 |
| POST | `/api/gift-cards/{code}/deactivate` | Admin | | 200 `GiftCardResponse` (`isActive: false`) | 404 |

Codes are matched upper-cased. A card is redeemable while active, unexpired,
and holding a positive balance.

## Webhooks (all Admin)

| Method | Route | Request | Response | Errors |
| --- | --- | --- | --- | --- |
| GET | `/api/webhooks` | `page`, `pageSize` (default 50) | `PagedResult<WebhookSubscriptionResponse>` `{ id, url, events, isActive, createdAt }` (secret never echoed, oldest first) | 400 pagination |
| GET | `/api/webhooks/{id}` | | `WebhookSubscriptionResponse` | 404 |
| POST | `/api/webhooks` | `{ url, secret (16–200 chars, write-only), events: ["order.created"\|"order.paid"\|"order.fulfilled"\|"order.refunded"], isActive? }` | 201 | 400, 422 unknown event |
| PUT | `/api/webhooks/{id}` | same | 200 `WebhookSubscriptionResponse` | 400, 404, 422 unknown event |
| DELETE | `/api/webhooks/{id}` | | 204 | 404 |
| GET | `/api/webhooks/{id}/deliveries` | `page`, `pageSize` (default 20) | `PagedResult<WebhookDeliveryResponse>` `{ id, subscriptionId, eventType, payload, signature, status, attemptCount, lastResponseStatusCode?, lastAttemptAt?, createdAt }` (newest first) | 400 pagination, 404 unknown subscription |
| POST | `/api/webhooks/deliveries/{id}/retry` | | 200 `WebhookDeliveryResponse` (new attempt recorded) | 404, 409 already succeeded or 5-attempt cap reached |

Event names are lower-cased and de-duplicated on write. Delivery `status` is
`Pending`, `Succeeded`, or `Failed`. Payloads are
`{ id, event, createdAt, data }` with `data` =
`{ orderNumber, email, status, currency, total, createdAt }`; `signature` is
lowercase-hex HMAC-SHA256 of the exact payload string using the subscription
secret, sent as `X-Agora-Signature`.

## Admin reports (all Admin)

| Method | Route | Query | Response | Errors |
| --- | --- | --- | --- | --- |
| GET | `/api/admin/reports/sales` | `from?`, `to?` (default: last 30 days), `interval` (`day` default / `week` / `month`) | `{ from, to, interval, totalOrders, totalRevenue, buckets: [{ period, orderCount, itemsSold, grossRevenue }] }`, bucketed by payment date | 400 bad interval or `from` after `to` |
| GET | `/api/admin/reports/top-products` | `from?`, `to?` (default: last 30 days), `limit` (default 10, 1–100) | `{ sku, productName, unitsSold, revenue }[]` by revenue | 400 bad limit |
| GET | `/api/admin/reports/low-stock` | `threshold` (default 5, ≥ 0), `page`, `pageSize` (default 50) | `PagedResult<{ sku, productName, variantName, quantityOnHand, quantityReserved, quantityAvailable }>`, scarcest first | 400 bad threshold/pagination |
| GET | `/api/admin/reports/discount-usage` | | `{ code, type, timesUsed, orderCount, totalDiscounted, totalRevenue }[]` per code (by code) | |

Bucket `period` keys are `yyyy-MM-dd` (day), `yyyy-Www` ISO week, or `yyyy-MM`
(month). Reports count only paid orders.

## Error codes

| Status | Used for |
| --- | --- |
| 400 | DataAnnotations validation (ProblemDetails with `errors`), domain rule violations (empty cart, quantity caps, negative stock), out-of-range pagination |
| 401 | missing/invalid bearer token on a guarded endpoint |
| 402 | payment gateway declined (checkout charge or RMA refund) |
| 403 | authenticated but not `Admin` on an admin endpoint |
| 404 | unknown resource — also returned for resources owned by someone else (addresses, wishlists, carts, RMAs) to avoid existence leaks |
| 409 | duplicates (email, slug, SKU, code, wishlist name), illegal state transitions, insufficient stock, optimistic-concurrency conflicts, webhook retry of a succeeded/exhausted delivery |
| 422 | semantically invalid references: unknown/expired/exhausted discount, unusable gift card, unknown shipping method or tax category, over-quantity returns/shipments, unknown webhook event, unknown cart variant |
| 429 | checkout rate limit exceeded |
| 503 | `/health/ready` when the database probe fails |

Domain exceptions map to statuses in `DomainExceptionFilter`:
`NotFoundException` → 404; `InsufficientStockException`,
`InvalidOrderStateException`, `InvalidReturnStateException`,
`InvalidWebhookDeliveryException`, `DbUpdateConcurrencyException` → 409;
`InvalidFulfillmentException`, `InvalidReturnRequestException`,
`InvalidDiscountException`, `InvalidShippingMethodException`,
`InvalidGiftCardException` → 422; `PaymentFailedException` → 402; any other
`DomainException` → 400.
