# Module 4: Compare products and explain an HTTP validator

[Bootcamp home](README.md) | [Tracker](story-tracker.md) | [Journal](journal.md)

Stories: MS-03 and MS-17. The source and acceptance tests are present; consult the tracker for the latest verification result. These endpoints do not require a schema migration.

## One result, several sources

A comparison is a purpose-built view of existing information. Product supplies identity and name; Category supplies classification; Image supplies gallery links; Variant supplies price, currency, options, and weight; Inventory supplies observed availability; approved Reviews supply the rating summary.

Open [the contracts](../../src/Agora.Api/Contracts/ProductInsightContracts.cs), [the controller](../../src/Agora.Api/Controllers/ProductInsightsController.cs), and [the tests](../../tests/Agora.Tests/Integration/ProductInsightsApiTests.cs). Keep all three open. A DTO says what we promise, a controller shows how we obtain it, and a test tries to falsify that promise.

Imagine a waiter collecting information for four meals. Walking to the kitchen once per meal is one approach. Asking for all four together avoids repeated trips. This analogy explains batching, but has a limit: a database can need several SQL statements to load several related collections. The target here is a fixed, small number of statements as the requested product count grows from two to four.

## Trace one comparison

Send `POST /api/products/compare` with a JSON object whose `productIds` array contains B followed by A. Use real product IDs from your local catalog.

1. Model validation rejects fewer than two or more than four IDs, duplicate IDs, empty GUIDs, or a missing array. This happens before the controller queries the catalog.
2. The controller loads the requested active products and related category, images, variants, and inventory without tracking them for updates.
3. It builds a dictionary keyed by product ID. An ID missing from that dictionary is missing or inactive. Any unusable ID makes the entire request fail with 422 and `unusableProductIds`.
4. A separate grouped query computes approved review statistics for all requested products. It does not load review bodies or query once per product.
5. The mapper iterates the original requested ID list. That is the operation that preserves B then A.
6. Each product contains ordered images and variants. Every variant retains its own amount and currency. Availability is a current observation, not an inventory reservation.

Why explicitly restore order? An SQL membership condition answers “is this ID in this set?” It does not answer “where did this ID appear in the caller's array?” The dictionary gives efficient lookup; iterating the original array supplies the required order. These two structures have different jobs.

Why use split queries? Loading multiple collections in one joined result can multiply rows: three images and four variants can produce twelve combinations for one product. `AsSplitQuery` loads the collections separately. It avoids that multiplication but adds round trips and does not promise a single snapshot across concurrent updates. That tradeoff is acceptable for this current-information view. Checkout must still revalidate commercial facts.

## Prove a performance property without timing a laptop

Find `Comparison_executes_a_fixed_number_of_selects_for_two_and_four_products`. It attaches a test logging provider to the factory's logger and observes EF's executed-command events. Setup happens before observation. It compares the command count for two products with the count for four, and rejects insert/update/delete statements in those reads.

This is stronger evidence for the specific N+1 concern than “it felt fast.” It is still limited: a fixed number of queries can be expensive if each query loads excessive rows. Here the request has at most four products, and the review query aggregates in the database. Existing products may still have substantial galleries or variant collections; the endpoint returns those choices intentionally.

Predict a regression: move the review aggregate inside the product mapping loop. Two products now need two review queries; four need four. Functional JSON tests could still pass, while the query-count test should fail. That is why this performance test protects a different property.

## Five buckets, one weighted average

For approved ratings `[5, 5, 3]`, the result is:

| Stars | Count | Contribution to the total star sum |
| --- | --- | --- |
| 1 | 0 | 0 |
| 2 | 0 | 0 |
| 3 | 1 | 3 |
| 4 | 0 | 0 |
| 5 | 2 | 10 |

There are three reviews and thirteen stars. The average is `13 / 3`, rounded to `4.33`. Dividing by five buckets would answer a different question. Empty buckets count as zero reviews; they are not five additional observations.

The query groups approved reviews by rating and returns at most five rows. The mapper fills missing buckets in ascending star order. With no approved reviews, count is zero and average is null: there is no observed average yet. A numeric zero would look like a rating outside the permitted one-to-five scale.

## An ETag is a fingerprint of a representation

First request: `GET /api/products/{productId}/reviews/summary`. The response contains the summary and an `ETag` header. Copy that complete header value, including quotes. On the next request, send it as `If-None-Match`.

The server still checks product existence and computes the current summary. It serializes that summary to bytes and hashes those bytes using SHA-256. If the supplied validator matches, the server sends 304 and no body. Otherwise it sends 200 with the exact bytes it hashed and the current ETag.

In everyday language: “I already have the version with this fingerprint; send the content only if it differs.” In implementation language: the validator identifies this response representation. In a test: hash the returned bytes yourself and compare that value with the quoted ETag. These are three descriptions of the same contract.

`Cache-Control: no-cache` allows storage but requires revalidation before reuse. It does not mean “never store.” This implementation saves response transfer on an unchanged conditional read; it does not avoid the database query, and it adds no stored cache.

The framework parser handles a list of validators, a weak validator beginning `W/`, and the wildcard `*`. GET permits weak comparison. The server emits a strong validator because it hashes the actual bytes, while also accepting a client's equivalent weak validator. A missing product returns 404 even when the client sends `*`; there is no existing representation to validate.

## Content changes and non-changes

Approving a pending review changes its bucket and the total. Editing an approved review moves it back to Pending in the existing domain method, so it leaves the public summary. Both transitions should change the ETag when they change the summary bytes.

Changing a review title without changing which approved ratings are represented does not inherently require a different summary ETag. The summary does not contain titles. Likewise, two different sets of approved reviews can produce identical buckets and therefore identical summary representations. A fingerprint of these bytes is not a review audit identifier.

## A short study session

Run the focused integration tests from the repository root:

```powershell
dotnet test Agora.slnx --filter FullyQualifiedName~ProductInsightsApiTests
```

If `dotnet` is not on your PATH, invoke your installed SDK executable explicitly. Read the actual test result; the existence of this command is not evidence that it passed.

Then work through these questions before opening the answers:

1. A costs 12 USD and B costs 10 EUR. Which product should the API call cheapest?
2. A comparison requests `[B,A]`, but SQL returns A first. Where should ordering be restored?
3. An approved three-star review changes to pending. Should the summary count drop even if its row remains in the database?
4. An unchanged summary receives `If-None-Match: W/"matching-value"`. What status and body should it return?
5. Does a 304 prove no SQL was executed?
6. Does a fixed query count prove all query performance problems are solved?

<details>
<summary>Answers and reasoning</summary>

1. Neither. There is no exchange-rate policy. Keep both currencies explicit and avoid inventing a conversion.
2. In response construction, iterate the requested ID sequence and look up each loaded product. Sorting by product name would violate the request-order contract.
3. Yes. Membership in this read model depends on approval, not physical row existence.
4. 304 with no response body when the opaque tag matches the current representation. GET uses weak comparison even though the emitted tag is strong.
5. No. This implementation queries and hashes current content before deciding whether to send it.
6. No. Also consider rows, selected columns, indexes, query plans, round trips, and concurrency requirements. This test specifically catches increasing per-product queries.

</details>

## Explain it back

Explain the comparison in thirty seconds using the words “validate, batch, dictionary, input order.” Then explain the summary using “approved, group, zero-fill, bytes, validator.” Finally, trace the same explanations in the controller. If a line seems unrelated, ask what promise or failure case it supports. That connection is the skill this module is building.
