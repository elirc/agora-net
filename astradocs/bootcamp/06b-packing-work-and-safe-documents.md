# Workshop 6b: stock, packing work, and a safe printable document

Stories: MS-22 and MS-27. See [the tracker](story-tracker.md) for actual completion status and [the journal](journal.md) for test and visual evidence.

## Two questions that sound similar

“Can I sell two units?” is an inventory question. “How many units from this paid order still need shipping?” is a fulfillment question. They use different records and different arithmetic.

| Question | Equation | Records |
| --- | --- | --- |
| How much stock can another checkout reserve? | On hand minus reserved | InventoryItem |
| How much packing work remains on an order line? | Ordered minus fulfilled | OrderItem and FulfillmentItem |

Read [InventoryItem](../../src/Agora.Domain/Entities/InventoryItem.cs), then [Fulfillment](../../src/Agora.Domain/Entities/Fulfillment.cs). Checkout commits the stock reservation after successful payment. Shipping the paid order later must not deduct that same stock again.

Say it another way: the store has already allocated the purchase when payment completes; the warehouse still needs to put the purchased goods into a shipment. An empty current shelf count does not erase that warehouse task.

## Work a partial order by hand

Order A has five mugs. Shipment X contains two mugs and shipment Y contains one. Fulfilled is 2 + 1 = 3; remaining is 5 - 3 = 2. A second line has one book and one shipped book, so its remaining quantity is zero.

The queue includes A with only the mug line. The packing slip includes both lines, including the fully covered book. These are deliberate differences: the queue lists outstanding work; the document describes the whole order's packing quantities.

If a bad legacy record says six mugs shipped, 5 - 6 = -1. Neither endpoint silently turns that into zero. It returns a conflict describing inconsistent fulfillment quantities, so incorrect data cannot appear as trustworthy packing instructions.

## Read the queue in this order

Open [FulfillmentQueueController](../../src/Agora.Api/Controllers/FulfillmentQueueController.cs).

1. Locate administrator authorization and filter validation. A supplied paid-date interval needs both endpoints, must increase, and spans at most 90 days.
2. Find the status predicate: Paid or PartiallyFulfilled. No stock predicate appears.
3. Find the correlated shipment sum. It asks for the total for one order-item ID, so multiple shipments cannot multiply unrelated line quantities.
4. Find the positive-remaining existence predicate before `CountAsync`, `Skip`, and `Take`.
5. Follow the bounded order page into the batched line query and lookup. There is no one-query-per-order loop.
6. Check the response model: historical names and shipping method, plus ordered/fulfilled/remaining quantities.

The count and result queries run inside a read transaction so one response has a consistent database view. The queue is still an observation, not a reservation or a warehouse assignment. A later shipment can change the next response.

## The page-before-filter trap

Imagine ten old paid orders whose lines are already fully covered, followed by one order with work remaining. If you take the first ten and then remove fully covered orders, page one is empty even though work exists. Counting all paid orders also produces a misleading total.

The implementation asks “does this order have positive remaining work?” before both counting and pagination. An integration fixture deliberately leaves a fully covered order in Paid state to prove that status alone is insufficient.

Equal paid timestamps need a second ordering key. Order ID makes pagination deterministic for a fixed database state. Offset pages can still shift between separate requests when data changes; SS-19 explores cursor-based navigation for a different use case.

## Why an old order must use old values

Open [OrderItem](../../src/Agora.Domain/Entities/OrderItem.cs) and [Order](../../src/Agora.Domain/Entities/Order.cs). They hold names, SKU, address, and other values captured when the purchase was made.

Suppose a customer buys “Blue mug,” then a catalog administrator renames the product “Ocean mug.” The packing slip should still describe the purchased snapshot. The same reasoning applies when a customer later edits an address-book entry: the original order already has its own shipping address.

Try explaining the distinction twice: first as “a receipt remembers the purchase,” then as “the query projects OrderItem and the order-owned ShippingAddress rather than joining live product or address-book values.”

## Make the renderer's input small

[PackingSlipsController](../../src/Agora.Api/Controllers/PackingSlipsController.cs) loads only the needed header and line fields. It reads up to 501 lines: the extra line is a sentinel proving that the 500-line limit was exceeded. It returns 422 for that case rather than silently printing an incomplete order.

[PackingSlipRenderer](../../src/Agora.Api/Rendering/PackingSlipRenderer.cs) receives a dedicated model containing only operational fields. There is no price, payment transaction, gift-card code, customer ID, or email field for the renderer to accidentally print. Administrator authorization and `private, no-store` caching accompany this address-bearing document.

The model is a boundary you can review quickly. Passing an entire tracked order would make it harder to see which information the document is allowed to expose.

## Encoding is a context decision

Every dynamic string goes through `HtmlEncoder.Default` into an HTML text node. Static markup controls the table and print CSS. Even the order number and address fields are encoded; do not assume that a friendly field name makes stored text safe.

Example input: `<script>old item</script>`.

Encoded output: `&lt;script&gt;old item&lt;/script&gt;`.

The browser displays the characters as a name instead of creating a script element. Dynamic strings are never inserted into CSS, script, URL attributes, or raw HTML. Those contexts would require separate design decisions; a text-node encoder is not a universal sanitizer.

## Verify values and physical layout separately

Read [PackingSlipApiTests](../../tests/Agora.Tests/Integration/PackingSlipApiTests.cs) and [FulfillmentQueueApiTests](../../tests/Agora.Tests/Integration/FulfillmentQueueApiTests.cs). Predict each result before running the test. Important counterexamples include multiple partial shipments, a fully covered Paid order, zero current stock, exact paid-date boundaries, forbidden callers, 501 lines, and over-fulfillment.

The renderer test checks encoding and print-style declarations. These checks cannot establish legibility. A browser print inspection must also examine a short document, long names and addresses, and a table spanning multiple pages. The journal records what was actually inspected; a planned check is not evidence.

The first visual inspection found exactly this kind of gap: all 25 selected tests passed, but the word Remaining split its final letter onto a second line in the printed header. The long document repeated that defect on every page. This is a useful counterexample to “the tests passed, so the document looks good.” Column sizing needs a visual check; do not add an assertion that merely copies the chosen CSS percentage and pretend it proves legibility.

## Exercises with answers

1. Five ordered, two separate shipments of one each: what are fulfilled and remaining? **2 and 3.**
2. Current inventory is zero but a paid order has three remaining: does it belong in the queue? **Yes; inventory is a separate question.**
3. A fully covered line on an otherwise partial order: where does it appear? **On the packing slip, not among positive-work queue lines.**
4. Why fetch a 501st line when the document permits only 500? **To distinguish exactly 500 from a truncated larger order.**
5. Does `no-store` replace administrator authorization? **No; caching policy and access control address different concerns.**
6. Why encode a SKU that your current product-creation form validates? **Legacy data and other writers may differ; output handling should be correct for every stored string.**

## Your journal entry

Draw checkout and fulfillment on two separate timelines. Place Reserve and CommitReservation on checkout; place shipment creation on fulfillment. Write both quantity equations underneath. Then trace one order from database snapshots through the packing-slip model into an encoded table row. Finish with one counterexample you initially missed and the test that would catch it.
