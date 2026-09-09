# Workshop 10b: Webhook attempt evidence

A delivery's current status is a summary. Attempt history is evidence. Keeping them separate lets operators answer whether a receiver rejected a request, a transport failed, or a worker disappeared.

## One example three ways

Attempt 1 is reserved and send initiation is recorded. The worker disappears, so its lease expires and the attempt becomes `Unknown`. Attempt 2 later gets HTTP 200 and becomes `Succeeded`. The delivery is Succeeded with count 2; attempt 1 remains Unknown forever.

As a timeline:

```text
reserve #1 -> initiate #1 -> no acknowledgement -> lease expires -> Unknown
reserve #2 -> initiate #2 -> HTTP 200 -> Succeeded
```

As a truth rule, lack of acknowledgement is not proof of failure. The receiver may have accepted the request just before the worker died.

## Stored evidence

Every new attempt records delivery ID, attempt number, lease generation, reserved time, optional send-initiated time, optional finish time, outcome, optional confirmed HTTP code, and a bounded safe reason code. The unique delivery/attempt-number index makes one row correspond to one reserved slot.

The admin endpoint is `GET /api/admin/webhook-deliveries/{id}/attempts`. It pages in attempt-number order and excludes payloads, signatures, secrets, full URLs, and exception stacks.

## Legacy honesty

Old deliveries may already have AttemptCount 3 but no attempt rows. Migration sets `historyStartsAtAttempt` to 4. It never fabricates three detailed rows. Missing evidence is permanent metadata, not an invitation to invent history.

## Late completion

Finalization checks lease generation and terminal attempt state. If attempt 1 was marked Unknown and attempt 2 began, a late HTTP result from worker 1 changes neither row nor delivery summary.

## Exercises

1. Does `SendInitiatedAt` prove remote receipt?
2. Why can delivery Succeeded coexist with an Unknown attempt?
3. What does `historyStartsAtAttempt=4` mean?
4. Why omit exception text?

## Answers

1. No; it proves local intent immediately before invoking transport.
2. A later attempt supplied explicit success evidence.
3. Attempts 1..3 predate detailed history; the first fully evidenced new slot is 4.
4. Stacks can leak infrastructure data and are unstable API contracts; bounded reason codes support safe grouping.

## Journal prompts

- Describe Unknown without calling it failure.
- Explain why claim and attempt insertion share a transaction.
- Identify the exact guard that rejects a stale worker result.
