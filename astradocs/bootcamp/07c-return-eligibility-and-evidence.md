# Workshop 7c: return eligibility and supplementary evidence

Stories: MS-23 and MS-24. [Tracker](story-tracker.md) | [Journal](journal.md) | [Shared quote calculations](07a-quotes-and-shared-pricing.md)

An eligibility preview explains what a customer can currently request. Creating a return enforces the rules. Evidence links add context to a return that already exists. Keeping those jobs distinct prevents a friendly preview or an extra attachment from accidentally becoming a refund decision.

## First understand the quantities

An order contains five tees. One is in a Requested return, two are in an Approved return, and one is in a Rejected return. How many can still be requested?

```text
Purchased quantity                    5
- Requested quantity                  1
- Approved quantity                   2
= Remaining quantity                  2

Rejected quantity does not consume capacity.
Cancelled quantity does not consume capacity.
```

Requested quantities count because they are already tied up in an open request. Approved quantities count because their returns were accepted. Rejection/cancellation releases that claim. This is a capacity rule, not a sum of every historical RMA line.

Say it another way: you cannot submit the same purchased unit in two live requests merely because neither has been approved yet.

## Then understand the estimate

Our worked order has five units at 20.00 each: subtotal 100.00, order discount 10.00, and tax 7.20 on the discounted 90.00. Its effective discount is ten percent; effective tax is eight percent. Returning two units estimates:

```text
20.00 × 2 × (1 - .10) × (1 + .08) = 38.88
```

The implementation preserves the existing return formula: use order-effective discount and tax ratios, calculate each requested line, and round to cents away from zero. Shipping is not included in this line estimate. This is not a reconstruction of original per-category tax on each item; those historical item-level tax amounts are not stored in this model. Read the existing formula before trying to improve its accounting semantics.

[ReturnEligibilityRules.EstimateRefund](../../src/Agora.Domain/Services/ReturnEligibilityRules.cs) is shared by preview and creation. A preview estimates all remaining units per line. Creating a request estimates the quantities actually requested. One unit in this example estimates 19.44, not the two-unit preview's 38.88.

## A window is another condition, not a replacement rule

`ReturnPolicy.WindowDays` is null by default, preserving the prior absence of a deadline. When configured, it must be 1–365 and the app validates it at startup. Example opt-in configuration:

```json
"ReturnPolicy": { "WindowDays": 30 }
```

With a configured window, the deadline is `FulfilledAt + WindowDays`. A new request is allowed only when the captured current instant is strictly before that deadline. Exact expiry is too late. A configured policy and missing FulfilledAt produce `MissingFulfilledAt`; the code does not invent a completion date from order creation or shipment tracking.

| Fact | Consequence |
| --- | --- |
| Order is not fully Fulfilled | OrderNotFulfilled |
| Window enabled, no completion timestamp | MissingFulfilledAt |
| Current instant equals or exceeds deadline | ReturnWindowExpired |
| No remaining line quantity | NoRemainingQuantity |
| All rules hold and some quantity remains | Eligible |

An unrepresentable computed deadline produces InvalidFulfilledAt. The ordinary case uses valid persisted timestamps, but malformed historical data must not make the API invent a date.

The line quantities and estimates remain visible when the window is closed. They explain the arithmetic; the top-level Eligible flag and reasons control whether a new request is currently allowed. An estimate is not a refund instruction.

## Trace preview and creation side by side

Authenticated GET `/api/me/orders/{number}/return-eligibility` loads an order using both number and actual CustomerId. A guest order with the same email is not owned through this route. Its response contains evaluatedAt, nullable deadline, eligible, reasons, currency, and per-line order-item ID/SKU/purchased/remaining/estimated refund.

Read [ReturnEligibilityController](../../src/Agora.Api/Controllers/ReturnEligibilityController.cs), then [ReturnEligibilityService](../../src/Agora.Infrastructure/Services/ReturnEligibilityService.cs). The service groups only Requested and Approved quantities using a widened sum, calculates remaining quantities, and invokes the pure rules. The preview does not save or refund anything.

Now open [ReturnService.CreateAsync](../../src/Agora.Infrastructure/Services/ReturnService.cs). It preserves the existing command's requester authorization, checks fulfilled status, validates requested line shape, and calls the same eligibility service. It then verifies each requested quantity and calls the same refund estimator. The deadline is enforced here too. A client bypassing the preview still cannot submit an expired new request.

