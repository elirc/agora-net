# API reference

## Category tree editing and integrity

Admin GET `/api/admin/category-tree` returns global `version`, `isValid`, structured `issues`, and flat `nodes` containing ID, parent ID, name, slug, and nullable depth, ordered by name then ID. GET `/api/admin/category-tree/integrity` returns revision/validity/issues/node count. Legacy loops, absent parents, and depth violations are reported without changing data. More than 5,000 categories returns 422 CategoryLimitExceeded; reads load at most 5,001 as a sentinel.

Admin POST `/api/admin/categories/{id}/move` requires `{newParentCategoryId,expectedTreeVersion}`. Explicit null parent makes a root; omitting the parent property or revision is invalid. Root depth is one and maximum depth ten, including the moved subtree's descendants. Missing parent, cycles, or invalid final topology return 422 with issues; stale tree revision returns 409. Success returns `{category,treeVersion}`. A same-parent move checks its revision and topology but does not advance the version. Moves preserve names, slugs, and product assignments.

Public GET `/api/categories/{id}/breadcrumbs` returns root-to-current nodes for the requested valid path. Missing category is 404; cyclic/missing-parent/overdeep paths return 422. A valid path in another branch remains readable when unrelated legacy problems exist. Existing category create/update/delete routes use the same serialized topology protocol. Creates, deletes, and actual parent changes advance the global revision; metadata-only updates do not. Slug conflicts and in-use deletion restrictions remain. Migration seeds revision zero without repairing existing category links. Admin reads/writes use private/no-store responses.

## Gift-card transaction ledger

Admin GET `/api/admin/gift-cards/{id}/transactions` returns `giftCardId`, `currency`, `historyStartsWith`, `openingRecordedVersion`, and `entries` as PagedResult. Pages default to 1/20, size maximum 100, ordered by recorded card version ascending. Each entry contains ID, card ID, recordedVersion, kind (OpeningBalance/Issued/Redeemed/RefundCredit), signed amount, currency, balanceAfter, recordedAt, and nullable sourceOrderId/sourceReturnId. No bearer gift-card code or payment credential is selected or returned. Unknown card returns 404; invalid pagination 400; reads are private/no-store. Ordinary GiftCardResponse adds non-secret `id` for report navigation.

New issuance saves Issued at version zero. Positive gift tender saves a negative Redeemed entry; actual order/RMA gift credits save positive RefundCredit entries with their source IDs. Balance and entry share the caller's local save. Zero tender and deactivation add no monetary entry. Migration records one OpeningBalance from each existing card's current balance/version, including zero balances; it does not reconstruct earlier history. Opening recording time is migration application time at UTC whole-second precision. Card deletion is restricted while entries exist, and source IDs survive source deletion. Ledger rows are immutable through the API. They establish local card accounting, not remote payment/refund success or recovery.

## Return eligibility and operational history

Authenticated GET `/api/me/orders/{number}/return-eligibility` requires the order's actual account owner. It returns `evaluatedAt`, nullable `deadline`, `eligible`, `reasons`, `currency`, and `lines` with order-item ID, SKU, purchased/remaining quantities, and estimated refund for the remaining quantity. Requested and Approved returns consume capacity; Rejected and Cancelled do not. Estimates preserve the existing effective order discount/tax allocation and exclude shipping. Reads are private/no-store and perform no refunds or writes. Quantities/estimates remain informative when top-level eligibility is false.

`ReturnPolicy.WindowDays` is disabled by null/unset, or must be 1–365 at startup. With a window, new return creation requires `now < FulfilledAt + WindowDays`; exact expiry is rejected. Reasons include `OrderNotFulfilled`, `MissingFulfilledAt`, `InvalidFulfilledAt`, `ReturnWindowExpired`, and `NoRemainingQuantity`. Existing return creation enforces the same calculation; previously valid submitted requests remain approvable after the deadline. The preview requires account ownership, while the existing command retains its current requester authorization.

Authenticated GET/POST `/api/me/returns/{number}/evidence` and DELETE `.../evidence/{id}` require ownership through the linked order's CustomerId. Guest email matching is insufficient. Admin GET `/api/admin/returns/{number}/evidence` inspects the same records. POST accepts `{url,description?}`: absolute HTTPS, host required, no user-info credentials, URL maximum 2,000 characters, description maximum 200. Server assigns ID, authorCustomerId, and createdAt. Unknown input properties are rejected. List is oldest creation/ID first. Five links maximum, serialized count/insert, sixth returns 409. Missing/foreign parents or scoped children return 404. Evidence can change in any return state and never changes approval/refund amounts or fetches the URL. Existing return DTOs do not embed it.

Admin GET/POST `/api/admin/fulfillments/{id}/tracking-events` exposes manual shipment history. POST accepts `{expectedVersion,status,message?}`; expectedVersion is required and nonnegative, status a defined name, message at most 200 plain-text characters. New and migrated fulfillments are Unknown/version 0 with no events. Allowed moves: Unknown→InTransit/Exception; InTransit→OutForDelivery/Delivered/Exception; OutForDelivery→Delivered/Exception; Exception→InTransit/OutForDelivery/Delivered. Delivered is terminal; same-state, forbidden, and stale-revision writes return 409. Each accepted 201 event contains a server timestamp and next sequence, saved atomically with parent status/revision. Reads return current status/version and `events` as PagedResult, page defaults 1/20, size maximum 100, ordered by sequence.

Authenticated GET `/api/me/orders/{number}/fulfillments/{id}/tracking-events` requires actual order ownership and matching fulfillment parent. Customer events omit ActorAdminId; administrator events include it. Tracking messages are customer-visible. These routes make no carrier calls and change no order status or stock.

