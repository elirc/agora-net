# Module 3: ownership and small writes

[Bootcamp home](README.md) | [Module 2](02-queries-and-counterexamples.md) | [Journal](journal.md)

**Implementation status:** the junior ownership/validation changes are present and undergoing verification. This module does not claim that all legacy order-access concerns are already fixed; those have a separate senior story.

## A real ID is not permission

Imagine A and B both have address books. B learns the ID of A's home address. A query using only that ID would find it, but B is not entitled to receive it.

The new address lookup uses both predicates in [MeController.GetAddress](../../src/Agora.Api/Controllers/MeController.cs): requested address ID and authenticated customer ID. If either fails, the route returns 404. Even an admin visiting `/me/addresses/{id}` means “my address,” not “any customer's address.”

Say it another way: the ID selects a folder; the owner check decides whether this caller can open it. You need both. Random IDs being difficult to guess is not an ownership policy.

Read [Address_lookup_and_country_filter_use_customer_identity_without_rewriting_country](../../tests/Agora.Tests/Integration/BootcampJuniorApiTests.cs). It creates two actual owners and tests the existing foreign ID. A random unknown ID would only test missing data, not forbidden access to real data.

## Validation before mutation

The category-parent update must reject a missing parent before assigning the new name, slug, description, or parent to the tracked category. Why care if SaveChanges will not run on the rejection path? Because keeping validation separate makes the intended all-or-nothing boundary clear and prevents later refactors from accidentally persisting partially changed tracked state.

Trace the update in [CategoriesController](../../src/Agora.Api/Controllers/CategoriesController.cs). Find the missing-category, duplicate-slug, self-parent, and missing-parent checks. Then find the first assignment. Every rejected request should leave the stored record unchanged.

The current small fix does not detect longer ancestor cycles. Do not mentally promote a narrow passing test into a stronger promise. SS-02 later introduces graph validation and the transaction protocol needed across tree writes.

## Clear children, retain the parent

A wishlist is a named parent record. WishlistItems are its child entries. Clearing a wishlist is not deleting the wishlist itself.

```text
Before: Wishlist W (“Birthday”, default=true) -> items A, B
After:  Wishlist W (“Birthday”, default=true) -> no items
```

W keeps its identity/name/default flag. Clearing it again returns 204 because the desired state is already true. Removing the parent would violate the feature contract and break clients retaining its ID.

In [WishlistsController.ClearItems](../../src/Agora.Api/Controllers/WishlistsController.cs), ownership is checked while loading the parent. Only that parent's loaded items are marked for removal. Inventory, carts, orders, and other wishlists are separate resources and are not touched.

## Some existing reads have side effects

Wishlist list/detail behavior includes creating a default list or recording that an item was observed out of stock. The junior search/count features preserve those existing behaviors. This is a useful reminder that GET does not automatically mean the implementation is side-effect-free.

Locate GetOrCreateDefaultAsync and ToResponseWithObservationAsync. Explain why a filtered search may return an empty array while still ensuring a default exists. Later export/quote features must deliberately avoid these helpers when their contract promises no writes.

## Write failures need database assertions

Checking 422 is only half the category-parent test. The test opens a fresh context and compares all persisted fields with the original values. Similarly, a rejected shipping update must preserve name and rates, not merely return an error while silently changing a tracked record later.

Use this mini-table for any write:

| Outcome | HTTP assertion | Stored-state assertion |
| --- | --- | --- |
| Success | Expected status/response | Intended change exists after reload |
| Invalid input | Expected problem status | No new row or partial edit |
| Foreign owner | 404 or policy-specific denial | Owner's resource unchanged |
| Repeat clear | 204 | Parent still exists, children remain empty |

## Exercises and explanations

1. **B requests A's address by a real ID. Why 404 instead of returning the address then hiding its label?** The whole resource is private; masking one field does not authorize access to the rest.
2. **A clears a default wishlist twice. What survives?** The wishlist row, ID, name, and default status. Both responses are 204; its child collection remains empty.
3. **Why reload after a rejected write rather than inspect the same in-memory object?** A fresh scope observes committed database state and avoids confusing tracked changes with persistence.
4. **Does preventing a category from parenting itself prevent all cycles?** No. A -> B -> A is still possible without ancestor checks; the small story deliberately does not claim otherwise.
5. **A country query normalizes ` us ` to US. Must stored `us` be rewritten?** No. Normalize the comparison without creating an unexpected address-book mutation.

## Review checkpoint

For one endpoint, point to the exact owner predicate, the first mutation, the save boundary, and a negative test using another real owner. Explain what is deliberately outside the endpoint's scope. Those habits scale directly into cart merges, imports, holds, and background jobs.
