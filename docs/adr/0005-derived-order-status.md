# 5. Order status is derived from shipment coverage, never commanded

Status: Accepted

## Context

Sprint 13 introduced partial shipments: an order's lines can ship across
several fulfillments, with carrier and tracking per shipment. That immediately
raises the question of who owns `Order.Status`.

The obvious move is to let the caller say so — a `PATCH /orders/{n}` with
`{"status":"PartiallyFulfilled"}`, or a flag on the fulfillment request. That
makes status a claim rather than a fact, and claims drift: an order marked
Fulfilled with two units unshipped is unfalsifiable from the row itself.

## Decision

Status is a **projection of the fulfillment rows**, computed by
`FulfillmentService` after each shipment and never accepted as input:

```
covered = Σ FulfillmentItem.Quantity per OrderItem (including this shipment)
every line fully covered → order.MarkFulfilled(now)
anything shipped, not all → order.MarkPartiallyFulfilled()
```

The API exposes no way to set a status. The entity's setter is private and the
transitions are guarded: `MarkFulfilled`/`MarkPartiallyFulfilled` accept only
`Paid` or `PartiallyFulfilled`, so a status can never be reached out of order
even internally. Over-shipping a line (`quantity > remaining`) throws
`InvalidFulfillmentException` → 422, and nothing is recorded.

`POST /orders/{n}/fulfill` is sugar for "one shipment covering everything
outstanding" — the same code path with the lines computed.

## Consequences

- `Order.Status` cannot disagree with the shipments. The fulfillment rows are
  the truth; the status is a cache of them, recomputed from scratch each time
  rather than incremented.
- The state machine stays small and total: `Cancelled` is reachable only from
  Pending/Paid — once anything ships, cancellation is 409 and the escape hatch
  is a refund — while `Refunded` is reachable from Paid, PartiallyFulfilled and
  Fulfilled. `OrderStateMatrixTests` pins all 30 (state, action) pairs from a
  declared table, so adding a status without deciding its legality fails the
  suite.
- `order.fulfilled` fires only on the shipment that completes the order, never
  on partials.
- Recomputation costs one aggregate query over `FulfillmentItems` per shipment.
  At this scale that is free, and it is what keeps the derivation honest.
- RMAs deliberately require `Fulfilled`, not `PartiallyFulfilled` — you cannot
  return what hasn't shipped.