Admin GET/POST `/api/admin/orders/{number}/notes` provides immutable internal support notes. POST accepts only `{body}` with 1–1,000 trimmed characters and returns 201 with server ID, authorAdminId, and createdAt. Client author/time fields are rejected. Pending orders return 409; missing orders 404. GET uses page defaults 1/20, maximum size 100, descending createdAt then ID. No edit/delete or notifications are provided. Notes leave order monetary/lifecycle fields unchanged and are excluded from customer order/history/timeline responses, packing slips, and webhook payloads. Historical actor IDs survive admin-account removal. History reads are private/no-store.

## Checkout quotes, saved preferences, and discount schedules

POST `/api/checkout/quote` accepts `cartToken`, `email`, optional inline `shippingAddress` or `shippingAddressId`, `shippingMethodCode`, `discountCode`, `giftCardCode`, and `useSavedPreferences` (default false). It has no payment token. Cart-token access and saved-address ownership follow checkout. Response fields are `calculatedAt`, `cartVersion`, `currency`, active `lines` (cart-item/variant IDs, display fields, quantity, unit price, line total), `subtotal`, `discountAmount`, `taxAmount`, `shippingAmount`, `total`, `giftCardAmount`, `remainingPayable`, `shippingMethodCode`, and `totalWeightGrams`. Quote and checkout share selection, validation, and calculation. Observed insufficient stock returns 409; invalid selections follow checkout's errors. Quotes are private/no-store and nonbinding: no order, reservation, usage increment, redemption, cart edit, webhook, or gateway call occurs. Checkout recalculates when submitted.

Authenticated GET/PUT `/api/me/checkout-preferences` manages `{shippingAddressId,shippingMethodCode,version}`. No saved row reads as all null. PUT requires `{shippingAddressId,shippingMethodCode,expectedVersion}`: present null expectedVersion means create-only, while an integer must match the existing revision. Creation starts at zero; replacement advances it. Null fields clear selections; a selected address must currently be owned and a method active. Invalid selection returns 422, stale/create-only conflict 409, missing expectedVersion 400. Reads are private/no-store.

Checkout and quote accept `useSavedPreferences=true` only when authenticated (otherwise 401). For each dimension, valid explicit input wins, then a saved selection if input is omitted, then existing fallback/required-input behavior. Invalid explicit selections fail instead of falling back. As before, an explicitly supplied saved-address ID takes priority over inline address. Deleted addresses clear the preference FK; stale missing/inactive methods return 422 if needed. Ownership/activity are checked again at use. The default false flag preserves existing behavior.

Discount create/update/read contracts include nullable `startsAt`. Redeemability requires `now >= startsAt` when present, plus existing active/usage rules and exclusive expiry (`now < expiresAt`). When both timestamps exist, startsAt must be earlier; invalid pairs return 400. Create/update timestamps require ISO strings with Z or an explicit offset. Omitted/null startsAt creates no schedule or clears it on replacement update. To retain a start during update, send it again. Eligibility uses one captured TimeProvider instant per quote or checkout; no scheduling worker changes IsActive.

## Account cart templates

Authenticated routes under `/api/me/cart-templates` scope resources to the current customer and use private, non-cacheable reads. POST `{name,cartToken}` creates from an owned source cart's active lines: trimmed name 1–80 characters, 1–50 distinct lines, quantities 1–99, maximum ten templates per account. Success is 201 with the detail resource; capacity is 409. Saved lines are excluded. Stored fields are variant identity, quantity, and display SKU/product/variant snapshots; no price or payment information is stored.

GET the collection returns summaries ordered by creation time and ID; GET `/{id}` returns lines ordered by SKU then ID. DELETE `/{id}` returns 204 and removes its lines. Unknown or foreign resources return 404. Historical line identities survive catalog deletion; customer deletion cascades through owned templates.

POST `/{id}/apply` accepts `{targetCartToken,expectedCartVersion}`. The target must be owned; the version is required. Apply adds quantities, activates overlapping saved lines, preserves existing target line IDs, and validates the complete result against current activity, stock, quantity limits, and one currency across active and saved lines. It returns current CartResponse with its new version. Stale revision returns 409; unusable lines return 422 with `lines` containing `templateLineId` (null for target-only problems), `variantId`, `sku`, and `reason`. Failure changes none of the target. Success changes no template, inventory, payment, or order. Applying again with a fresh version adds again; repeating the original successful request's version returns 409.

## Webhook health report

Admin GET `/api/admin/reports/webhook-health` accepts optional paired `from`/`to`, optional `subscriptionId`, and `page`/`pageSize` (default 1/20, maximum size 100). Dates select delivery creation times with an inclusive start and exclusive end, must increase, and may span at most 30 days. Omitted dates mean the seven days before one captured `asOf`. Invalid dates/pagination return 400; an explicitly requested unknown subscription returns 404.

The response contains `asOf`, `from`, `to`, `overall`, and paged `subscriptions`. Each totals object contains total/pending/succeeded/failed counts, `exhaustedFailed` (failed with at least the maximum attempts), `cohortLifetimeAttemptCount`, and a nullable `successRatio`. Overall counts cover the whole filtered cohort, independently of pagination. Empty totals have a null ratio. Subscription groups are ordered by ID; existing subscriptions with no matching deliveries produce no group. Current status and lifetime attempts are reported, even if retries happened outside the creation interval. This endpoint does not report historical per-attempt outcomes, send messages, or retry deliveries. It returns `Cache-Control: no-store` and excludes payloads, signatures, URLs, and secrets. See [the worked reporting lesson](../astradocs/bootcamp/06a-webhook-health.md).

## Packing and fulfillment reports

Admin GET `/api/admin/orders/{number}/packing-slip` returns `text/html; charset=utf-8` for Paid, PartiallyFulfilled, or Fulfilled orders. It prints order number/date, the order's shipping-address snapshot, and all order-line snapshot SKUs/names with ordered, fulfilled, and remaining quantities. Missing orders return 404, forbidden order states or inconsistent shipment totals return 409, and orders exceeding 500 lines return 422. All text is HTML-encoded. Static inline print CSS requires no remote assets. Prices, payment identifiers, gift codes, and account metadata are excluded. The response is `private, no-store`; generating it makes no fulfillment or inventory changes.

