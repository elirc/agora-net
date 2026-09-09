# Durable webhook API reference

Webhook subscription routes remain under `/api/webhooks` and require Admin. Secrets are write-only. Deleting a subscription soft-deletes it and cancels queued work; an already in-flight network request cannot be retracted.

`POST /api/webhooks/deliveries/{id}/retry` schedules an existing failed delivery and returns `202 Accepted`. It does not send inline. Succeeded and five-slot-exhausted deliveries return 409.

`GET /api/admin/webhook-deliveries/{id}/attempts?page=1&pageSize=20` returns attempt history in attempt-number order. Page size is 1..100. The response includes `historyStartsAtAttempt` plus reserved/send-initiated/finished times, outcome, confirmed HTTP code, and safe reason code. It excludes payload, signature, destination URL, secret, and stack traces.

`POST /api/admin/webhook-replays` accepts:

```json
{
  "operationId": "11111111-1111-1111-1111-111111111111",
  "subscriptionId": "22222222-2222-2222-2222-222222222222",
  "eventIds": ["33333333-3333-3333-3333-333333333333"]
}
```

The list contains 1..100 distinct retained events. The active target must subscribe to every type. Events must be schema version 1 and at most 30 days old. Invalid sets return 422 without rows. A matching operation returns its durable receipt; changed content for the same operation returns 409. Results are `Enqueued` or `AlreadyExists`. The response is 202 and transport happens later.

Worker defaults: batches of at most ten, 60-second leases, 15-second hard send wait, five total slots, and retry delays of 1/5/15/60 minutes. `WebhookOutbox:Enabled=false` pauses delivery while retaining queued rows. `PollSeconds` is clamped to 1..300.

Deployment order matters: stop every legacy synchronous worker before migrating, apply the schema and legacy-delivery backfill, deploy origin writers and the outbox worker together, then enable the worker after verifying queued rows are readable. During shutdown an interrupted attempt keeps its lease; later recovery records Unknown when it expires.

Receivers must tolerate duplicates and deduplicate stable delivery/event IDs. The outbox prevents silent loss of committed intent; it cannot prove exactly-once remote handling.
