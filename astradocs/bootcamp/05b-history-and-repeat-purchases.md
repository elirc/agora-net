# Workshop 5b: remembering an order and buying again

Stories: MS-20 and MS-21. Follow [the tracker](story-tracker.md) for verification status. These two features use the same historical order for different purposes.

## One order, two questions

A customer bought two blue mugs for 15.00 each. Payment was recorded on Monday, the first shipment on Tuesday, the last shipment on Wednesday, and a return was approved Friday. The live mug price is now 18.00.

The timeline asks “what milestones were recorded for this purchase?” Repeat purchase asks “can I put the same variant identities into a new cart now?” The timeline uses recorded timestamps; the new cart uses current catalog prices and availability.

| Value | Timeline | New repeat-purchase cart |
| --- | --- | --- |
| Original paid timestamp | Show it if stored | Do not copy |
| Original shipping address | Do not expose here | Do not copy |
| Historical variant ID | Not needed for milestones | Use to locate the same variant |
| Original 15.00 unit price | Not needed for milestones | Do not authorize it as today's price |
| Current 18.00 unit price | Not part of history | Use the current variant value |
| Current stock | Does not rewrite history | Validate without reserving |

Say it in everyday language: a timeline remembers what happened; a new cart expresses what the customer wants to do next.

## Account ownership comes first

Both routes start under `/api/me/orders/{number}`. They require a real authenticated customer ID and compare it with `Order.CustomerId` in the initial lookup. An order number or matching email is insufficient. Another customer's order and a guest order both return 404.

Read the first database query in [OrderTimelinesController](../../src/Agora.Api/Controllers/OrderTimelinesController.cs) and [OrderReorderService](../../src/Agora.Infrastructure/Services/OrderReorderService.cs). Find the owner predicate before following any related data. An administrator does not automatically become the owner on these account routes.

This repeats the ownership lesson from private wishlist notes: attach the caller's identity to the database predicate, rather than loading arbitrary private data and hoping the response mapper removes enough information.

## The timeline contains evidence, not reconstructed state

The order has CreatedAt and nullable PaidAt, FulfilledAt, CancelledAt, and RefundedAt. Shipments have CreatedAt. Returns have CreatedAt and a single nullable ProcessedAt. There is no complete transition log hidden behind those fields.

If a legacy order says Fulfilled but its FulfilledAt is null, the timeline does not manufacture a fulfillment time. If a return has an Approved status but no ProcessedAt, the approval milestone is unavailable. The endpoint still includes other real timestamps.

Two different facts can share one time. The final shipment's creation and the order becoming fully fulfilled remain separate entries. Each has a stable key, such as `fulfillment-created:{id}` or `order-fulfilled:{id}`. Sorting by timestamp then ordinal key makes tied results deterministic.

## Why the merge can stay bounded

Suppose a caller requests page 3 with 20 entries per page. The final offset is 40, so the endpoint needs the global first 60 candidates. It only needs the first 60 entries from each source, plus the fixed order milestones.

Why? A source's 61st entry already has 60 entries from that same source ahead of it. It cannot be among the global first 60.

The implementation separately counts all matching shipment/return sources, fetches only the necessary ordered prefixes, combines them, and takes the final page. A maximum offset of 10,000 prevents callers from turning deep paging into unbounded application loading. The source queries and counts share a read transaction for an internally consistent response.

Rephrase the algorithm without code: ask each sorted pile for only as many cards as could possibly matter, merge those cards, then select the requested slice. Count the complete piles separately so the reported total is not merely the number of cards you picked up.

## Repeat purchase follows identity, then current rules

The reorder service first groups historical lines by ProductVariantId and sums quantities. It supports 1–50 distinct variants, with each combined quantity between 1 and 99. It then batch-loads current variants, products, and inventory.

A variant that keeps its ID but changes SKU is still the same variant for this operation. A deleted variant does not become the same item merely because somebody creates a new variant using its old SKU. Failure details retain a historical SKU to help the customer identify the unusable line.

All proposed lines must be active purchases, share one currency, and have sufficient available stock. The operation validates the whole proposal before creating the cart. If one variant is missing, the valid subset is not saved.

After validation, the service creates a new owned Cart, adds lines through its domain methods, and saves once. Current catalog objects are attached for response mapping only after persistence, avoiding accidental insertion of an untracked catalog graph with the new cart.

## A new cart is not a reservation

The response shows current prices and observed stock suitability. Stock can change afterward. Checkout still revalidates and reserves through its existing workflow.

The original order remains unchanged. Payment details, discounts, gift cards, delivery addresses, and historical status are not copied into the new cart. Pending orders are rejected, but cancelled/refunded historical purchases can be used as shopping references.

Repeated successful reorder requests intentionally produce different cart tokens. Compare this with [stock-adjustment replay](06d-atomic-stock-and-replay.md): there, repeated submission of the same operation ID recovers one saved outcome. Here, the contract explicitly asks for a fresh cart each time. “Retry behavior” is part of a feature's contract, not a universal property of POST.

## Follow the experiments

[OrderTimelineApiTests](../../tests/Agora.Tests/Integration/OrderTimelineApiTests.cs) checks exact event keys, equal timestamps, page boundaries, missing optional timestamps, ownership, private-field exclusion, and a bounded number of read commands.

[OrderReorderApiTests](../../tests/Agora.Tests/Integration/OrderReorderApiTests.cs) performs a real fake-gateway checkout at 15.00, edits the live variant to 18.00 with a changed SKU, and verifies a new cart at 18.00 while the original order stays at 15.00. It also covers reused SKUs with different IDs, unusable subsets, stock/currency/quantity limits, cancelled history, and repeated successful creation.

Before running each test, predict the HTTP status and the number of carts/order changes it should leave behind. Then trace the code to find the exact point that makes your prediction true.

## Exercises and answers

1. Final shipment and full fulfillment share a timestamp. How many timeline entries? **Two, because they describe different recorded facts.**
2. A fulfilled legacy order has no fulfillment timestamp. Should the API use UpdatedAt? **No; that would invent evidence.**
3. A guest order's email matches the account. Can this account timeline read it? **No; these routes require the stored account owner ID.**
4. Old SKU X is reused by a new variant. Does reorder substitute it? **No; historical variant identity must match.**
5. One of two variants is unavailable. How many carts are created? **Zero.**
6. Why can the repeat cart cost more than the original order? **It uses today's prices; the original price remains an immutable purchase snapshot.**
7. Page offset 40, size 20: how many candidates per ordered source are sufficient? **At most 60.**

Draw the two requests side by side in your learning log. Label every value as historical evidence, current catalog observation, or new shopping intent. Explaining those categories is more useful than memorizing the controller names.