Creation begins a short local write transaction before reading quantities and inserting the new request. Two requests competing for the same final units are serialized. One can consume the remaining capacity; the next sees it consumed and fails. This section makes no external calls, so it does not hold a database transaction across a payment provider.

## Submission time and approval time answer different questions

A return submitted one tick before expiry was valid when created. Support can approve that existing request after expiry. Approval does not call the new-submission eligibility gate again.

Think of a form submitted before a deadline but reviewed the next day. The reviewer being busy should not retroactively make the submission late. The test moves a fake clock across the deadline; it never sleeps for time to pass.

[ReturnEligibilityApiTests](../../tests/Agora.Tests/Integration/ReturnEligibilityApiTests.cs) checks the 5−1−2 example, one tick before/exactly at/after expiry, late approval, disabled policy, missing timestamp, partial fulfillment, and ownership. It verifies preview SQL contains no writes and provider counters remain unchanged. Startup tests reject zero and 366 days. [OperationalHistoryPersistenceTests](../../tests/Agora.Tests/Integration/OperationalHistoryPersistenceTests.cs) coordinates competing creation requests on separate connections.

## Evidence is a link, not a refund trigger

Authenticated account owners can GET/POST `/api/me/returns/{number}/evidence` and DELETE `.../evidence/{id}`. Ownership is determined through the linked order's CustomerId, not matching email. Administrators can inspect GET `/api/admin/returns/{number}/evidence`.

An evidence record contains a server ID, absolute HTTPS URL up to 2,000 characters, optional description up to 200, author account ID, and server timestamp. URLs require a host and reject user-info credentials such as `https://user:password@example.test/image`. The API stores the supplied reference. It does not upload, fetch, authenticate to, scan, or verify the external content.

Up to five links may exist per return. POST serializes the count and insert in one local write section; a sixth returns 409. DELETE includes both the return and child ID, so a child from another return cannot be deleted through this route.

Evidence is allowed in any return state. A photo link added after approval is supplementary context, and its later timestamp makes that visible. It does not reopen the request, adjust the estimate, refund again, or alter stock. The evidence collection has no new return-state concurrency token: protecting this small independent collection should not silently change the semantics of existing refund saves.

## Read the evidence feature in four passes

1. In [ReturnEvidence](../../src/Agora.Domain/Entities/ReturnEvidence.cs), identify URI and description validation. There is no HTTP client.
2. In [ReturnEvidenceController](../../src/Agora.Api/Controllers/ReturnEvidenceController.cs), underline the order-owner predicate, transaction start, cap check, and scoped delete.
3. In [AgoraDbContext](../../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs), find the return FK and cascade. Deleting a return deletes its evidence records.
4. In [ReturnEvidenceApiTests](../../tests/Agora.Tests/Integration/ReturnEvidenceApiTests.cs), follow an already approved request through add/list/delete. Refund amount, status, processed timestamp, refund transaction, and provider counters must remain unchanged.

The persistence test begins with four links and races two additions. Exactly one creates the fifth and the other receives a conflict. Merely checking five sequential inserts would not establish that guarantee.

## Practice questions

**A preview says two units remain. Another request consumes one before you submit two. What happens?** Creation rereads current capacity and rejects the oversized request. A preview is informative, not a reservation.

**A rejected return contains one unit. Does it reduce remaining quantity?** No. Requested and Approved are the consuming states.

**The user uploads nothing but posts a URL. Has the API verified that the URL is a photo?** No. It validated the URL shape and stored a reference.

**An administrator approves a return, then the customer adds another evidence link. Must approval run again?** No. Evidence has an independent lifecycle.

**Why keep actual ownership on the new read/evidence routes even if the old return command has a guest email path?** Authorization is part of each feature's contract. Matching an email is not actual account ownership. The later guest-credential story addresses the older guest flow separately.

In your journal, draw three boxes: eligibility calculation, RMA lifecycle, evidence collection. Label which operation changes which box. Then teach the same scenario as a shopper explanation and as a request/database trace. Consult the tracker for verification status rather than assuming that a written lesson means its tests have finished.
