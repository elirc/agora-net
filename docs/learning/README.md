# Learn software engineering with Agora

For smaller steps and repeated explanations of the codebase itself, begin with
[AstraDocs](../../astradocs/README.md). It builds familiarity with requests,
objects, persistence, and tests before you continue with this broader curriculum.

Start here if you can write some code but struggle to understand a whole backend or change it confidently. Agora is your practice codebase: you will trace requests, reproduce defects, build features, and defend design decisions using evidence.

The goal is increasing independence. Reading every page does not demonstrate seniority; making a safe change, explaining its limits, and helping someone else maintain it does. The stages below describe practice outcomes, not a guaranteed promotion timeline.

## Your first session

1. Follow [your first hour](01-first-hour.md). Run a request and one test before reading the architecture overview.
2. Explain the request path aloud using [the code tour](02-code-tour.md).
3. Work through [the catalog bug](04-catalog-worked-example.md). Predict the result before looking at the fix.
4. Copy [the progress journal](progress-journal.md) into your own notes. Record evidence, confusion, and the next smallest experiment.

Use this loop for every lesson: **predict -> run -> inspect -> change -> test -> explain**. Spend more time doing the exercises than reading. If stuck, write expected behavior, actual behavior, and the smallest input that shows the difference. Then use a hint or ask for one.

## Curriculum

| Stage | Read and practice | Evidence to produce |
| --- | --- | --- |
| Foundations | [First hour](01-first-hour.md), [code tour](02-code-tour.md), [C# essentials](03-csharp-essentials.md) | Run locally; trace a request through HTTP, C#, and SQL; explain object lifetime |
| Independent feature work | [Worked example](04-catalog-worked-example.md), [testing](05-testing.md), [HTTP contracts](06-http-contracts.md) | Reproduce a bug; add a focused regression; implement and document a feature |
| Data and correctness | [Queries and performance](07-data-and-performance.md), [concurrency](08-concurrency.md) | Inspect SQL; explain a conflicting write; identify a failure that a transaction cannot solve |
| Operational ownership | [Debugging and operations](09-debugging-and-operations.md), [security boundaries](10-security.md) | Investigate a failure with evidence; produce a threat model and recovery plan |
| Design and technical leadership | [Design and delivery](11-design-and-delivery.md), [feature backlog](feature-backlog.md) | Compare alternatives, split a risky change, review another solution, write an ADR |

Use the [roadmap and readiness rubric](roadmap.md) to choose your pace. The [review findings](review-findings.md) distinguish changes implemented here from open limitations. [Glossary](glossary.md) translates unfamiliar terms. [Mentor prompts](mentor-prompts.md) help you get assistance without outsourcing the practice.

## What was implemented as a teaching example

The product search feature now has a dedicated input contract and query composer. It fixes cross-variant price matching, supports availability and currency filters, treats search wildcards literally, rejects invalid ranges and overflowing page offsets, and adds a unique sort tie-breaker. Integration tests exercise these behaviors through real HTTP and SQLite. [ADR-0009](../adr/0009-catalog-query-contract.md) records the tradeoffs.

You still have meaningful work to do: the [backlog](feature-backlog.md) contains unsolved tickets with acceptance criteria and hints. Learning docs stay in this repository; the commerce API remains a realistic subject to practice on.

## How to use existing reference docs

[Getting started](../getting-started.md) is the full commerce walkthrough. [Architecture](../architecture.md) explains the existing design. [API reference](../api-reference.md) defines endpoint behavior. [Testing reference](../testing.md) describes the harness. Read the relevant section when an exercise reaches it instead of memorizing all endpoints first.