Admin GET `/api/admin/fulfillment-queue` returns `PagedResult<FulfillmentQueueOrderResponse>`. `page`/`pageSize` default to 1/20 with maximum size 100. Optional `paidFrom`/`paidTo` must be supplied together as an increasing half-open interval of at most 90 days. Invalid bounds return 400. The queue includes Paid or PartiallyFulfilled orders with positive remaining line quantities, ordered by paid time then order ID. Counts apply the same eligibility filter before paging. Each order returns its number, paid timestamp, shipping-method snapshot, and positive remaining lines, ordered by snapshot SKU then order-item ID. Lines include snapshot names and ordered/fulfilled/remaining quantities. Current inventory does not determine eligibility. An inconsistent negative remaining quantity in the filtered candidate orders returns 409. Reads are private/no-store and do not reserve, ship, or restock. See [the packing and fulfillment workshop](../astradocs/bootcamp/06b-packing-work-and-safe-documents.md).

## Stock policy and replenishment

Admin POST `/api/admin/inventory/adjustments` accepts a nonempty GUID `operationId`, a trimmed reason of 1..200 characters, and 1..50 distinct lines `{variantId, delta, expectedVersion}`. Deltas must be nonzero and between -1,000,000 and 1,000,000. Obtain each stock revision and variant ID from GET `/api/inventory/{sku}`, whose response now includes `version`. New operations require exact stock revisions and resulting on-hand between reserved stock and 1,000,000. Input errors return 400, missing inventory 404, stale revisions or changed content under an existing operation ID 409, and unusable resulting balances 422. New operations return 201 with a receipt; normalized replay returns 200 with the original receipt before checking current stock. Reason trimming and line ordering do not change request identity; changed reason/delta/version does. The receipt includes original actor/time, reason, and SKU/variant/before/after/reserved/revision snapshots. Stock and receipt commit atomically. Admin GET `/api/admin/inventory/adjustments/{operationId}` retrieves the receipt (404 when absent). Receipt history survives catalog deletion and contains no catalog-cascading foreign key. Both responses are no-store. Database contention is a conflict; retry the same ID and content to recover a possibly completed local operation. This does not provide checkout/payment idempotency. See [the batch and replay workshop](../astradocs/bootcamp/06d-atomic-stock-and-replay.md).

Admin GET/PUT `/api/admin/inventory/{variantId}/reorder-policy` reads or replaces a per-variant override. PUT requires `threshold`, `targetLevel`, and the **present but nullable** `expectedVersion` property. Values satisfy `0 <= threshold <= targetLevel <= 1,000,000`. Explicit null is create-only; an integer updates that exact revision. Missing properties/invalid values return 400; missing variants return 404; create/update revision conflicts return 409. A real variant with no override returns `hasOverride:false`, threshold/target 5, and null version/updatedAt, without inserting anything. A newly created override starts at revision 0; accepted replacements increment it. An explicit 5/5 override remains distinguishable from the computed default.

Admin GET `/api/admin/inventory/reorder-report` accepts bounded page/pageSize (1/20 by default, maximum size 100). It starts from existing inventory records, uses available = onHand - reserved, includes available at or below the effective threshold, and suggests max(0, targetLevel - available). Zero suggestions at threshold remain visible. Rows contain variant ID, snapshot-at-read catalog names/SKU, stock values, hasOverride, effective threshold/target, and suggestedQuantity. Sort is suggested quantity descending, then variant ID. It does not create policies or change inventory; inactive variants with inventory can appear. Variants lacking inventory do not appear in this stock-observation report.

Admin GET `/api/admin/reports/replenishment` accepts `windowDays` 7..90 (default 30), `coverDays` 1..60 (default 14), and bounded page/pageSize. It returns `asOf`, `from`, `to`, formula inputs, and paged `variants`. The payment-time cohort is `[asOf-windowDays, asOf)` and includes currently Paid/PartiallyFulfilled/Fulfilled orders. Net units subtract currently Approved returns on those same order lines, including approvals after the cohort interval. Only current variants with active parent products survive; missing inventory is treated as zero available. Suggested units = max(0, ceiling(netUnits / windowDays * coverDays - availableUnits)); only positive suggestions appear, sorted descending then variant ID. Each row returns netUnits, dailyAverage, availableUnits, suggestedUnits, and current catalog identity. Invalid bounds return 400; impossible negative net/availability or unsupported intermediate arithmetic returns 409. Both stock reports use consistent read transactions and no-store caching. Neither creates purchase orders, reserves stock, or mutates quantities. See [the stock policy and demand lesson](../astradocs/bootcamp/06c-stock-policy-and-demand.md).

## Saved searches, recent products, and review reports

Authenticated `/api/me/saved-searches` supports POST `{name, definition}`, GET list, GET `/{id}`, DELETE `/{id}`, and GET `/{id}/results?page=1&pageSize=20`. Names are trimmed to 1..80 characters; accounts may store at most 50 definitions (409 when full). Version-1 definitions whitelist only `search`, `categoryId`, `categorySlug`, `minPrice`, `maxPrice`, `currency`, `inStock`, `isActive`, and `sort`. Unknown definition properties are rejected, paging is never stored, and serialized definitions are bounded to 8,192 characters. Public ProductSearchRequest validation and catalog execution are reused, including current visibility, literal-text, price/currency, and sort behavior. Results use current data. A removed category does not delete the search. Reads return schemaVersion, interpreted definition, canRun, and unavailableReason; unknown or invalid stored definitions cannot run and return 409 on the results route. Other owners' IDs return 404. Reads/results are private/no-store.

Authenticated POST `/api/me/recent-products/{productId}` explicitly records a view of a currently active product (204; absent/inactive 404). Ordinary catalog and product GETs never record views. One row per customer/product stores server lastViewedAt; a transaction serializes upsert and retention of at most 50 identities. GET `/api/me/recent-products` returns at most 20 `{lastViewedAt, product}` entries, filtering active products before the limit and ordering time descending then product ID. Current product details are batch-loaded. DELETE clears only the caller's history and returns 204 even when empty. Customer/product deletion cascades history. Reads are private/no-store. See [saved criteria and explicit history](../astradocs/bootcamp/05d-saved-criteria-and-explicit-history.md).

