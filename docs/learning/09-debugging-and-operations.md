# Debug systematically and think about recovery

**Outcome:** narrow a failure with evidence and describe what an operator should do next.

## Use a hypothesis loop

Write: "I expected X for input Y, observed Z, and suspect boundary B because of evidence E." Choose the cheapest experiment that distinguishes your hypothesis from another plausible cause. If you get 400 before a breakpoint in the controller, inspect model binding and validation before investigating EF.

For a catalog result that looks wrong, save the request, matching product IDs, relevant variant prices and inventory, and generated SQL. Reduce to one category and two products. Do not begin by rewriting the entire controller.

## Three drills

**Drill A: wrong filter result.** Use the 10/100 variant example. Identify the exact logical condition that admits the product. Deliver a failing assertion, not only a screenshot.

**Drill B: healthy process, unhealthy dependency.** Inspect `/health` and `/health/ready` implementations and tests. Explain why a process being alive and its database being usable are separate questions. Use an isolated test configuration to simulate a dependency failure rather than damaging your working database.

**Drill C: timeout during payment.** Use the failure table in the concurrency lesson. Decide which records and gateway identifiers you need before retrying. A timeout means no response was received; it does not prove the operation did nothing.

## Write a small runbook

For one failure, document the symptom, impact, first checks, safe mitigation, reconciliation steps, verification, and escalation information. Example: pending orders with reserved inventory require checking payment outcomes before releasing stock or retrying charges. Identify missing tools as missing tools; do not invent a reconciliation endpoint that is not implemented.

The API currently logs method, path, status, and duration. Think about how a guest token in a path could reach a log. Useful diagnostics and sensitive-data handling must be designed together.

For a future metric, define numerator, denominator, time window, and action: "fraction of checkout attempts with an unresolved payment outcome over fifteen minutes" is more actionable than "errors." Set thresholds only after understanding baseline traffic and recovery needs.

**Checkpoint:** write a one-page incident note separating observed facts, hypotheses, mitigation, and follow-up prevention. **Stretch:** define a latency objective and describe which failures it would miss.
