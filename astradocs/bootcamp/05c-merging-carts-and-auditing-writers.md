# Workshop 5c: two carts, one all-or-nothing change

Story: MS-11. [The tracker](story-tracker.md) records verification status. This workshop builds on wishlist copying and stock batches, but the merge rules are deliberately different.

## Work the four overlap cases

Target contains quantity 2 and source contains quantity 3 for the same variant:

| Target state | Source state | Combined state | Quantity |
| --- | --- | --- | ---: |
| Active | Active | Active | 5 |
| Active | Saved | Active | 5 |
| Saved | Active | Active | 5 |
| Saved | Saved | Saved | 5 |

One active copy is enough to make the combined line active. In Boolean terms, the combined line is saved only when **all** copies are saved. This explains the `All` predicate in [CartCombinationRules](../../src/Agora.Domain/Services/CartCombinationRules.cs).

Do the same calculation with target 60 and source 40. Quantity 100 violates the per-line maximum of 99, so neither cart changes. A merge is not a sequence of independent successful line additions.

## A proposal before a mutation

The service loads the source and target with current variants, products, and inventory inside a local transaction. Target must belong to the caller. Source may be unclaimed or belong to that same caller. Foreign carts return 404 before contents are returned.

[CartMergeService](../../src/Agora.Infrastructure/Services/CartMergeService.cs) builds a proposed representation before changing either cart. It validates both expected cart revisions, every combined quantity, one currency across all resulting lines, and current activity/stock for active lines.

Saved-only lines stay saved and do not need stock to become available merely to remain on the cart. Their currencies still participate in this bounded feature's single-currency rule. That rule also avoids the current subtotal mapper's first-line currency ambiguity; quantity-pricing work in SS-04 has its own planned mapping audit.

This is a useful distinction to explain aloud: “saved” means excluded from purchasing totals and active-stock checks; it does not mean excluded from every rule in every operation.

## Preserve target identity

Open `Cart.ReplaceContents` in [Cart.cs](../../src/Agora.Domain/Entities/Cart.cs). It validates all quantities and distinct IDs before changing line fields. For a variant already in the target, it reuses the existing CartItem and its ID. New variants receive new target line IDs. The source's lines are removed; their IDs do not move between parents.

Why retain target IDs? A caller may already be referring to a particular target line. Combining quantities does not require replacing that line's identity. The method sets the final quantity once, so activating a saved overlap does not accidentally add its quantity twice.

The first integration run found a related persistence trap: a merge introducing a new target line returned 409. Its SQL log showed a full-field CartItems UPDATE where this new child needed an INSERT. The service now remembers the original target line IDs and explicitly marks new lines Added. It does not infer “new” solely from an EF entry state after relationship discovery. The journal records the rerun result separately; a plausible fix still needs verification.

The merge applies the target proposal and clears the source using one captured time. Both cart versions advance once and both updates are saved together. The response includes the target representation, sourceVersion, and targetVersion; CartResponse now exposes its own version on ordinary reads too.

## Two versions means two opportunities for a conflict

Imagine you read source revision 4 and target revision 7. Another tab edits the source to revision 5 before your merge. Your operation must fail even if the target is still revision 7. Reverse the situation and the same rule applies to the target.

The controller's comparison provides an early conflict. EF's mapped concurrency token provides the conditional check when saving. If either conditional update fails, the transaction rolls back the other cart's changes and child-row deletions too.

[CartMergePersistenceTests](../../tests/Agora.Tests/Integration/CartMergePersistenceTests.cs) runs this experiment with independent database connections twice: once with a stale source and once with a stale target. The winning edit survives, and the losing merge leaves both aggregates otherwise intact.

## The writer audit matters as much as the new endpoint

A version only protects changes that advance it. The audit found two older paths worth following:

- Claiming an unowned cart assigned CustomerId directly without advancing Cart.Version. The new `Cart.Claim` operation advances it when ownership changes. Reclaiming your already-owned cart is an unchanged, idempotent read of that ownership.
- Deleting a product cascades through variants into cart items. The delete endpoint now advances every affected cart's parent revision, alongside the existing wishlist/collection revisions. It obtains a transaction before finding those parents so a concurrently added membership cannot slip between discovery and deletion.

This repeats the parent-membership lesson from wishlists in a new setting. Do not stop your audit at methods whose names contain “cart.” A catalog operation can change a cart indirectly through a database relationship.

## Retry is defined, not assumed

A successful merge empties the source. Repeating the original request with old revisions conflicts. Repeating it with the new revisions encounters an empty source and returns 422. It does not add the original quantities again.

Unlike a stock adjustment, a merge has no operation receipt. The client should reload both carts after an uncertain response. Do not invent idempotency-key guarantees that the endpoint does not provide.

Known SQLite lock contention is mapped only on explicitly marked local operations through [LocalSqliteWriteAttribute](../../src/Agora.Api/Filters/LocalSqliteWriteAttribute.cs). This is not applied as an automatic retry loop around checkout or external payment calls.

## Follow the tests in three passes

1. Read [CartCombinationTests](../../tests/Agora.Tests/Unit/CartCombinationTests.cs) and predict each Boolean overlap case without a database.
2. Read [CartMergeApiTests](../../tests/Agora.Tests/Integration/CartMergeApiTests.cs) and track the target IDs, saved lines, total quantity, and both revisions. Notice the zero-stock saved lines and mixed-currency rejection.
3. Read the persistence tests and explain what the fresh context must see after the stale merge fails. Do not inspect only the failed context's in-memory objects.

## Exercises and answers

1. Active target A=2 plus saved source A=3: **Active A=5.**
2. Saved target B=1 plus saved source B=2 with no stock: **Saved B=3; it is not activated.**
3. Target revision matches but source revision is stale: **409 with no merged changes.**
4. Why not remove target lines and recreate them all? **That would replace stable target identities unnecessarily.**
5. What unexpected writer can remove cart membership? **Product deletion through variant/cart-item cascades.**
6. Why must parent discovery and product deletion share a transaction? **Otherwise a new membership can appear after discovery and be deleted without its parent revision advancing.**
7. Does clearing the source reserve or restock inventory? **No; merging shopping intent does not change physical stock.**

For your journal, draw source and target as two boxes. Write the revisions above them and line IDs inside them. Apply one successful merge by hand, then replay it with one stale version. Describe the result first in plain language and then using the actual method names.
