# 12: Make one small contribution

[Home](README.md) · Previous: [Tests](11-tests-as-examples.md) · Next: [Revisit and recall](13-revisit-and-recall.md)

**Small outcome:** add a boundary test, run it, and explain it in a review. This is a learner exercise; the test described here is not added to the application by these docs.

## The task

The current cart limit is 99 units per line. Existing tests cover merging quantities and exceeding the limit. Add a test demonstrating that a merge **exactly at** the limit succeeds and keeps one line.

Use 60 existing units plus 39 more of the same variant. Work only on the in-memory cart rule. Availability belongs to a different test boundary, as the previous lesson explained.

## Pass 1: write the expected behavior

Before editing:

> When a cart has 60 units of a variant and I add 39 of the same variant, it should contain one line with quantity 99.

That sentence gives you the setup, action, and result. `60 + 40` would describe a different boundary case.

## Pass 2: find the home for the change

Open [CartTests.cs](../tests/Agora.Tests/Unit/CartTests.cs). Read `AddItem_MergeExceedingMax_Throws` and `AddItem_ExistingVariant_MergesQuantity`. Your new test can sit beside them. Keep any pre-existing working-tree changes intact; inspect `git diff` before and after your edit.

Add a `[Fact]` method named `AddItem_MergeExactlyAtLimit_KeepsOneLine`. Inside it, create a cart and one variant ID, add 60, add 39, and write assertions for line count and quantity.

## Pass 3: use hints only as needed

**Hint 1:** use the same GUID for both calls. Two different IDs create different variant lines.

**Hint 2:** `Assert.Single(cart.Items)` checks something different from `Assert.Equal(99, ...)`. You need both outcomes to match the sentence.

**Hint 3:** use `Guid.NewGuid()` for the identifier; no database product needs to exist for this domain-only test.

The complete example is in [the answer key](14-answer-key.md). Try your version first, then compare the reasoning rather than just the spelling.

## Pass 4: run and inspect

```powershell
dotnet test --filter FullyQualifiedName~CartTests.AddItem_MergeExactlyAtLimit_KeepsOneLine
dotnet test --filter FullyQualifiedName~CartTests
git diff -- tests/Agora.Tests/Unit/CartTests.cs
```

The existing implementation should already satisfy this test. It is an added specification of a boundary, not proof of a newly fixed bug. An optional temporary wrong expected quantity lets you see an assertion fail; restore the correct expectation immediately afterward.

## Pass 5: explain the contribution

Write: "This test covers the exact allowed merge boundary. It checks both one line and quantity 99. It exercises the cart rule without HTTP, stock availability, or persistence. I ran __ and observed __."

**Q17:** Why is an assertion about quantity alone insufficient if the requirement also says to retain one line?

**Q18:** Should this test call the running API or create a database row to obtain a valid variant ID?

**Stop:** show the test and your explanation to a teammate. After this feels comfortable, try [the SKU-filter practice ticket](../docs/learning/feature-backlog.md), which adds an actual API feature with a wider test boundary.
