# 09f — Private account exports

An export is a deliberately designed API document, not a dump of entity objects. Version 1 includes profile contact fields, addresses, owned orders and items, their fulfillments and returns, owned wishlists and items, and reviews authored by the caller.

Think of the service as a customs desk. Every field needs a reason to cross the boundary. Password hashes, sessions, guest credentials, gift-card codes, transaction identifiers, webhook secrets, integration keys, internal notes, moderation notes, and other users' identities stay inside.

## Ownership repeated three ways

An order belongs in the export when `Order.CustomerId == callerId`. Matching email is irrelevant. A return belongs when its `OrderId` points to an owned order, even if a legacy return's own customer column disagrees. Children inherit export scope through the promised parent relationship.

In SQL-shaped language: caller → owned orders → order items, fulfillments, and returns. Caller → owned wishlists → wishlist items. Caller → authored reviews.

In plain language: follow stored ownership links, never look for text that happens to resemble the customer's email.

## Snapshot and budgets

All counts and projections run inside one read transaction. Without that boundary, the count phase could approve 9,999 records and a concurrent insert could make the load phase return 10,001 from a different moment.

The budget is global: profile plus every row in every section must be at most 10,000. Counts happen before large payload loading. The explicit projections are then serialized through a bounded memory stream that rejects the write which would exceed 5 MiB. Only a complete, accepted buffer reaches the attachment response.

Repeat the sequence: count → project → serialize while enforcing the byte cap → respond. Buffering the complete bounded document before sending it allows a clean 422 on overflow. Sending bytes to the client first would make a later 422 impossible because part of the private document would already be on the wire.

The response uses `application/json`, a server-generated `agora-account-export-YYYYMMDD.json` attachment name, and `Cache-Control: private, no-store`. There is no public download URL, background job, email, or retention store in this bounded implementation.

## Why explicit records matter

Each `Export...` record is a field allowlist. Adding a secret property to an EF entity later cannot silently add it to version 1. Reusing `OrderResponse` would be dangerous because it historically carried gift-card and payment references.

Wishlist rows are projected directly with `AsNoTracking`. The service does not call wishlist presentation helpers that update stock-observation flags. A data export is a read and must remain a read.

## Exercises

1. A guest order uses the caller's email. Is it included?
2. A legacy return has a wrong CustomerId but belongs to the caller's order. Is it included?
3. Why count all sections together?
4. Why serialize before returning `File(...)`?
5. Why is `PasswordHash` absent even though it is associated with the account?
6. What permits version 2 to add a section safely?

## Answers

1. No; email is not ownership.
2. Yes; order ownership governs return inclusion.
3. A per-table cap could still create an enormous combined response.
4. The byte cap must reject with no partial download.
5. Association is not a disclosure purpose; password material is never portable profile data.
6. A new explicit version contract lets consumers choose how to handle the changed schema.

## Explain it back

Explain the export as an allowlist, then as an ownership graph, then as a bounded snapshot algorithm. If all three explanations agree, you understand why this is safer than serializing a customer entity graph.

Journal: Which excluded field was easiest to accidentally include? Which negative ownership test proves the most? What future section would require version 2 rather than silently changing version 1?