Authenticated POST `/api/reviews/{reviewId}/reports` accepts named `reason` Spam/Abuse/OffTopic and optional plain-text `comment` up to 500 characters. The review must exist (404 otherwise), be Approved, and belong to another customer (422 otherwise). One reporter/review pair is allowed; duplicate submission returns 409. Success returns 201 with the submitted receipt, excluding reporter identities and internal resolution fields. Admin GET `/api/admin/review-reports` optionally filters named status Open/Resolved/Dismissed; omitted status includes all. Bounded paging defaults to 1/20, maximum size 100, ordered oldest first then ID. Rows include reporter ID, submission, a review excerpt of at most 200 characters, current review moderation status, and report resolution/revision fields. Admin PUT `/api/admin/review-reports/{id}/resolution` requires expectedVersion and named outcome Resolved/Dismissed, plus an optional note up to 500 characters. A report resolves once; stale or terminal edits return 409. Actual review moderation stays on its existing separate endpoints. Reporting/resolving never changes the review body or approval state. Reports cascade with the review or reporting account. See [the independent report workflow](../astradocs/bootcamp/05e-review-reports-as-a-separate-workflow.md).

## Owned order timelines, repeat purchase, and cart merging

Authenticated GET `/api/me/orders/{number}/timeline?page=1&pageSize=20` requires actual account ownership; matching email, guest access, or administrator role alone does not grant access. Foreign/missing/guest orders return 404. Page size is 1..100, and the offset cannot exceed 10,000. The paged response contains stable event keys, types, recordedAt timestamps, safe related IDs/numbers, and labels. It includes stored order created/paid/fulfilled/cancelled/refunded times, shipment creation, return creation, and timestamped terminal return processing. Missing legacy timestamps are omitted. Order is recordedAt ascending then ordinal event key; equal-time shipment and full-order fulfillment milestones remain separate. Counts cover all entries, while each source loads at most offset+pageSize candidates. The private/no-store read excludes email, payment information, return comments, and internal notes. It is recorded milestone evidence, not a complete audit log.

Authenticated POST `/api/me/orders/{number}/reorder` creates a new owned cart from historical variant IDs and grouped quantities. It accepts no price/owner fields. Foreign/missing/guest orders return 404; Pending returns 409. Cancelled/refunded history can be reused. Require 1..50 distinct variants after grouping, quantity 1..99, existing active products, one currency, and enough current available stock. Any unusable line returns 422 with historical SKU/reason details and creates no cart. Success returns 201 with today's cart representation and a location under `/api/carts/{token}`. Old prices, payment information, discounts, gift cards, shipping addresses, and order status are not copied. Inventory is not reserved or changed. Repeated successful calls intentionally create separate carts. See [history and repeat purchases](../astradocs/bootcamp/05b-history-and-repeat-purchases.md).

Authenticated POST `/api/me/carts/merge` takes `sourceToken`, `targetToken`, required `expectedSourceVersion`, and required `expectedTargetVersion`. CartResponse now exposes its inventory-independent cart `version` on ordinary reads. Target must belong to the caller; source may be unclaimed or owned by that caller. Foreign/ineligible carts return 404, identical tokens 400, stale versions 409, and empty source or unusable merged contents 422. Quantities combine by variant up to 99; a line is saved only if all copies were saved. Active lines must currently be active and in stock. All resulting lines, including saved lines, must share a currency. Success preserves target line IDs, clears source items, advances both revisions once, and returns `{target, sourceVersion, targetVersion}`. It does not change inventory. Claiming a previously unowned cart and product deletion that cascades cart lines now also advance cart revisions. A retry with old versions conflicts; a fresh-version retry finds an empty source. See [the merge and writer-audit workshop](../astradocs/bootcamp/05c-merging-carts-and-auditing-writers.md).

## General conventions

