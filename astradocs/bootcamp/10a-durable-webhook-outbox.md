# Workshop 10a: Durable webhook outbox

The old flow sent a webhook after saving an order and only then saved the delivery record. A process crash between those steps lost the notification. The outbox changes the promise: commit the business fact and notification intent together, then let a worker perform transport.

## Three explanations

As a mailroom analogy, the business transaction puts a signed envelope in a durable tray. The worker may deliver it later. Restarting the mailroom does not empty the tray.

As a database rule, `Order state + OutboxEvent + matching WebhookDelivery rows` share one local commit. Sending is never inside that transaction.

As a crash table:

| Crash point | Durable state | Remote state |
|---|---|---|
| before commit | neither business change nor event | no send |
| after commit, before send | business change, event, queued deliveries | no send yet |
| after remote acceptance, before acknowledgement | event and in-flight attempt | receiver may have accepted |

The final row explains why duplicates are possible. Losing an accepted notification is worse than retrying an uncertain one, so receivers deduplicate stable delivery/event IDs.

## Frozen facts

`OutboxEvent` keeps event ID, type, schema version 1, occurrence time, and immutable data JSON. Each subscription receives a separate delivery ID, frozen destination URL, signed payload, and due time. Events remain durable even when no subscriber existed.

Both the data JSON and the completed payload envelope are capped at 64 KiB. One event/subscription pair is unique. A replay creates a delivery for the same event only when that target pair does not exist; a normal enqueue cannot create duplicates for one pair.

## Claim, send, finalize

The worker claims at most ten rows in a short transaction. Claiming reserves one of five slots, increments a lease generation, sets a 60-second lease, and inserts an attempt row. It commits before network I/O.

The worker sends with a 15-second timeout outside a transaction. It then reloads state and finalizes only when the same lease generation still owns the delivery. A late worker cannot overwrite a newer attempt.

Retry delays after explicit failures are 1, 5, 15, then 60 minutes. An expired uncertain lease consumes its slot.

## Origin-writer checklist

Checkout stages created and paid events after successful payment state is known but before its final save. Refund stages refunded with the refund transition. Fulfillment stages fulfilled only for the full transition. Partial fulfillment emits nothing.

Checkout has two database boundaries. First it commits a Pending order and inventory reservation before calling the external payment provider. After payment succeeds, one final transaction commits Paid state, stock consumption, guest access, and both staged events. If staging or that final save fails, the Pending order and reservation remain for reconciliation; Paid state, stock consumption, guest credential, event, and delivery rows all roll back together. The earlier database commit and the external payment call cannot be undone by a later SQL rollback.

`StageAsync` never calls `SaveChanges` and never sends. The origin owns the unit of work.

## Exercises

1. Why is sending inside a database transaction unsafe?
2. What survives a restart with the worker disabled?
3. Why reserve an attempt before sending?
4. Which ID should a receiver deduplicate?

## Answers

1. A database rollback cannot undo a network request, and a network wait holds database locks.
2. Events and queued deliveries; enabling the worker resumes them.
3. A crash after acceptance must still consume a slot and be represented honestly.
4. Use stable event or delivery identity according to consumer semantics; both remain stable across transport retries.

## Journal prompts

- Draw all three crash boundaries from memory.
- Explain why this does not solve payment reconciliation.
- Find every business origin and identify its final atomic save.
