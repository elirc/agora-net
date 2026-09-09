# Workshop 10c: Historical webhook replay

Retry and replay solve different problems. Retry repeats transport for an existing delivery. Replay creates a delivery of an old retained business event to a subscription that may not have existed then.

## Shared fact, separate envelopes

OrderPaid event E occurred yesterday. Subscription A existed, so delivery A1 was created. Subscription B is added today. Replaying E to B creates delivery B1.

```text
event E (original time/data)
  -> delivery A1 (old destination/signature)
  -> delivery B1 (current B destination/signature)
```

E keeps business identity. B1 gets transport identity. The payload uses E's original data/time and B's current URL/secret.

## Request contract

`POST /api/admin/webhook-replays` accepts an operation GUID, target subscription ID, and 1..100 distinct retained event IDs. IDs are sorted for a canonical request digest.

The target must be active and subscribed to every type. Every event must exist, be schema version 1, and be no more than 30 days old at one captured evaluation time. The whole set validates before any delivery is added.

## Durable idempotency

The operation ID is looked up before current target validation. Reusing operation/content returns the saved receipt even after later deactivation. Reusing the ID with changed content returns 409.

For a different operation, an existing event/subscription pair is `AlreadyExists` regardless of success or failure. Use normal retry for that delivery. Missing pairs become `Enqueued`. Receipt, results, and deliveries commit atomically; the API returns 202 and never sends inline.

## Why replay never reloads an order

Prices, customer details, and order status may have changed. Reconstructing from today's tables would rewrite history. `OutboxEvent.DataJson` is the immutable source.

## Exercises

1. A failed E/B delivery exists. Should replay create another?
2. What changes between A1 and B1?
3. One of 50 events is too old. How many deliveries commit?
4. Why look up a matching operation before checking current subscription activity?

## Answers

1. No; report AlreadyExists and use retry.
2. Delivery ID, current target URL, and signature. Event ID, occurrence time, schema, and business data remain.
3. Zero; validation is all-or-nothing.
4. An accepted command remains replayable as the same durable receipt even when later configuration changes.

## Journal prompts

- Explain event identity versus delivery identity using your own analogy.
- Draw the transaction containing batch, results, and missing deliveries.
- Why is arbitrary payload editing excluded?
