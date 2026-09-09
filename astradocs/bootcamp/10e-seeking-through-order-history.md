# Workshop 10e: seeking through order history

Story: SS19. Start with [history and repeat purchases](05b-history-and-repeat-purchases.md), then return here when page-number pagination feels familiar. Check the [tracker](story-tracker.md) for verification status.

## The problem in ordinary words

Imagine a customer with thousands of orders. A page-number query asks the database to walk past earlier rows before returning the next page. It also makes the meaning of page two depend on how many rows currently precede it. Inserting or deleting rows can move those positions.

The order feed carries a bookmark instead. It says: continue after this particular creation time and order number, using the same customer, page size, and initial cutoff. The bookmark is protected against modification.

The endpoint is `GET /api/me/orders/feed?limit=25`. The customer must sign in. A response contains `items`, `hasMore`, and `nextCursor`. When `hasMore` is true, send the returned cursor unchanged with the same `limit`.

## The same idea as a small example

Five orders share one creation timestamp. Their numbers are A, B, C, D, and E. We sort newest timestamps first and, within a tie, numbers in descending binary order.

| Request | Returned numbers | Last returned number | Has more |
| --- | --- | --- | --- |
| First, limit 2 | E, D | D | true |
| Cursor after D | C, B | B | true |
| Cursor after B | A | A | false |

The first query actually reads three keys: E, D, and C. C proves another page exists. The cursor must describe D, the last **returned** row. If it described C, the next request would skip C.

Try covering the last column. Explain why the final page does not need a separate count query.

## The same idea as a predicate

Let the bookmark contain `(lastTime, lastNumber)`. A remaining row must satisfy:

```text
row.CreatedAt < lastTime
OR
(row.CreatedAt == lastTime AND row.Number < lastNumber)
```

Both the comparison and ordering use SQLite `BINARY` collation for the number. A tie-breaker is necessary: creation time alone would skip orders that share a timestamp. A consistent collation is necessary: a bookmark created under one ordering cannot safely be resumed using another ordering.

Every query also requires `CustomerId == signedInCustomer` and `CreatedAt <= initialCutoff`. These filters remain in place on every page.

## Trace the code slowly

1. Open [the controller](../../src/Agora.Api/Controllers/OrderHistoryFeedController.cs). Find how the authenticated customer ID reaches the query. The cursor never decides who is signed in.
2. Open [the cursor protector](../../src/Agora.Api/Queries/OrderHistoryCursorProtector.cs). List its fields: format version, customer, cutoff, last key, page size, and expiry.
3. Find the length bound before unprotection. This limits the encoded input before parsing protected content.
4. Find the owner, page-size, and expiry checks. A cryptographically intact token can still be invalid for this particular request.
5. Open [the query](../../src/Agora.Api/Queries/OrderHistoryFeedQuery.cs). Locate the initial cutoff and the seek predicate.
6. Find `Take(limit + 1)`. The extra row establishes `hasMore`; it is not returned.
7. Follow the second, batched order load. It retrieves the items needed for the selected orders without a per-order query loop.
8. Find the final cursor construction. Check that it preserves the original cutoff and expiry rather than extending either on every page.
9. Open [the database mapping](../../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Find `IX_Orders_CustomerId_CreatedAt_Number` and compare its columns with the filters and sort order.

Pause after step five. Say the query in English before reading the rest: “My orders, no newer than when I began browsing, after the last order I already saw.”

## A cutoff is not a frozen database

Suppose browsing begins at 10:00. An order created at 10:01 is excluded from this traversal. A new traversal can see it.

However, a backdated order inserted later with a creation time before 10:00 can appear on a later page if its key is after the bookmark. Existing orders may also be updated or deleted between HTTP requests. The transaction gives each individual response a consistent database view; it does not remain open between requests.

This distinction matters when explaining an API guarantee. The feed provides stable seek boundaries and an initial time cutoff. It does not claim a historical snapshot of every field across an entire browsing session.

## Protection, ownership, and persistence

The cursor is opaque application state, not a login credential. The endpoint still requires normal authentication and filters orders by the authenticated owner. Copying another customer's cursor fails with the same generic invalid-cursor response as a malformed cursor.

ASP.NET Data Protection protects the serialized cursor. Production hosts persist their key ring in `DataProtection:KeyDirectory`, defaulting to `data-protection-keys` under the content root. Keep this directory durable across restarts. Multiple instances serving the same application need the same protected key ring and application name. Protect access to that directory; it contains cryptographic key material and is ignored by Git.

Testing hosts use an isolated ephemeral provider. A cursor from another test host therefore does not become valid merely because its JSON would otherwise describe the same customer.

Keys and cursor lifetime solve different problems. A retained key makes an old token decryptable. The explicit 24-hour expiry still makes that token unacceptable at the boundary. Advancing the cursor does not renew that lifetime.

Responses use private, no-store cache headers because they contain customer order history. Errors deliberately avoid explaining whether ownership, integrity, or expiry caused rejection.

## Verify the behavior rather than the method name

Read [the API tests](../../tests/Agora.Tests/Integration/OrderHistoryFeedApiTests.cs) and [the protection tests](../../tests/Agora.Tests/Unit/OrderHistoryCursorTests.cs).

The tests traverse timestamp ties in pages of 2, 2, and 1; insert a newer order after the cutoff; remove a previously returned row; reject tampering, another owner, changed limits, and exact expiry; and demonstrate that a backdated insertion can join later pages.

The SQL checks reject `OFFSET` and a total `COUNT(*)`. An `EXPLAIN QUERY PLAN` example uses 2,000 orders and verifies the intended composite index without a temporary sorting tree. That is evidence about the query shape and index choice, not a universal latency benchmark.

The protection test recreates a provider over the same temporary key directory. The same application can read its old cursor; another application or an unrelated ephemeral key cannot. This is a small, direct demonstration of restart behavior.

Run the relevant tests from the repository root using the installed .NET SDK:

```powershell
dotnet test tests/Agora.Tests/Agora.Tests.csproj --filter "FullyQualifiedName~OrderHistoryFeed|FullyQualifiedName~OrderHistoryCursor"
```

## Exercises with answers

**1. Remove the order-number tie-breaker in your mental model.** Three orders share a timestamp and the first page returns two. What happens next?

Answer: seeking strictly older timestamps skips the third tied order. A complete ordering key prevents that loss.

**2. Set the next cursor to the extra lookahead row.** Which row disappears in the table above?

Answer: C disappears, because the next request begins strictly after C even though the first response never returned it.

**3. Recalculate the cutoff on every request.** What promise changes?

Answer: the browsing window can now admit later-created orders. Keep the first cutoff inside the protected cursor to preserve the original upper time boundary.

**4. Extend expiry by 24 hours for every new cursor.** Why is that different?

Answer: repeatedly browsing could keep one traversal alive indefinitely. Preserving the first expiry bounds the entire traversal.

**5. Decode a cursor successfully, then skip the owner check.** Is normal route authentication enough?

Answer: the SQL owner filter still prevents another customer's rows from being returned, but the cursor contract is broken: one customer's bookmark can influence another customer's traversal. Keep both resource filtering and cursor binding.

**6. An order is inserted later with an older creation time. Must it be invisible?**

Answer: no. This design is not a retained database snapshot. Its creation key determines whether it can appear after the current bookmark.

## Explain it back

Give three explanations without copying this page: a bookmark analogy, the two-part seek predicate, and a request-to-query trace. Then explain one limit of the guarantee. Being able to name that limit is part of understanding the implementation.
