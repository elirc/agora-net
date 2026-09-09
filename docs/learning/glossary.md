# Glossary with Agora examples

| Term | Plain meaning | Agora example |
| --- | --- | --- |
| API contract | What callers can rely on | Product query parameters, response fields, status codes |
| DTO | An object shaped for crossing a boundary | `ProductResponse` |
| Entity | Object with identity and changing state | `InventoryItem` |
| Value object | Value whose meaning comes from its contents | `Money` |
| Invariant | A rule that must stay true | Reserved stock must not exceed on-hand stock |
| Dependency injection | The host supplies an object's collaborators | Checkout receives a database and gateway |
| Scope | A lifetime boundary for related services | Request-scoped `AgoraDbContext` |
| ORM | Maps application objects and queries to database operations | EF Core |
| Materialization | Turning query results into objects | `ToListAsync` |
| Projection | Selecting the fields or shape you need | Review aggregate query |
| Predicate | A true/false condition selecting matches | Variant price and stock filter |
| Pagination | Fetching a bounded portion of a result | `Skip` and `Take` |
| Tie-breaker | Unique secondary ordering when primary values match | Product ID after price |
| Transaction | A group of database changes committed together | One `SaveChangesAsync` operation |
| Optimistic concurrency | Reject a write based on an outdated version | `InventoryItem.Version` |
| Idempotency | Repeating one logical operation does not repeat its effect | Proposed durable checkout replay |
| Reconciliation | Compare systems and resolve uncertain outcomes | Proposed payment recovery |
| Outbox | Durable pending event saved with business changes | Proposed webhook delivery work |
| Authentication | Establish an accepted identity | JWT validation |
| Authorization | Decide whether that identity may perform an action | Admin role or ownership check |
| Regression test | Detect a previously observed failure returning | Split-variant price-range test |
| Test fixture | Shared setup/lifetime for a set of tests | `AgoraApiFactory` |
| ADR | A record of a design choice and tradeoffs | Catalog query ADR |
| Runbook | Concrete investigation and recovery instructions | Pending-payment drill |
| SLO | A measurable service reliability objective | A proposed checkout latency target |

When a term remains fuzzy, point to the code that implements it and explain what would fail if it were removed. Vocabulary should help you reason, not replace the reasoning.
