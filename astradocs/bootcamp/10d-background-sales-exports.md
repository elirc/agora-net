# 10d — Background sales exports

An accepted job is a promise to attempt bounded work. It is not a finished download. `POST /api/admin/report-exports` stores intent and returns 202; a worker later claims, reads, builds, and publishes.

## Learn the state machine three ways

Queued means nobody owns the work. Running means one lease generation owns it for two minutes. Succeeded means a complete artifact was atomically published. Failed means a named bound or execution rule stopped it. Cancelled means publication is forbidden.

As a timeline: create → claim generation 1 → snapshot orders → build local CSV → re-read job → publish only if generation 1 is still current, unexpired, and uncancelled.

As a safety rule: computation may be stale and repeated; publication must be current and unique.

The worker uses fresh scopes for claim, read, and publication. This is essential. A DbContext that loaded the job before computation would not see cancellation committed by another request. The final fresh read is the authority.

## Bounds and meanings

The requested interval is `[paidFrom, paidTo)`, increasing and no longer than 90 days. The worker selects at most 10,001 rows so 10,001 becomes a clear `OrderLimitExceeded` failure rather than an unbounded allocation. CSV is capped while writing at 10 MiB; no partial artifact is saved.

Each row contains order number, paid timestamp, current status, currency, purchased quantity, and the snapshotted subtotal, discount, tax, shipping, and total. Rows remain separate by currency. These are historical order totals, not net revenue after refunds.

Text cells are quoted, embedded quotes doubled, and leading `=`, `+`, `-`, or `@` prefixed with an apostrophe. Amount columns remain invariant numeric text. This prevents spreadsheet formula execution without turning numbers into labels.

## Leases, crashes, and cancellation

A claim increments both claim count and lease generation. An expired lease may be reclaimed and recomputed because export generation has no external business side effect. After three claims the job fails with `ClaimsExhausted`.

Cancellation of Queued work immediately marks Cancelled. Cancellation during Running sets a durable request flag. A delayed worker may finish its local byte array, but final publication fails its cancellation/generation/expiry check, so no artifact appears.

Two workers can read the same candidate, but the concurrency token permits one claim save. Losing the race performs no computation. A crash after claim leaves a lease that another process can recover after two minutes.

## Artifact lifecycle

Succeeded artifacts expire 24 hours after publication. Download before success returns 409. Expired download returns 410. Cleanup deletes at most 25 expired blobs per tick and retains job metadata, including expiry and result facts.

Automatic polling is disabled in `Testing` and can be disabled through configuration. Tests call `RunOnceAsync` explicitly, which makes state transitions deterministic without sleeps.

## Exercises and answers

1. Why not return CSV from POST? The request would stay open and lose durable progress on disconnect/restart.
2. Why re-read before publish? Cancellation or a newer lease can supersede the worker while it computes.
3. Why does a stale worker discard good bytes? Publishing under an obsolete lease would violate ownership.
4. Why retain job metadata after blob cleanup? Polling can still explain success and expiry rather than pretending the job never existed.
5. Why are currencies not summed? `10 USD + 10 EUR` is not a meaningful money value without an exchange-rate policy.
6. Why is the upper timestamp exclusive? Adjacent exports can use `[A,B)` and `[B,C)` without gaps or duplicates.

Explain it back as an HTTP lifecycle, then a lease lifecycle, then a data-boundary lifecycle. Journal which test distinguishes a crash recovery from a duplicate external action, and why export recomputation is safe.

## Trace one job in four views

**HTTP view.** An admin creates a job, polls its identifier, and downloads only after seeing Succeeded. The API never returns incomplete CSV. Every response is private and non-cacheable.

**Database view.** Create inserts a Queued row. Claim changes it to Running and increments its concurrency generation. Publication inserts the artifact and changes the job to Succeeded in one transaction. Either both changes commit or neither does.

**Worker view.** One iteration cleans a bounded batch of expired artifacts, attempts one claim, builds outside the claim transaction, and opens a fresh scope to publish. The fresh scope observes cancellation or a competing lease committed during the build.

**Security view.** Authentication proves the caller is an admin. Ownership further proves this admin created this job. CSV cells are encoded as untrusted input, while row and byte limits bound resource use.

Repeat the flow aloud using five verbs: **queue, claim, read, build, publish**. Then repeat it using failure questions:

1. Did another worker win the claim?
2. Did the read exceed 10,000 orders?
3. Did the encoded output exceed 10 MiB?
4. Was this lease cancelled, replaced, or expired?
5. Did the artifact and terminal state commit together?

These are two descriptions of the same implementation. The first explains the normal path; the second identifies the boundary tests.

## Follow the code without getting lost

Start at `ReportExportsController` to learn the public contract. Follow creation into `ReportExportService.QueueAsync`, then inspect `ReportExportJob` to see which transitions the domain permits. Next read `ReportExportRunner.RunOnceAsync` and follow the separate claim, build, and publish phases. Finish with `ReportExportWorker`, which schedules repeated iterations.

Read the tests in the same order. Controller tests teach status codes and ownership. Domain tests teach legal transitions. Persistence tests teach races, rollback, lease recovery, row limits, and cleanup progress. When behavior is unclear, locate the narrowest test that names it before changing production code.

## Debugging drills

**A job stays Running after a crash.** Check `LeaseExpiresAt`. Before that instant another worker must leave it alone. After that instant one worker may reclaim it and increment both claim count and generation.

**Cancellation returned success but CPU work continued.** That can be correct. Cancellation is a publication barrier, not an interrupt guarantee. Confirm that the final fresh read rejects the bytes and that no artifact row exists.

**Cleanup removed 25 blobs and stopped.** Run another iteration. The batch bound protects the database. Because the query joins only jobs that still have artifacts, retained job metadata cannot starve later blobs.

**A CSV value begins with `=`.** Confirm the emitted text begins with an apostrophe and is correctly quoted. Formula neutralization applies to text cells; invariant amount fields remain numeric.

**A job failed on its third expired claim.** This is deliberate. Repeated crashes eventually become `ClaimsExhausted`, giving operators a terminal explanation instead of an infinite retry loop.

## Suggested journal entry

Write one paragraph for each prompt:

- Draw the state transition you changed and name the method that owns it.
- State which database value prevents a stale worker from publishing.
- Describe the exact row and byte boundary, including the limit and one past it.
- Name one API response that deliberately hides resource existence and why.
- Identify the test you would run first after changing claim, cancellation, publication, or cleanup behavior.

Answer key: the lease generation is the stale-publication guard; 10,000 orders and 10 MiB are accepted while 10,001 orders or the first byte beyond 10 MiB fail without an artifact; another admin receives 404; persistence race tests are the first check for claim or publication changes.
