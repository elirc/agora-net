# Roadmap and readiness rubric

Use these as twelve practice cycles, roughly a week each if you have time for several focused sessions. Repeat a cycle when you cannot explain the result without the solution. Progress depends on evidence, not elapsed weeks.

| Cycle | Focus | Deliverable | Exit question |
| --- | --- | --- | --- |
| 1 | Run and navigate | Request trace and baseline test result | Can I locate the responsible code from a URL? |
| 2 | C# and domain rules | Inventory state table and one boundary test | Can I distinguish an object mutation from a database write? |
| 3 | Debug a defect | Reproduction of the split-variant bug | Can I explain exactly why the old predicate passes? |
| 4 | Implement a contract | Backlog L1 with API examples | Can someone predict the endpoint's behavior from my docs? |
| 5 | Test independence | Backlog L2 regression matrix | Can my tests pass in any order without shared-state assumptions? |
| 6 | SQL and measurement | Query inspection and benchmark note | Can I support my performance claim with comparable data? |
| 7 | Concurrency | Two-writer timeline and deterministic test | Can I explain what a version token does not protect? |
| 8 | Authorization | Backlog L5 design and ownership tests | Have I tested a real other owner's resource? |
| 9 | Operations | Incident drill and runbook | Can I decide whether a retry is safe? |
| 10 | Durable work | L6 or L7 design proposal | Can I recover from a stop between every durable step? |
| 11 | Delivery | Migration, rollout, and rollback plan | Can old and new code coexist during deployment? |
| 12 | Teach and review | Capstone demo, ADR, and peer review | Can another engineer maintain this from my explanation? |

## Self-assessment

Score each competency 0 (unfamiliar), 1 (with a walkthrough), 2 (independently), or 3 (can handle tradeoffs and teach it). Attach an artifact; a score without evidence is a guess.

| Competency | Evidence of independence | Evidence of broader ownership |
| --- | --- | --- |
| Code comprehension | Trace an unfamiliar endpoint | Explain coupling and choose a safe change boundary |
| Correctness | Reproduce and fix a boundary bug | Identify cross-request and partial-failure invariants |
| Testing | Select the right test boundary | Expose a realistic false-positive test and improve it |
| Data | Inspect SQL and constraints | Defend query and migration choices with measurements |
| Security | Enforce identity and ownership | Model abuse cases and design credential recovery |
| Operations | Investigate with logs and state | Define reconciliation, mitigation, and useful signals |
| Communication | Write a clear PR | Resolve competing requirements and unblock a reviewer |

Mid-level practice means delivering a scoped feature independently with relevant tests and clear limits. Senior-level practice adds handling ambiguity, failure recovery, tradeoffs, and enabling teammates. A repository cannot demonstrate every workplace skill; seek real review, collaboration, and operational exposure too.

## Capstone

Choose durable checkout (L6) or webhook outbox (L7). Submit a design before implementation, demonstrate one successful flow and three controlled failure scenarios, include migrations where needed, and write a recovery runbook. Ask a reviewer to challenge one assumption. Your final reflection should describe what changed after review and what remains uncertain.
