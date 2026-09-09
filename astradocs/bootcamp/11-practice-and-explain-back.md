# Practice: become the person who can change this codebase

Use this page after one or two workshops, and return after the later modules. You do not need to finish the whole bootcamp before practicing. The [tracker](story-tracker.md) tells you which implementations have passing evidence; the [journal](journal.md) includes failures encountered while building them.

## A repeatable 25-minute session

Choose one endpoint and one example request. Write down the expected status, response, and database changes before reading the implementation. Then trace controller, service, domain rule, database mapping, and response mapping. Run the smallest relevant test group. Finish by changing one input in your prediction and explaining why the result changes.

Keep three short sentences in [your learning log](learning-log.md): “I expected…”, “I observed…”, and “The rule I missed was…”. A corrected prediction is useful progress.

## First pass: explain without framework vocabulary

| Prompt | A useful answer |
| --- | --- |
| Why does a saved cart item not affect checkout totals? | It is stored for later, so it is outside the active purchase. |
| Why can a paid order retain an old unit price? | The order records the agreed purchase, even when the catalog changes later. |
| Why does a stock count record a baseline? | The warehouse observation must be compared with the state it actually observed. |
| Why does a login token sometimes stop working before its printed expiry? | Its server-side login session was revoked or no longer matches the account. |
| Why is a queued export not downloadable yet? | Accepting the request and finishing its file are separate steps. |
| Why might a webhook arrive twice? | A receiver can process it even when the sender loses the acknowledgement. |

Cover the answer column and use a different example. If you can only repeat the original wording, return to the worked example before moving on.

## Second pass: find the responsible boundary

For each scenario, identify the layer that knows enough to enforce the rule. More than one layer may participate.

1. An HTTP request supplies page size 10,000.
2. A customer requests another customer's private export.
3. A stock correction includes one stale inventory revision among five lines.
4. A worker finishes after another worker has acquired a newer lease.
5. An import preview was valid yesterday but its SKU is taken today.
6. A product is deleted while a disconnected catalog consumer still has its old copy.

Answers:

1. Validate the HTTP query before running the bounded database query. A database limit still constrains the actual read.
2. Derive the owner from authentication and filter every exported relationship by ownership. The export does not accept a customer ID to trust.
3. Validate all observed versions and commit all stock changes in one transaction. A stale line prevents the whole correction.
4. Re-read durable state and check current generation/status/expiry before publication. A locally held object is insufficient evidence.
5. Revalidate live constraints in the commit transaction. Preview is a staged proposal, not a resource reservation.
6. Persist a deletion tombstone without a cascading product foreign key, so a later feed read can remove that product from the mirror.

## Third pass: predict the database after a failure

Do this on paper before looking at a rollback test.

| Failure | What may already exist? | What must not be committed? |
| --- | --- | --- |
| Invalid import at commit | The saved preview draft | Any subset of the proposed products or their feed changes |
| Failed purchase receipt line | The open purchase order | A receipt claiming success or stock changes for only some lines |
| Webhook attempt insert fails | A queued delivery | An incremented attempt slot with no corresponding attempt evidence |
| Catalog snapshot exceeds its byte cap | The product's previous committed version | The new product state, its revision, or a partial feed event |
| Cancelled export finishes late | The job and cancellation request | A newly published artifact from that stale execution |

Now explain the important exception in checkout: an external payment call cannot be rolled back by a SQLite transaction. The current outbox makes local event intent atomic with local business completion; it does not implement provider reconciliation. Distinguishing local guarantees from external side effects is part of reviewing the code honestly.

## A debugging drill from the implementation journal

A shipping request returns 500 before the controller's business logic runs. The JSON looks valid. What do you inspect first?

Start with the exception and framework validation metadata, not a rewrite of shipping arithmetic. Positional records need validation attributes in the location expected by ASP.NET model validation. A serialization attribute and a validation attribute may need different targets. The journal records this real failure and the focused rerun that follows its correction.

Another build says a type cannot be found. Check the declared namespace, the using directive, and the project/package reference in that order. Adding an import cannot supply a missing assembly; adding a package cannot fix an import placed after a namespace's declarations. These small errors are useful opportunities to read the compiler's exact evidence.

## Design review drills

**“Let us retry everything on 409.”** Explain why that is unsafe. A stock receipt with the same operation ID/content has an explicit replay contract. A stale edit may need a new business decision. A payment-related failure may have external effects. The status code alone does not define a safe retry algorithm.

**“The cursor is encrypted, so we can trust its customer ID.”** Explain why the route must still authenticate the caller and independently filter rows by that caller. Protection preserves issued state; it does not establish who is making the new request.

**“We will count bytes after producing the complete file.”** Explain the memory problem. Rejecting an oversized response at the end can still allocate the oversized object first. A bounded output stream stops accumulation at the threshold; a bounded record query limits the input side too.

**“We can clean old rows by timestamp wherever they appear.”** Explain why a sequential feed needs a contiguous retention prefix. Jumping over a recent barrier makes a single retention floor misleading.

## Small independent exercises

Use an isolated branch or a disposable copy. These are practice tasks, not additional requirements secretly added to the feature backlog.

1. Write a new example for the quantity-tier calculator, calculate the cents by hand, then add a focused test that would catch choosing the wrong threshold.
2. Trace an order from checkout to return. Draw which amounts are current catalog values and which are historical snapshots.
3. Change a cursor's requested page size without changing the token. Predict and verify the response, then explain why the binding exists.
4. In a temporary test database, stage a catalog mutation and roll back the outer transaction. Read product, event, and watermark from a fresh context.
5. Simulate an expired worker lease with the controllable clock. Explain why a late successful transport response cannot rewrite the durable attempt evidence.

For each exercise, write one test that detects a meaningful failure. Avoid a test that merely repeats the implementation's expression in different syntax.

## Readiness checkpoints

You are ready for a larger change when you can explain a small one through user behavior, data, execution, failure, and evidence. Useful signs of growth are locating all writers of a field, spotting a missing ownership filter, distinguishing a revision from an operation ID, and explaining what remains uncertain after a timeout.

Choose one unfamiliar endpoint each week. Give a five-minute explanation without reading the lesson aloud. Then answer a counterexample: another owner, an exact expiry boundary, a stale revision, a duplicate request, or a database failure. This is a repeatable way to turn working code into engineering judgment.
