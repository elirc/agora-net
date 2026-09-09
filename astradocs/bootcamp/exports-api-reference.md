# Export API reference

## Private account export

`POST /api/me/data-export` requires a current customer bearer session. It accepts no customer ID; ownership comes from `sub` and stored relationships.

Success is a version-1 JSON attachment named `agora-account-export-YYYYMMDD.json` with `Cache-Control: private, no-store`. It includes profile, addresses, owned orders/items/fulfillments/returns, owned wishlists/items, and authored reviews. The combined limit is 10,000 records and 5 MiB. Exceeding either returns 422 before any attachment bytes are sent.

| Result | Meaning |
| --- | --- |
| 200 | complete JSON attachment |
| 401 | missing, revoked, or invalid login session |
| 422 | combined record or byte bound exceeded |

Guest orders with matching email remain excluded. Password/session/guest/gift-card/integration/webhook secrets, payment references, and internal notes are excluded.

## Background sales export jobs

All routes require an Admin bearer session and are scoped to the requesting admin. Every response uses `Cache-Control: private, no-store`.

### Create

`POST /api/admin/report-exports`

```json
{
  "version": 1,
  "paidFrom": "2026-09-01T00:00:00Z",
  "paidTo": "2026-10-01T00:00:00Z"
}
```

The interval is half-open `[paidFrom, paidTo)`, must increase, and may span at most 90 days. Success returns 202 and a new job. At most ten Queued/Running jobs may exist per requesting admin.

### Poll, cancel, and download

| Method and route | Contract |
| --- | --- |
| `GET /api/admin/report-exports/{id}` | owned job state, lease generation/count, timestamps, failure code |
| `POST /api/admin/report-exports/{id}/cancel` | requests cancellation; queued work becomes Cancelled immediately |
| `GET /api/admin/report-exports/{id}/download` | complete CSV attachment for Succeeded job only |

| Result | Meaning |
| --- | --- |
| 202 | job accepted, not yet completed |
| 400 | missing/unsupported version, invalid timestamps, non-increasing or over-90-day range |
| 401 | no valid bearer session |
| 403 | authenticated non-admin |
| 404 | unknown job or job requested by another admin |
| 409 | ten-active-job cap, or artifact not ready |
| 410 | the 24-hour artifact lifetime ended or its expired blob was cleaned |

CSV columns are `orderNumber,paidAt,status,currency,purchasedQuantity,subtotal,discount,tax,shipping,total`. Each currency remains on its own labeled row. Amounts describe historical order totals, not net revenue after returns.

Workers claim with two-minute leases and at most three recoverable claims. Jobs may be Queued, Running, Succeeded, Failed, or Cancelled. Failed reason codes currently include `OrderLimitExceeded`, `ByteLimitExceeded`, `BuildFailed`, and `ClaimsExhausted`.

The worker exports at most 10,000 orders and 10 MiB. A cancelled or stale lease cannot publish. Artifacts expire after 24 hours; cleanup removes at most 25 expired blobs per run while retaining job metadata.

### State and retry matrix

| Current state | Worker may claim? | Cancel result | Download result |
| --- | --- | --- | --- |
| Queued | yes | immediately Cancelled | 409 |
| Running, live lease | no | records a cancellation request | 409 |
| Running, expired lease | yes, until the third claim | prevents later publication | 409 |
| Succeeded, unexpired artifact | no | idempotent, unchanged | 200 |
| Succeeded, expired or cleaned artifact | no | idempotent, unchanged | 410 |
| Failed | no | idempotent, unchanged | 409 |
| Cancelled | no | idempotent, unchanged | 409 |

The job identifier is not an authorization capability. The API always compares the stored requesting-admin identifier with the authenticated admin before returning state or bytes. Another admin therefore receives 404, avoiding disclosure of whether the identifier exists.

`POST /cancel` is idempotent for terminal jobs: it returns the existing state without changing its lease generation. During a Running job, cancellation can race with publication. The worker resolves that race by opening a new transaction, reloading the job, and accepting its output only when the exact lease generation is still current and cancellation has not been requested.

### Operational cutover

Enable the hosted worker only after the migration containing both report-export tables is applied. In `Testing`, leave polling disabled and drive one iteration explicitly. Cleanup runs on every worker tick, including ticks that process queued work, so a busy queue cannot indefinitely retain expired blobs.
