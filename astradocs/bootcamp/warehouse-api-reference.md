# Warehouse API reference: SS07 and SS08

All routes in this chapter require an admin bearer token. Read responses set `Cache-Control: no-store`. GUIDs must be nonempty where they identify a command or resource. JSON validation failures return 400, invalid warehouse proposals return 422, and stale lifecycle/version decisions return 409.

## Suppliers

### `POST /api/admin/suppliers`

```json
{ "name": "Northwind Supply", "reference": "NW-42" }
```

`name` is required after trimming and is limited to 120 characters. `reference` is optional, trimmed, and limited to 120 characters. The response is 201 with `id`, normalized fields, `isActive`, and server `createdAt`.

### `GET /api/admin/suppliers`

Returns all suppliers ordered by name and ID. A supplier remains visible after deactivation so historical purchasing screens can render it.

### `POST /api/admin/suppliers/{supplierId}/deactivate`

Returns the supplier with `isActive: false`. Deactivation prevents new POs. It does not invalidate an already submitted PO because delivered goods still need an honest receipt.

## Purchase orders

### `POST /api/admin/purchase-orders`

```json
{
  "supplierId": "11111111-1111-1111-1111-111111111111",
  "lines": [
    { "variantId": "22222222-2222-2222-2222-222222222222", "quantity": 10 }
  ]
}
```

The supplier must be active. Supply 1–100 distinct current variants on active products. Each ordered quantity is 1–1,000,000. The server snapshots SKU and variant name and creates a `Draft` at revision 0. It does not change stock.

### `GET /api/admin/purchase-orders/{purchaseOrderId}`

Returns supplier snapshot/current status, lifecycle timestamps, revision, ordered/received/remaining values, and immutable receipts. A nullable `variantId` means the current catalog variant was deleted; SKU/name history remains available.

### `POST /api/admin/purchase-orders/{purchaseOrderId}/submit`

```json
{ "expectedRevision": 0 }
```

Only `Draft` can become `Ordered`. Exact revision match is required. Success advances the revision once and records server submission time.

### `POST /api/admin/purchase-orders/{purchaseOrderId}/cancel`

```json
{ "expectedRevision": 1 }
```

Cancellation is allowed from `Draft`, or from `Ordered` before any receipt. It is terminal, changes no stock, records server time, and advances revision once.

### `POST /api/admin/purchase-orders/{purchaseOrderId}/receipts`

```json
{
  "operationId": "33333333-3333-3333-3333-333333333333",
  "expectedRevision": 1,
  "lines": [
    { "purchaseOrderLineId": "44444444-4444-4444-4444-444444444444", "quantity": 4 }
  ]
}
```

Supply 1–100 distinct PO line IDs with positive quantities. Every quantity must fit within that line's ordered remainder. The PO must be `Ordered` or `PartiallyReceived`; its supplier may now be inactive. Every target variant and inventory row must still exist. Resulting integer stock/version values must be representable.

For a new operation, success returns 201, increments stock, accumulates received quantities, derives `PartiallyReceived` or `Received`, and advances PO revision once. Receipt, PO, every line, and every stock row commit atomically.

Replay is keyed by `operationId`. The fingerprint includes PO ID, expected revision, and line IDs/quantities sorted by line ID. Repeating identical normalized content returns the original receipt with 200 and never restocks. Reusing the operation ID with changed content returns 409. Replay lookup precedes current revision/state checks.

## Inventory count sessions

### `POST /api/admin/inventory-counts`

```json
{ "variantIds": ["22222222-2222-2222-2222-222222222222"] }
```

Select 1–100 distinct current variants with inventory rows. One consistent read captures SKU, on-hand, reserved, and inventory version for every line. The server creates an `Open` session at revision 0. Creation changes no stock.

The counting instruction is specific to this application: count units represented by on-hand, including pending checkout reservations, while excluding paid units already deducted before fulfillment. `available = onHand - reserved`.

### `GET /api/admin/inventory-counts/{sessionId}`

Returns lifecycle state/revision, created/applied/cancelled actors and server times, plus every immutable baseline. After application, each line also contains `appliedOnHand` and `difference`, where `difference = appliedOnHand - baselineOnHand`.

### `PUT /api/admin/inventory-counts/{sessionId}/lines/{lineId}`

```json
{ "countedQuantity": 9, "expectedRevision": 0 }
```

Count is 0–1,000,000. The session must be `Open` and the revision exact. Recording/replacing an observation advances the session revision once but does not change live stock.

### `POST /api/admin/inventory-counts/{sessionId}/apply`

```json
{ "expectedRevision": 3 }
```

Every line must have a count. Every live inventory version must still equal its baseline, every variant/inventory row must exist, and every count must be at least the current reserved quantity. One conflict rejects the whole session before any stock mutation. The API never rebases a physical observation using a later database delta.

Success calls absolute `SetStock` for all lines, stores applied values/differences and actor/time, moves to `Applied`, and commits everything atomically. Repeating apply on an already applied session returns the stored applied worksheet without changing stock again, even though the submitted revision is now old.

### `POST /api/admin/inventory-counts/{sessionId}/cancel`

```json
{ "expectedRevision": 2 }
```

Only `Open` can be cancelled. Cancellation stores actor/time, advances revision, is terminal, and changes no stock. A stale or abandoned worksheet should be cancelled and recreated from a fresh baseline.

## Revision checklist

Before sending a command, read the current resource, copy its revision, and preserve the response. On 409, reload and make a new business decision. For a receipt retry after an uncertain response, retain the original operation ID, expected revision, and exact normalized content; changing any of them creates a different command.

## Operational holds and assignments

These routes require an Admin session. The order number is the existing order's immutable number.

| Method and suffix under `/api/admin/orders/{number}` | Input and result |
| --- | --- |
| `GET /holds` | Retained hold history, including active/released metadata |
| `POST /holds` | `{ "reason": "AddressQuestion", "note": "Confirm apartment number" }`; creates one active hold |
| `POST /holds/{holdId}/release` | `{ "expectedRevision": 0 }`; releases the exact observed hold revision |
| `GET /work-assignment` | Current assignment slot, including expiry; 404 when absent |
| `POST /work-assignment` | Claims available work for the authenticated admin for 15 minutes |
| `POST /work-assignment/renew` | `{ "assignmentId": "...", "expectedRevision": 0 }`; exact live owner/generation required |
| `POST /work-assignment/release` | Same command shape; exact owner/generation/revision required |

Hold reasons are `AddressQuestion`, `StockInvestigation`, and `CustomerRequest`; notes are at most 500 characters. A second active hold conflicts. Holds block both fulfillment entry points and are visible/filterable in the admin work queue. They do not reverse payment or alter stock.

An assignment's ID identifies a particular claim generation. Expiry permits a new claim with a new ID; an old ID cannot authorize a later worker. Fulfillment requests supply `assignmentId` when a live claim exists and must come from that claim's owner. The existing full-order fulfill route accepts an optional JSON body containing this ID; item-level fulfillment includes it in the regular request body. With no live assignment, existing unassigned fulfillment remains available. A supplied stale ID conflicts. Partial fulfillment retains the assignment; complete fulfillment removes it.

Revision/state/ownership-of-claim conflicts return 409. Reload the current record before deciding what to do next. Internal hold notes never appear in customer order responses. See [holds](09c-operational-holds.md) and [leases](09d-warehouse-leases.md) for worked timelines and race explanations.
