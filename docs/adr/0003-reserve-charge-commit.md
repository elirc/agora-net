# 3. Checkout is a reserve → charge → commit/release pipeline

Status: Accepted

## Context

Checkout spans one thing we control (stock in our database) and one we do not
(the payment gateway, a network call that can succeed, decline, or fail after
charging). The two orderings that suggest themselves both lose money:

- **Charge first, then decrement stock**: a customer is charged for a unit that
  another checkout took in the meantime. Real money, no goods.
- **Decrement first, then charge**: a decline leaks stock — the unit is gone
  from inventory with no order behind it, and nothing ever puts it back.

## Decision

Split stock into `QuantityOnHand` / `QuantityReserved` and drive a three-phase
pipeline in `CheckoutService.CheckoutAsync`:

1. **Validate everything side-effect free** — cart non-empty, products active,
   address resolvable, shipping method active, discount redeemable, gift card
   redeemable and currency-matched. Every failure here touches nothing.
2. **Reserve** each line (`InventoryItem.Reserve`, throwing
   `InsufficientStockException` → 409), compute totals, and persist the
   reservations *together with* the Pending order in one `SaveChangesAsync`.
3. **Charge** the gateway for `total − giftCardTender`, then either
   - **decline** → release every reservation, delete the pending order, keep the
     cart intact, leave the gift card untouched, respond 402; or
   - **approve** → redeem the gift card, `MarkPaid`, commit every reservation
     (`QuantityReserved--`, `QuantityOnHand--`), register the discount use,
     clear the cart's active lines, respond 201.

Reserved units are invisible to other checkouts (`QuantityAvailable =
QuantityOnHand − QuantityReserved`), so the units are held across the network
call without being sold.

## Consequences

- No one is charged for stock that isn't there, and a decline leaks neither
  stock, nor the cart, nor gift-card balance. `StockReservationEdgeTests` pins
  both outcomes.
- Validation failures cost one read and no writes.
- A gift card covering the whole total skips the gateway entirely
  (`PaymentTransactionId = "gift_<code>"`), as does a total discounted to zero
  (`"free_<order#>"`). Both are real orders with a synthetic transaction id — a
  reader can tell how an order was tendered from that prefix alone.
- **The reservation is not crash-safe.** If the process dies between the charge
  and the commit, the reservation stays held and the order stays Pending; there
  is no sweeper that expires stale reservations. This is the known cost of
  keeping the pipeline in one request without a durable saga, and it is the
  first thing to build if this ever runs in anger.
- The gateway is charged inside the request. A slow gateway is a slow checkout,
  which is why checkout is the one rate-limited endpoint.
