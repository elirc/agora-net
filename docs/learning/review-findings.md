# Learning-focused repository review

This is a scoped code review, not an exhaustive production audit. Findings below name the evidence and distinguish implemented improvements from proposed work. Use them to practice prioritization.

| Priority | Finding and evidence | Status and learning value |
| --- | --- | --- |
| High | Product listing used separate `Any` predicates for minimum and maximum prices; different variants could satisfy them | Fixed in `ProductCatalogQuery`; regression in `CatalogSearchApiTests.PriceRange_NoSingleVariantInsideRange_ExcludesProduct`. Learn predicate logic and counterexample tests |
| Medium | Product paging sorted only by non-unique names, prices, or creation dates, and offset multiplication could overflow | Product listing now adds ID ordering and validates widened offset arithmetic. Other paged routes remain L2 |
| Medium | Catalog search treated user `%` and `_` as SQL LIKE wildcards | Search now escapes LIKE metacharacters. This is a search-semantics fix, not a claim that the previous parameterized query was SQL injection |
| Learning | Product listing mixed numerous HTTP inputs, validation, and SQL composition | Split into request contract, query helper, and controller execution; availability and currency features provide a small extension example |
| Learning | The sample HTTP file still referenced `/weatherforecast/` | Replaced with runnable catalog and validation requests |
| High | `OrdersController.GetByNumber`, `Cancel`, and `Refund` do not enforce caller identity or ownership | Open: L5. Order numbers currently grant substantial access. Existing role checks elsewhere do not cover these routes |
| High | `CheckoutService` calls the gateway between durable saves, with cleanup for declines but no durable reconciliation for an accepted charge followed by failure | Open: L6. Concurrency tests do not establish safety across external side effects |
| High | `WebhookService.DispatchAsync` sends before saving deliveries, after checkout has already persisted paid state | Open: L7. A process interruption can lose notification intent or its attempt record |
| Medium | `AdminReportsController.Sales` materializes matching orders/items before aggregation; `TopProducts` lacks the same reversed-range check as `Sales` | Open: L4. Bound workload and clarify date semantics before performance refactoring |
| Medium | `Program` enables request-path logging; some guest credentials appear in routes | Open design review in L5 and the security lesson: useful logs should not become a credential store |

## Intentional remaining catalog limits

Price sorting uses the minimum price across all product variants, preserving the existing contract. Currency filtering does not convert currencies. A product can match both availability values via different variants. Count and page are separate reads, so concurrent catalog changes can produce differences between them. Offset pagination still becomes more expensive on deeper pages. Existing controllers beyond product search have not been migrated to the new validation approach.

## Prioritize as an engineer

For a learning session, L1 is a small, approachable change. For a proposed real deployment, order access and payment recovery have higher consequences than another catalog feature. Explain that distinction in your journal: learning sequence and release priority serve different purposes.

Review [the implemented ADR](../adr/0009-catalog-query-contract.md), then choose one open finding and write its reproduction plan, acceptance criteria, and likely compatibility effects before implementing it.
