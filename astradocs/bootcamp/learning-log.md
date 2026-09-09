# Your learning log: predictions before explanations

[Bootcamp home](README.md) | [Implementation journal](journal.md)

The implementation journal records what changed in the repository. This file helps you record what changed in your understanding. Copy one blank entry into your own notes for each session. A short, accurate explanation is enough; you do not need to rewrite the whole lesson.

## A worked entry: cart counts

**Question:** A cart has two active rows, with quantities three and five, and one saved row with quantity two. What should `activeLineCount` and `savedLineCount` be?

**Prediction before reading:** I think the active count is eight and the saved count is two.

**Observed contract:** The counts are two and one. These fields count rows, not physical units.

**Evidence:** [CartContracts](../../src/Agora.Api/Contracts/CartContracts.cs) derives the values from response collections. [BootcampResponseContractTests](../../tests/Agora.Tests/Unit/BootcampResponseContractTests.cs) exercises quantities that differ from row counts.

**Corrected explanation in my own words:** A line is a choice of variant with a quantity. Buying five of one variant still occupies one line. The two counts help the interface display how many different rows are active or saved.

**New example without copying:** One active row with quantity 99 has activeLineCount one. An empty saved collection has savedLineCount zero.

**One useful follow-up:** If the product needs a physical-unit total, that should be a separately named value calculated by summing quantities. Reusing the line-count name would break its meaning.

Notice that the wrong prediction is kept. It identifies exactly which distinction needed practice.

## A worked entry: a stale note

**Question:** Two clients read note version zero. Client B saves first. Can client A safely save because its earlier C# version check succeeded?

**Prediction before reading:** Maybe yes, because the controller already validated the request.

**Observed implementation:** The database checks the original version in the UPDATE. After B saves version one, A's zero-version update affects no row and becomes a concurrency conflict.

**Evidence to inspect:** [WishlistConcurrencyTests](../../tests/Agora.Tests/Integration/WishlistConcurrencyTests.cs). Check the [tracker](story-tracker.md) for the current test result; a test's source alone is not a passing result.

**Corrected explanation:** Validation established a fact at one moment. Another writer can invalidate that fact before persistence. The database condition closes that gap.

**New example:** Two people reorder one gallery. A parent revision can prevent the second person's old order from silently replacing the first person's accepted order.

**One useful follow-up:** Find every write that changes the protected information. A token is incomplete if another route changes the same information without participating.

## Blank session entry

- Date and lesson:
- One question I will answer before reading code:
- My prediction:
- The request and fixture values I used:
- What I observed, including HTTP status and relevant database state:
- Exact code/test I traced:
- My corrected explanation in two or three sentences:
- A different example that follows the same rule:
- A counterexample that would break a weaker implementation:
- One thing I still cannot explain:
- The next smallest experiment:

If a command fails to run, record that separately from the behavior you were testing. “The test did not execute” and “the assertion failed” are different observations.

## Repeat the idea in different forms

For each completed lesson, do four short passes. First explain it aloud without code. Then draw two or three boxes for the data involved. Next trace the request through the actual files. Finally, predict a failure case and find the test that protects it.

For category filtering, the boxes might be request → filtered query → count and page. For note editing, use request → owned item → conditional save. For copying, use owned source + owned target → validated selection → atomic save. Keep the drawing small enough that you can explain every arrow.

Return to the same question at your next session and again several sessions later. Change one input rather than memorizing the original answer. For example, change the cart quantities, reverse product comparison IDs, or make a review pending. The goal is to carry the rule into a new example.

## A practical self-check

You are ready to extend a feature when you can explain its user value, locate its input and response contracts, name its ownership boundary, predict one rejected request, and verify the persisted result after rejection. If one part is unclear, revisit that part of the lesson. This is a guide for choosing your next exercise, not a test of your worth or speed.
