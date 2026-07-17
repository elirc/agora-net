# 8. Guests are first-class: cart tokens and order-email authentication

Status: Accepted

## Context

Accounts arrived in sprint 7, six sprints after carts and checkout. The
tempting simplification was to require an account for everything and delete the
guest paths. Forcing registration before checkout is also a well-known way to
lose the sale — the store should take money from someone who wants to give it.

But guests still need identity of *some* kind: a cart has to be re-findable
across requests, and a returns flow has to prove the requester owns the order.

## Decision

Keep every shopper-facing flow reachable without an account, using the
narrowest credential that works:

- **Carts are bearer tokens.** `POST /api/carts` mints an opaque
  `Guid.ToString("N")` token; holding it *is* the authorization. No auth header
  is required on any cart route.
- **Checkout takes an email**, not a session.
- **RMAs authenticate by order email**: `ReturnService.EnsureRequesterOwnsOrder`
  accepts either the owning account (`CustomerId` match) or the order's email,
  and throws `NotFoundException` — **404, not 403** — on a mismatch, so the
  endpoint never confirms that an order number exists to someone who cannot
  name its email.
- **Order numbers are unguessable** (`ORD-yyyyMMdd-<8 hex>`), which is what
  makes public `GET /api/orders/{number}` acceptable.

Accounts then *layer on* rather than replace: a signed-in `POST /api/carts`
attaches `CustomerId`; `POST /api/carts/{token}/claim` adopts a guest cart
(409 if another account already owns it); checkout attaches the order to the
account when the caller is authenticated; and `CustomerId` is what powers
`/api/me/orders` and `/api/me/returns`.

## Consequences

- Nobody has to register to buy, and a shopper who registers mid-session keeps
  the cart they already filled.
- Ownership checks answer 404 rather than 403 throughout (carts, addresses,
  wishlists, RMAs), so the API leaks no existence information. `AuthzMatrixTests`
  pins this across anonymous/customer/admin.
- **A cart token is a bearer credential in a URL path.** Anyone who obtains it
  holds the cart — it will sit in server logs, proxy logs and browser history.
  That is an accepted, bounded risk: a cart holds no money and no PII, and the
  blast radius of a leaked token is "someone edits your cart". Nothing that
  *does* hold money is reachable this way — order refunds/cancels work on
  unguessable order numbers, and RMAs additionally demand the order email.
- Guest orders have `CustomerId == null`, so `/api/me/orders` cannot show them
  retroactively; a guest who later registers with the same email does not
  inherit their past orders. Claiming carts is supported; claiming orders is
  not.
- Two identity paths exist in the return flow forever (account or email), which
  every ownership check has to honor. `EnsureRequesterOwnsOrder` is the single
  place that decides it.
