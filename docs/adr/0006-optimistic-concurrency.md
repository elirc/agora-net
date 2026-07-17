# 6. Optimistic concurrency on stock, carts and gift cards only

Status: Accepted

## Context

Checkout already guards stock logically: `InventoryItem.Reserve` throws when
`QuantityAvailable < quantity`. That check is correct and it is not enough.
Two concurrent checkouts can each read the same row showing one unit available,
each pass their own check against their own snapshot, and each write a
reservation — the classic read-modify-write race. Last write wins, one unit is
sold twice, and nothing anywhere reports an error.

The same shape applies to a gift-card balance drawn by two checkouts at once,
and to interleaved cart edits racing a checkout that is clearing the cart.

## Decision

Add an `int Version` concurrency token to exactly the rows where a lost update
costs money or goods, bumped by every domain mutation and mapped with
`.IsConcurrencyToken()`:

| Row | Protects against |
| --- | --- |
| `InventoryItem` | two checkouts reserving/committing the same units |
| `Cart` | interleaved cart edits, and cart-vs-checkout races |
| `GiftCard` | double redemption of the same balance |

EF Core puts the token in the `WHERE` clause, so the losing writer updates zero
rows and `SaveChangesAsync` throws `DbUpdateConcurrencyException`, which
`DomainExceptionFilter` maps to **409** ("Concurrency conflict"). Clients retry
by re-reading.

Everything else — products, categories, discounts, shipping methods, tax zones,
webhooks — has no token. Those are admin-authored config where a lost update
means "the second admin's edit won", which is both rare and survivable.

The version token is the **backstop**, not the primary check. The logical guard
still runs first and produces the better error (`InsufficientStockException` →
409 "Insufficient stock"); the token only catches the genuinely interleaved
case that the guard cannot see.

## Consequences

- The last unit of stock cannot be sold twice, and a gift card cannot be
  redeemed twice against one balance. `ConcurrencyEdgeTests` drives both races
  from two `DbContext` snapshots and asserts the second writer fails loudly.
- Optimistic, not pessimistic: no locks are held across the payment gateway
  call, which matters because that call is slow and external (ADR-0003).
- Clients must be able to handle a 409 on checkout and retry. That is a real
  burden pushed onto callers, and the reason the 409 is documented with a
  distinct title.
- Two 409s now mean different things — "insufficient stock" and "concurrency
  conflict" — distinguished by ProblemDetails `title`, not status code.
- Adding a money- or stock-bearing row means deciding about a token. The table
  above is the checklist.
