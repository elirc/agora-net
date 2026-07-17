# 7. Webhooks are HMAC-signed, logged per delivery, retried under a cap

Status: Accepted

## Context

Subscribers need order lifecycle events (`order.created`, `order.paid`,
`order.fulfilled`, `order.refunded`). An HTTP POST to a customer-supplied URL
raises three problems at once: the receiver cannot tell our POST from anyone
else's, deliveries fail for reasons that have nothing to do with us, and
"did that event go out?" must be answerable after the fact.

## Decision

**Signing.** Each subscription carries a `secret` (≥ 16 chars). The payload is
serialized once as `{ id, event, createdAt, data }`, and the signature is a
lowercase-hex HMAC-SHA256 over those exact payload bytes, sent as
`X-Agora-Signature`. Signing the serialized string — not a re-serialization —
is what lets a receiver recompute it byte-for-byte. Secrets are **write-only**:
no response ever echoes one back.

**Delivery log.** Every (event, subscription) pair gets a `WebhookDelivery`
row storing the payload, its signature, status, attempt count, last HTTP
status, and timestamps. The log is the audit trail, and because it stores both
payload and signature, a disputed delivery can be re-verified later.

**Retry under a cap.** Failed deliveries are retried manually via
`POST /api/webhooks/deliveries/{id}/retry`, capped at
`WebhookDelivery.MaxAttempts = 5`. Retrying an exhausted delivery returns 409 —
and so does retrying one that already **succeeded**, so an event is never
delivered twice through this path.

Transport is `IWebhookSender`; the default `FakeWebhookSender` fails URLs
containing `fail` and succeeds otherwise, mirroring `FakePaymentGateway`.

## Consequences

- Receivers can authenticate our POSTs with a shared secret and constant-time
  comparison, and reject anything else.
- Every dispatch is answerable after the fact: what was sent, when, how many
  times, and what came back.
- **Dispatch is inline and synchronous.** `WebhookService.DispatchAsync` is
  awaited inside the checkout/fulfillment/refund request, so a slow subscriber
  slows a customer-facing write, and a subscriber that is down produces a
  Failed delivery that no one retries until an admin does. This is the honest
  cost of having no queue or background worker: the log makes failures
  *visible*, it does not make them *self-healing*. A durable queue with
  exponential backoff is the obvious next step.
- Delivery rows are written after the business transaction commits, so an event
  is never logged for an order that rolled back — but the reverse is possible:
  a crash between commit and dispatch drops the event with no record. At-most-once,
  not at-least-once.
- Unknown event names are rejected at subscription time (422) against
  `WebhookEvents.All`, so a typo fails at configuration rather than by silently
  never firing.
