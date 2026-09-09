# Grow from implementing tickets to owning decisions

**Outcome:** make a change understandable, reviewable, and recoverable for other engineers.

## Explain the problem before the pattern

For catalog search, the problem was a false match plus growing input and query logic. A request contract and query helper address those forces. A new service layer, repository abstraction, or microservice would need an additional reason. Ask what future change becomes easier and what new complexity the abstraction imposes now.

Use [ADR-0009](../adr/0009-catalog-query-contract.md) as an example. An ADR should state context, considered alternatives, decision, consequences, and when to revisit. Do not record only the selected solution's benefits.

## Split a larger feature vertically

For durable webhooks, a useful plan might separate: event record and migration; atomic creation with order state; worker claim and retry; operational inspection and replay; rollout and reconciliation. Each slice needs a coherent behavior and validation. A database table alone does not establish reliable delivery.

Before a schema change, ask whether old code and new code can both run during rollout. For a required field, consider adding it permissively, backfilling, verifying, then enforcing it. State how a failed rollout affects writes already made. "Revert the code" may not reverse a data migration.

## Review in risk order

Check requirements and authorization first, then invariants and failure paths, then query cost, test evidence, clarity, and style. In the catalog change, ask whether one variant satisfies all filters and whether page arithmetic can overflow before debating helper names.

A useful PR description says: "A product with prices 10 and 100 matched a 20-40 search because separate variants satisfied each bound. The query now requires one variant to satisfy every variant filter. Verified with HTTP/SQLite regressions for split bounds and stock reservations." Add behavior changes and limitations a reviewer needs to assess.

## Practice technical communication

Write a short design note comparing offset and cursor pagination for this API. Include caller requirements, expected data volume, consistency under inserts, indexing, implementation cost, and migration. Then argue for the alternative you did not choose. This tests whether you understand the tradeoff instead of memorizing a preferred pattern.

**Checkpoint:** ask a peer to implement or review from your note without the chat history. Revise where they need clarification. **Stretch:** write a plan with two acceptable implementation options and explain what evidence would change your recommendation.
