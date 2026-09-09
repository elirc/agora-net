# Correctness when work overlaps or stops halfway

**Outcome:** distinguish valid object state, atomic database updates, and external side effects.

## Two valid decisions can conflict

Suppose on-hand stock is 1 and reserved stock is 0. Request A loads version 7. Request B also loads version 7. Both can reserve one unit in their own objects. A saves with a predicate that includes version 7 and writes version 8. B's save with the old version should affect no matching row and raise a concurrency exception.

Read `InventoryItem.Version`, its EF mapping, and `ConcurrencyEdgeTests`. The version check protects the database update; it does not stop both requests from computing the same decision earlier. Application-managed tokens are how this repository handles SQLite. [EF concurrency documentation](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) explains the original-version comparison.

## A local transaction has a boundary

`CheckoutService` persists reservations and a pending order, then calls the gateway, then persists paid state, cart clearing, gift-card redemption, and stock commitment. A declined payment has an explicit cleanup path. An accepted payment followed by a database failure is a different case.

| Interruption point | Possible durable state | Question to answer |
| --- | --- | --- |
| Before first save | No pending order persisted | Is retry safe? |
| After pending save, before charge | Pending order and reserved stock | Who releases an abandoned reservation? |
| Charge accepted, response lost | Payment outcome unknown locally | How can we query or safely repeat the payment? |
| Charge accepted, final save fails | Gateway charged; local order still pending | How do we reconcile without double charging? |
| Paid save succeeds, process stops before webhook dispatch | Paid order; notification may be absent | How is notification work recovered? |

These are open design exercises in the current implementation. The deterministic fake gateway does not establish crash safety for real payments. Automatically retrying the whole method on any exception can repeat a charge.

## Design exercise: durable idempotency

An idempotency key represents one logical operation, scoped to a caller. A robust design needs a persisted key, a request fingerprint, a uniqueness constraint, an in-progress/completed state, and a replay policy. The same key with a different request must not silently reuse a result. The gateway also needs a stable identifier for that payment attempt and a way to reconcile an unknown outcome.

Write a sequence diagram for two simultaneous requests with the same key. Identify which database write elects the winner. Then inject a process-stop assumption after every durable step. A dictionary in process memory will not survive a restart or coordinate multiple servers.

**Checkpoint:** explain why a concurrency token, an idempotency key, and a transaction solve different problems. **Stretch:** propose an outbox row saved with paid state and a worker that retries delivery; acknowledge that receivers still need deduplication because acknowledgement can be lost.
