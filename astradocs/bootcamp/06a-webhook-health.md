# Workshop 6a: ask precisely what a report counts

Story: MS-30. Read [the tracker](story-tracker.md) for verification status. This workshop explains the implementation; it does not turn a pending test into a passing test.

## Start with one delivery

A webhook delivery is a message addressed to one subscription. Sending it can fail and later succeed. In the current model, the delivery stores its latest status and a running attempt count. It does not store a row for every attempt.

Open [Webhook.cs](../../src/Agora.Domain/Entities/Webhook.cs). Find `CreatedAt`, `AttemptCount`, and `RecordAttempt`. Before reading further, predict which of these changes when a failed message is retried successfully.

Answer: the creation time stays fixed, the attempt count increases, and the current status becomes succeeded. The health report therefore describes **current outcomes for deliveries created during an interval**. It cannot reconstruct historical outcomes as they looked yesterday.

Say that again in ordinary language: choose the messages by their birthdays, then inspect how they are doing now. Retrying a message changes its result, not its birthday.

## Trace the request in three passes

1. Read [the controller](../../src/Agora.Api/Controllers/WebhookHealthController.cs). It checks administrator access, paired dates, a maximum 30-day interval, and bounded pagination. With no dates, it captures the clock once and uses the preceding seven days.
2. Read [the query](../../src/Agora.Api/Queries/WebhookHealthQuery.cs). Find the creation-time predicate, overall aggregate, grouping by subscription, and pagination. Counts happen in SQL; only bounded groups come back to the application.
3. Read [the response contracts](../../src/Agora.Api/Contracts/WebhookHealthContracts.cs). Notice that URL, payload, signature, and secret are absent. A reporting DTO states which information this feature needs.

Now explain the same path without class names: validate the question, choose matching messages, count their outcomes, then return totals and one page of subscription totals.

## Work the arithmetic by hand

Suppose the chosen interval contains these four deliveries:

| Delivery | Current status | Lifetime attempts |
| --- | --- | ---: |
| A | Pending | 0 |
| B | Failed | 2 |
| C | Succeeded | 3 |
| D | Failed | 5 |

Total is 4; pending is 1; succeeded is 1; failed is 2. With a five-attempt cap, exhausted failed is 1. Exhausted failed is a subset of failed: adding it to the other status counts would double-count D.

Success ratio is 1 / 4 = 0.25. The lifetime attempt total is 10. It is **not** the number of attempts performed inside the interval: some retries may have happened later. This distinction is why the contract calls the field `CohortLifetimeAttemptCount`.

If D succeeds on a later attempt, the same creation interval now has two successes and one failure. The report changes because it is a current-state report. Detailed attempt history belongs to SS-16, which depends on the durable delivery work in SS-15.

## Why the end is exclusive

The query uses `CreatedAt >= from && CreatedAt < to`. A message created exactly at `to` belongs to the next adjacent interval. For intervals [Monday, Tuesday) and [Tuesday, Wednesday), a Tuesday-midnight message appears once.

Try replacing `<` with `<=` mentally. That same message now appears in both reports. Boundary tests exist to catch this error even when ordinary daytime examples look correct.

## Overall totals are not page totals

Imagine subscription A has 1 success out of 1 delivery, and B has 1 out of 9. Their ratios are 1 and approximately 0.111. Averaging those ratios gives approximately 0.556, which is wrong for the whole population. The whole population has 2 successes out of 10 deliveries: 0.2.

The implementation computes overall counts from the complete filtered cohort before paging subscription groups. A page containing only A must still report the same overall totals as a page containing B. Empty cohorts have a null success ratio because there is no denominator; zero would incorrectly suggest an observed failure rate.

## One response, one database view

The controller opens a database transaction around existence checking and both aggregates. This keeps the response internally consistent while delivery retries update rows. The transaction does not freeze the world forever, and the `asOf` timestamp does not provide time travel. A later request can see a newer state.

The report neither calls the sender nor performs retries. Reading operational information should not itself change delivery outcomes. `Cache-Control: no-store` asks caches not to retain this administrator report.

## Read the tests as experiments

Open [WebhookHealthReportApiTests.cs](../../tests/Agora.Tests/Integration/WebhookHealthReportApiTests.cs). Each test controls a different source of ambiguity:

- A frozen `TimeProvider` makes default window boundaries predictable without sleeping.
- Exact start and end timestamps prove half-open interval behavior.
- Multiple subscription pages distinguish overall totals from page totals.
- A later recorded retry proves the report uses current status for the original creation cohort.
- A counting sender proves reporting does not send a webhook.
- Captured EF commands check that reads stay bounded and do not select sensitive delivery columns or issue data mutations.
- Authentication cases check that customer and anonymous callers cannot read the administrator report.

The SQL inspection is deliberately a supporting check. HTTP values and unchanged persisted state still matter; a short query can calculate the wrong answer.

## Practice before looking at answers

1. An old delivery is retried today. Does today's creation cohort include it?
2. One page contains three successes. Can the overall succeeded count be ten?
3. Why should a report with no deliveries return a null ratio?
4. Which new data would you need to answer “how many HTTP attempts failed yesterday”?
5. If a delivery has failed with six recorded attempts, is it exhausted under a five-attempt cap?

Answers: (1) No, selection uses creation time. (2) Yes, overall totals include all matching groups. (3) Division has no denominator; no observations are different from observed zero successes. (4) Timestamped individual attempt records and their outcomes. (5) Yes; use `>=`, because legacy or inconsistent data may exceed the cap.

## Journal exercise

Write your original prediction for the four-delivery example, then your corrected calculation. Sketch the endpoint as four boxes: validation, cohort, aggregation, response. Finally explain it aloud using the birthday analogy, then explain it again using the actual field names. Repeating the idea across arithmetic, a drawing, and code is the exercise.