Base URL: `http://localhost:5077` (the `http` launch profile; see launch
output). Request/response bodies are JSON unless an endpoint explicitly returns another format, such as the packing slip's HTML. All errors are RFC 7807
`application/problem+json` — see [Error codes](#error-codes).

**Auth column**: `—` public · `JWT` any signed-in customer (`Authorization:
Bearer <token>`) · `Admin` requires the `Admin` role claim. Unauthenticated
calls to guarded endpoints return 401; non-admin customers get 403.

**Common shapes**

- `PagedResult<T>` — `{ items: T[], page, pageSize, totalCount, totalPages, hasPreviousPage, hasNextPage }`.
  Every paged list accepts `page` (≥ 1) and `pageSize` (1–100; the default
  differs per endpoint and is noted below); out-of-range values return 400.
  Previous means requested page > 1; next means requested page < totalPages.
  An empty first page returns false/false; a page beyond the end returns true/false.
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
| GET | `/api/me/orders` | JWT | `status?` (named order status), `page`, `pageSize` (default 20) | `PagedResult<OrderResponse>` (own orders, newest first, then ID) | 400 status/pagination, 401 |
| GET | `/api/me/returns` | JWT | `page`, `pageSize` (default 20) | `PagedResult<ReturnResponse>` (newest first) | 400 pagination, 401 |
| GET | `/api/me/addresses` | JWT | `country?` (two ASCII letters, case-insensitive) | `CustomerAddressResponse[]` `{ id, label, address, isDefault, createdAt }` (default first, then creation time/ID) | 400 country, 401 |
| GET | `/api/me/addresses/{id}` | JWT | | `CustomerAddressResponse` (caller-owned, including for admins) | 401, 404 unknown/not yours |
| POST | `/api/me/addresses` | JWT | `{ label?, address: AddressDto, isDefault? }` (first address auto-defaults) | 201 `CustomerAddressResponse` | 400 |
| PUT | `/api/me/addresses/{id}` | JWT | same | 200 `CustomerAddressResponse` | 400, 404 not yours/unknown |
| DELETE | `/api/me/addresses/{id}` | JWT | | 204 | 404 |
| POST | `/api/me/addresses/{id}/default` | JWT | | 200 `CustomerAddressResponse` (clears previous default) | 404 |

## Wishlists (`/api/me/wishlists`)

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/me/wishlists` | JWT | `search?` (literal name substring, maximum 100 raw characters) | 200 `WishlistSummaryResponse[]` `{ id, name, isDefault, itemCount, createdAt }` (auto-creates the default list even if filtered out) | 400 search, 401 |
| POST | `/api/me/wishlists` | JWT | `{ name }` | 201 `WishlistResponse` | 400, 409 duplicate name |
| GET | `/api/me/wishlists/default` | JWT | | 200 `WishlistResponse` `{ id, name, isDefault, items[], inStockItemCount, outOfStockItemCount, createdAt }` | 401 |
| GET | `/api/me/wishlists/{id}` | JWT | | 200 `WishlistResponse` | 404 |
| PUT | `/api/me/wishlists/{id}` | JWT | `{ name }` (rename) | 200 `WishlistResponse` | 400, 404, 409 duplicate name |
| DELETE | `/api/me/wishlists/{id}` | JWT | | 204 | 404, 409 default list |
| POST | `/api/me/wishlists/{id}/items` | JWT | `{ productVariantId }` | 200 `WishlistResponse` | 404 unknown/not yours, 409 duplicate item, 422 unknown variant |
| POST | `/api/me/wishlists/default/items` | JWT | `{ productVariantId }` | 200 `WishlistResponse` | 409, 422 |
| DELETE | `/api/me/wishlists/{id}/items/{itemId}` | JWT | | 200 `WishlistResponse` | 404 |
| DELETE | `/api/me/wishlists/{id}/items` | JWT | | 204 (clears items, retains list; repeated clears succeed) | 401, 404 unknown/not yours |
| POST | `/api/me/wishlists/{id}/items/{itemId}/move-to-cart` | JWT | `{ cartToken }` | 200 `CartResponse` (adds quantity 1, removes from the wishlist) | 404 (incl. someone else's cart), 409 out of stock |
| PUT | `/api/me/wishlists/{id}/items/{itemId}/note` | JWT (owner) | `{ note?, expectedVersion }` (required nonnegative revision; note at most 500 characters after trim; blank/null clears) | `{ itemId, note?, noteVersion }` | 400 invalid input, 404 missing/foreign item or wrong list, 409 stale note |
| POST | `/api/me/wishlists/{id}/copy-items` | JWT (owner of both lists) | `{ sourceId, itemIds: [guid, ...], expectedTargetVersion }` (1–50 distinct item IDs; required revision) | `{ addedVariantIds, skippedVariantIds, membershipVersion }` in selection-derived order | 400 invalid input, 404 missing/foreign list, 422 same list or item outside source, 409 stale/concurrent membership |

Wishlist items carry `{ id, productVariantId, sku, productName, variantName,
price, inStock, backInStock, addedAt, note?, noteVersion }`. Reading a list records an
out-of-stock observation per item, so `backInStock` becomes true once an item
seen out of stock is available again. The default list is named `Favorites`.
Detailed stock counts use the mapped `inStock` flags and sum to item count;
they do not count physical units. Name search escapes LIKE wildcard characters
and retains default-first, creation-time, then ID ordering.

Owned list summaries and details also expose `membershipVersion`. Membership changes
advance it; rename, notes, and stock observation do not. Treat versions as opaque
revision values, not request counts. Copy preserves source rows, skips choices already
in the target, and creates new item IDs with current stock observation and no copied
notes. Out-of-stock choices may be copied. An all-skipped request with the current
revision succeeds without advancing it. Notes appear only in owned wishlist responses.
A stock-observation save that conflicts with a concurrent note edit returns 409;
reload before retrying. Existing mutation routes may have committed their membership
change before a subsequent response-observation conflict.

## Categories

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/categories` | — | `search?` (literal name substring, max 200), `rootOnly?`, `parentCategoryId?`, `page`, `pageSize` (default 50) | `PagedResult<CategoryResponse>` `{ id, name, slug, description, parentCategoryId? }` (name then ID) | 400 query/pagination/overflow or rootOnly=true with parent ID |
| GET | `/api/categories/{id}` | — | | `CategoryResponse` | 404 |
| GET | `/api/categories/by-slug/{slug}` | — | trimmed exact, case-sensitive slug | `CategoryResponse` | 404 |
| POST | `/api/categories` | Admin | `{ name, slug?, description?, parentCategoryId? }` (slug derived from name when omitted) | 201 | 400, 409 duplicate slug, 422 unknown parent |
| PUT | `/api/categories/{id}` | Admin | `{ name, slug, description?, parentCategoryId? }` | 200 `CategoryResponse` | 400, 404, 409 duplicate slug, 422 self-parent or unknown parent |
| DELETE | `/api/categories/{id}` | Admin | | 204 | 404, 409 while products **or child categories** reference it |

## Tags and curated collections

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/tags` | — | | `[{ id, name, slug }]`, sorted by slug | |
| POST | `/api/admin/tags` | Admin | `{ name, slug }` (trimmed name 1–60; normalized immutable slug) | 201 `{ id, name, slug }` | 400 invalid input, 409 duplicate slug |
| PUT | `/api/admin/products/{id}/tags` | Admin | `{ tagIds: [], expectedVersion }` (0–20 distinct IDs, required nonnegative revision) | `{ tags: [...], tagVersion }` | 400 invalid input, 404 product, 422 unknown tag, 409 stale/concurrent edit |
| POST | `/api/admin/collections` | Admin | `{ title, slug }` (trimmed title 1–120; immutable slug) | 201 `{ id, title, slug, isPublished: false, version: 0, productIds: [] }` | 400 invalid input, 409 duplicate slug |
| GET | `/api/admin/collections/{id}` | Admin | | `{ id, title, slug, isPublished, version, productIds: [...] }`, complete stored order including inactive members | 404 |
| PUT | `/api/admin/collections/{id}` | Admin | `{ title, isPublished, productIds: [], expectedVersion }` (0–100 IDs) | Updated admin collection | 400 input, 404 collection, 422 duplicate/unknown member, 409 stale/concurrent edit |
| GET | `/api/collections/{slug}` | — | `page`, `pageSize` (1–100, default 20) | `{ id, title, slug, products: PagedResult<ProductResponse> }` in stored member order; active members only | 400 slug/paging, 404 unknown/draft |

Tag and collection slugs are trimmed and lowercased, then checked for 1–60 ASCII
letters/digits with single hyphens between segments. There are no slug-edit routes.
Product responses include all assigned `tags`, sorted by slug, and `tagVersion`.
The optional product-search `tagSlug` intersects existing filters before count/paging;
an unknown valid tag gives an empty page. Empty tag replacement clears membership.
Collections retain membership when unpublished. Product deletion cascades memberships
and advances affected collection revisions; inactive products remain in admin output.

Example workflow: create `{"name":"Summer","slug":"summer"}`, then assign its returned
ID with `{"tagIds":["<tag-id>"],"expectedVersion":0}` to an unchanged product. Search
`GET /api/products?tagSlug=summer`. Use real returned GUIDs and the current product
revision when repeating the example.

## Products & reviews

| Method | Admin editing route | Request | Response | Errors |
| --- | --- | --- | --- | --- |
| GET | `/api/admin/products/{productId}/variants/{variantId}` | | `{ id, productId, sku, name, price: MoneyDto, weightGrams, options, version }` | 404 wrong product/variant |
| PUT | `/api/admin/products/{productId}/variants/{variantId}` | `{ name, price, weightGrams, options, expectedVersion }`; complete replacement of editable fields | Updated admin variant | 400 invalid values or duplicate option keys, 404, 409 stale/concurrent edit |
| GET | `/api/admin/products/{productId}/images` | | `{ productId, version, images: [...] }` | 404 |
| POST | `/api/admin/products/{productId}/images` | `{ url, altText?, expectedVersion }` | 201 updated gallery | 400 link/text, 404, 409 revision, 422 gallery already has ten or more images |
| PUT | `/api/admin/products/{productId}/images/order` | `{ imageIds: [...], expectedVersion }`, exact current permutation | Updated gallery | 400 input, 404, 409 revision, 422 missing/extra/repeated IDs |
| DELETE | `/api/admin/products/{productId}/images/{imageId}` | Required query `expectedVersion` | Updated gallery with compact zero-based positions | 400 missing/invalid revision, 404, 409 revision |

All editing routes require Admin; anonymous access returns 401 and other roles 403.
Variant names are 1–120 characters after trim; amount is 0–1,000,000 with at most two
decimal places; weight is 0–1,000,000 grams. Options contain at most 20 pairs, with
trimmed keys 1–60 and values 1–120. Exact duplicate JSON keys and normalized duplicates
ignoring case are rejected. SKU, currency and product identity are immutable in this
editor. Live carts read the edited values; order items retain purchase snapshots.

Gallery links must be absolute HTTP/HTTPS URLs up to 2,000 characters, with optional
alt text up to 500; the server does not fetch them. Every accepted gallery write returns
a new version. New product creation accepts at most ten initial images. Existing larger
galleries can be read/reordered/reduced, and draft cloning preserves a legacy source's
complete gallery. Gallery additions are blocked until the count is below ten. The first
ordered image remains the public product's `primaryImage`.

Admin `POST /api/admin/products/{sourceId}/clone` accepts
`{ name, slug, variantSkus: [{ sourceVariantId, sku }] }`. Supply exactly one new SKU
per source variant. Identity lengths and request-SKU normalization/uniqueness match
normal creation. A source may have 0–50 variants; an empty source uses an empty list.
Success returns 201 `{ id, slug, isActive: false }` with a product-detail Location.
Description, category/tax classification, variant commercial values/options, and
image presentation are copied. All IDs are new, stock/reservations start at zero,
and reviews, cart/order history, tags and collections are excluded. Invalid input
is 400; missing source 404; missing/extra/repeated mapping or oversized source 422;
slug/SKU uniqueness conflict 409. Inactive sources are allowed. Activation is a later
normal product PUT, not part of cloning.

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/products` | — | `search`, `categoryId`, `categorySlug`, `sku`, `hasImages`, `minPrice`, `maxPrice`, `currency`, `inStock`, `isActive`, `sort` (`name`, `name_desc`, `price`, `price_desc`, `newest`, `oldest`), `page`, `pageSize` (default 20) | `PagedResult<ProductResponse>` `{ id, categoryId, name, slug, description, isActive, createdAt, variants[], variantCount, images[], primaryImage?, averageRating?, reviewCount, taxCategoryCode? }`; variants carry `{ id, sku, name, price: MoneyDto, options, weightGrams }`, images `{ id, url, altText?, sortOrder }` | 400 invalid query |
| GET | `/api/products/{id}` | — | | `ProductResponse` | 404 |
| GET | `/api/products/by-slug/{slug}` | — | | `ProductResponse` | 404 |
| POST | `/api/products/compare` | — | `{ productIds: [guid, ...] }` (2–4 distinct nonempty IDs) | `{ products: [...] }` in request order; identity, category, ordered images/variants, approved review count/average; each variant includes explicit price/currency, options, weight and `inStock` | 400 malformed input, 422 with `unusableProductIds` for any missing/inactive product |
| GET | `/api/products/{id}/reviews/summary` | — | Optional `If-None-Match` header | `{ totalCount, averageRating?, buckets: [{ stars, count }] }` with all five buckets in ascending order; `ETag`, `Cache-Control: no-cache`; 304 and empty body when a validator matches | 404 unknown product |
| POST | `/api/products` | Admin | `{ categoryId, name, slug?, description?, isActive?, variants: [{ sku, name?, price, currency?, options?, weightGrams? (integer 0–1000000, default 0) }] (≥ 1), images?, taxCategoryCode? }` | 201 | 400, 409 duplicate slug / existing SKU, 422 unknown category / duplicate SKUs in request / unknown tax category |
| PUT | `/api/products/{id}` | Admin | `{ categoryId, name, slug, description?, isActive, taxCategoryCode? }` | 200 `ProductResponse` | 400, 404, 409 duplicate slug, 422 unknown category / tax category |
| DELETE | `/api/products/{id}` | Admin | | 204 | 404 |
| GET | `/api/products/{id}/reviews` | — | `minRating?` (1–5), `sort?` (`newest`/`oldest`, trimmed and case-insensitive), `page`, `pageSize` (default 20) | `PagedResult<ReviewResponse>` (approved only, newest by default, ID tie-breaker) | 400 query/pagination, 404 unknown product |
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

Catalog search semantics:

- Price bounds are inclusive, from 0 through 1,000,000 (matching product creation limits), with at most two decimal places. Invalid/reversed bounds return 400; extra precision is rejected because storage uses cents.
- `currency` accepts three ASCII letters and normalizes to uppercase. It matches currency codes without performing currency conversion or registry validation.
- `inStock=true` requires available stock (`onHand - reserved > 0`). `inStock=false` matches a variant with missing inventory or no available stock. Omit it to ignore availability.
- One variant must satisfy every supplied SKU, price, currency, and stock filter. A product with both available and unavailable variants can match both availability searches.
- `sku` is trimmed, exact, case-sensitive, maximum 64 raw characters; blank means no filter. `hasImages` filters presence/absence of image rows without checking remote URLs.
- Variant arrays use ordinal SKU order then ID; `variantCount` counts the full array. Images use sortOrder then ID; `primaryImage` is the first ordered image or null.
- Responses retain all variants; price sorts use the cheapest variant overall. Without currency filtering, amounts are compared numerically across currencies.
- `search` is a literal substring (maximum 200 characters); `%`, `_`, and backslash are escaped for LIKE. SQLite's existing case behavior remains unchanged.
- Every sort uses product ID as a unique tie-breaker. Unknown sorts fall back to newest. The page offset must fit a signed 32-bit integer; invalid offsets return 400. Count and page are separate reads, and offset pages can shift under concurrent writes.

For a conditional review-summary read, first request
`GET /api/products/<product-id>/reviews/summary` and retain its complete `ETag` header.
Repeat that GET with `If-None-Match: "<returned-hash>"`. Unchanged content returns 304
with no body; changed content returns 200 with its current summary and ETag. Validator
lists, weak validators, and `*` are supported. Pending/rejected reviews do not count;
an empty approved set has five zero buckets and a null average. This saves response
transfer, not the current database aggregation.

## Inventory

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| GET | `/api/inventory/{sku}` | — | | `{ sku, productVariantId, quantityOnHand, quantityReserved, quantityAvailable, inStock }` (`inStock` means available > 0) | 404 |
| PUT | `/api/inventory/{sku}` | Admin | `{ quantityOnHand (0–1 000 000) }` (absolute stock take) | 200 `InventoryResponse` | 400 out of range or below current reservations, 404 |

## Carts

Carts are addressed by an opaque bearer `token` — no auth required, but a
cart created (or claimed) while signed in is attached to that account.

| Method | Route | Auth | Request | Response | Errors |
| --- | --- | --- | --- | --- | --- |
| POST | `/api/carts` | — | | 201 `CartResponse` `{ token, createdAt, updatedAt, items[], savedItems[], activeLineCount, savedLineCount, totalQuantity, subtotal }` | |
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
Line counts count entries in those respective collections, not summed quantities.

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
| GET | `/api/shipping-methods` | — | `maxDeliveryDays?` (0–365, compares MaxDays inclusively) | 200 active methods `{ code, name, rateType: "Flat"\|"Weighted", baseRate, perKgRate, freeThreshold?, minDays, maxDays, isActive, isDefault }` (base rate then code) | 400 filter |
| GET | `/api/shipping-methods/{code}` | — | | `ShippingMethodResponse` (active or not) | 404 |
| POST | `/api/shipping-methods` | Admin | `{ code, name, rateType, baseRate, perKgRate, freeThreshold?, minDays, maxDays, isActive?, isDefault? }` (a new default clears the old one) | 201 | 400, 409 duplicate code, 422 bad rate type / minDays > maxDays |
| PUT | `/api/shipping-methods/{code}` | Admin | same minus `code` | 200 `ShippingMethodResponse` | 400, 404, 422 bad rate type / minDays > maxDays |
| DELETE | `/api/shipping-methods/{code}` | Admin | | 204 | 404 |

Codes are lower-cased on write and lookup.
Rate types accept only trimmed names Flat/Weighted, ignoring case. Numeric,
comma-separated, and unknown values return 422; missing/empty required input is 400.

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
| GET | `/api/admin/reports/top-products` | `from?`, `to?` (default: last 30 days), `limit` (default 10, 1–100) | `{ sku, productName, unitsSold, revenue }[]` by revenue; inclusive endpoints, equal instants allowed | 400 bad limit or from after to |
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

## Category option schemas (SS-03)

Admin GET/PUT `/api/admin/categories/{id}/option-schema` reads/replaces a category's own schema. Missing policy reads as Off, schemaVersion 1, revision null, and no rules. PUT accepts `mode` (Off, Observe, Enforce), `expectedRevision` (explicit null for create, exact integer for replacement), and `rules`. Each rule has `key`, `required`, and `allowedValues`.

Limits: ten keys; keys normalize to lowercase ASCII letters/digits/underscore/hyphen and contain 1–40 characters; each key has 1–50 distinct trimmed ordinal values of 1–80 characters. There is no parent inheritance. Observe permits authoring and logs aggregate reasons; Enforce rejects invalid new products, clones, category changes, and actual option changes with 422. Existing unchanged products remain readable and purchasable. Other metadata/price-only edits do not retroactively enforce a schema on identical options.

Admin GET `/api/admin/categories/{id}/option-schema/violations?page=1&pageSize=20` evaluates the stored rules, including in Off mode, filters violations before paging, and orders by SKU/ID. Page size is at most 100. More than 10,000 candidate variants returns 422 rather than an incomplete report. Stale revisions and unreadable stored schema versions return 409. See [the schema workshop](../astradocs/bootcamp/08b-category-option-schemas.md).

## Catalog import preview and commit (SS-01)

Admin POST `/api/admin/catalog-imports/preview` accepts version-1 JSON with 1–100 product rows, at most 300 variants, and at most 1 MiB of request body. Each row has `rowKey` and `product`, using the existing product-create fields. Imported products are always inactive and their stock is zero. Preview stores normalized rows, digest, per-row errors, author, revision, creation time, and a 24-hour expiry; it creates no catalog products.

Admin GET `/api/admin/catalog-imports/{id}` returns the stored proposal and receipt. POST `/{id}/commit` requires `revision` and the exact `digest`. A valid draft is revalidated against live categories, tax categories, identifiers, and option schemas. A conflict creates no partial batch. A successful commit returns Applied state and row-key/product-ID receipt. A matching Applied replay returns that same receipt before checking the old draft expiry or revision. Different digest, expired/unusable draft, stale revision, or current row conflicts return 409. An invalid draft must be corrected in a new preview. Responses are private/no-store. Drafts and receipts are retained; no cleanup worker is provided. See [the worked import requests and failure examples](../astradocs/bootcamp/08g-preview-is-not-a-reservation.md).

## Quantity pricing (SS-04)

Admin GET/PUT `/api/admin/variants/{id}/quantity-pricing` manages up to five `tiers`, each with `minimumQuantity` and `unitAmount`, plus explicit nullable `expectedRevision`. Thresholds must be strictly increasing from 2 through 99. Amounts must be nonnegative whole cents, nonincreasing, and no greater than the variant's current base price when saved. Null revision creates a missing policy; an exact revision replaces an existing one. Empty tiers disable pricing. Stale replacement returns 409; invalid tier rules return 422.

Each cart line uses its highest qualifying threshold, capped at today's base unit price. `unitPrice` is applied price; additive `baseUnitPrice` and `selectedMinimumQuantity` explain it. Saved lines are priced but excluded from subtotal. Subtotal currency follows active lines, or defaults to zero USD when none are active; mixed active currencies are rejected. All cart response paths batch-load policies. Quote and checkout use the same calculation before coupon/tax/shipping/tender; order items snapshot the applied price. Returns retain historical order pricing. See [the quantity-pricing workshop](../astradocs/bootcamp/08h-one-price-calculator-many-workflows.md).

## Shipping and warehouse policy references

The [shipping API reference](../astradocs/bootcamp/shipping-api-reference.md) specifies eligibility policy, public preview, calendar replacement, date meaning, limits, and checkout behavior. The [warehouse API reference](../astradocs/bootcamp/warehouse-api-reference.md) specifies suppliers, purchase orders, receipts, inventory counts, replay identity, revisions, and stock reconciliation.
## Machine credentials and private history

See the [access API reference](../astradocs/bootcamp/access-api-reference.md) for revocable login sessions, guest order credentials, and the ownership changes to existing order/return routes.

Administrator JWT routes:

| Method and route | Contract |
| --- | --- |
| `POST /api/admin/integration-keys` | `{ "name": "Warehouse reader", "expiryDays": 30, "scopes": ["InventoryRead"] }`; 201 with metadata `key` and one-time `apiKey` |
| `GET /api/admin/integration-keys?page=1&pageSize=20` | Paged metadata only; maximum page size 100; digest/token never listed |
| `POST /api/admin/integration-keys/{id}/revoke` | Revokes future use; repeated revocation preserves the original timestamp |

Machine-only routes use `X-Agora-Api-Key`, with the entire issued value. `GET /api/integrations/catalog` requires `CatalogRead`; `GET /api/integrations/inventory` requires `InventoryRead`. Both accept `page`/`pageSize` (defaults 1/20, maximum size 100). Catalog rows describe active product variants and base prices; inventory rows include on-hand, reserved, available, and inventory revision. Invalid/expired/revoked credentials return 401; an authenticated key missing the required scope returns 403. These named policies do not grant customer or administrator access. Do not put keys in URLs.

`GET /api/me/orders/feed?limit=25&cursor=...` is an authenticated customer history route. Limit is 1..100. Response: `items`, `hasMore`, `nextCursor`; no total count. Ordering is creation time descending then unique order number descending with binary collation. Keep the returned cursor opaque and reuse the original limit. Malformed, tampered, wrong-owner, expired, or changed-limit cursors return a generic 400. Start again without a cursor after invalidation. Existing offset history remains available.

The cursor retains the initial creation-time cutoff and expires 24 hours after the first page. This is live traversal: later backdated insertions, ownership changes, and deletions may affect later pages. It does not preserve a cross-request database snapshot. Private history responses use `private, no-store`. Production cursor keys persist under configured `DataProtection:KeyDirectory` (default `data-protection-keys` beneath the content root); retain that directory across restarts and share the same protected ring/application name across serving instances. Losing the keys invalidates outstanding cursors. See [the cursor workshop](../astradocs/bootcamp/10e-seeking-through-order-history.md).

The senior workflow references include [durable webhooks and replay](../astradocs/bootcamp/webhook-api-reference.md), [private and background exports](../astradocs/bootcamp/exports-api-reference.md), [warehouse documents and coordination](../astradocs/bootcamp/warehouse-api-reference.md), and [catalog synchronization](../astradocs/bootcamp/catalog-sync-api-reference.md). These references describe the current contracts; the [story tracker](../astradocs/bootcamp/story-tracker.md) records verification status.
