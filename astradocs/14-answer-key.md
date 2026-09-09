# 14: Answers with the reasoning left in

[Home](README.md) · Previous: [Revisit and recall](13-revisit-and-recall.md) · Next: [Mentor guide](15-mentor-guide.md)

Use this after making a prediction. If your answer differs, compare the assumption behind it, then return to the named lesson. A short explanation in your own words matters more than matching this wording.

## Big picture: Q1–Q3

**Q1:** This is the backend, plus infrastructure and tests. A storefront could call its API. JSON returned by the API is data a frontend could display. Revisit [01](01-big-picture.md).

**Q2:** No. Product browsing reads information. It does not create an order or change stock. Even browsing with `inStock=true` only filters a current observation.

**Q3:** SQLite is the persistent store. A controller's local variable belongs to executing code; assigning that variable is not a database save.

## Browsing and code: Q4–Q7

**Q4:** Two items and total count five, assuming the dataset does not change between reads. Page size limits the returned page. `CountAsync` counts all matches. Revisit [04](04-browsing-story.md).

**Q5:** No. `ProductCatalogQuery.Apply` returns a composed query. The controller executes operations such as `CountAsync` and `ToListAsync`. A helper that returns a query description has a different job from a call that retrieves rows.

**Q6:** No. Ten is below twenty; one hundred is above forty. Neither variant is within the requested range. One variant must satisfy both bounds. Separate existential checks could accidentally use different variants. Revisit [05](05-read-code-slowly.md).

**Q7:** Four: `(3 - 1) * 2`. Page one skips zero; page two skips two; page three skips four.

## Cart and stock: Q8–Q12

**Q8:** No. The cart endpoint checks availability and saves the cart; it does not reserve inventory. Reservation occurs in checkout. Revisit [06](06-adding-an-item.md).

**Q9:** `db.SaveChangesAsync(...)` persists tracked changes. `cart.AddItem(...)` applies the object-level rule and changes in-memory state. The controller can return a stock conflict before saving that changed object.

**Q10:** One line, quantity five. Both additions refer to the same variant. Different variant IDs would be different lines.

**Q11:** Three: `5 - 2`. Reserved units are still included in on-hand stock but excluded from available stock. Revisit [07](07-checkout-storyboard.md).

**Q12:** On hand three, reserved zero, available three. Commitment deducts the two units from on-hand and removes their reservation. Releasing instead would return to five, zero, five.

## Data and debugging: Q13–Q15

**Q13:** A variant ID. The cart needs a specific purchasable choice, such as black/medium. A product groups choices. Revisit [08](08-follow-the-data.md).

**Q14:** The order should preserve the purchase's price. Reading today's variant price could rewrite the meaning of a historical purchase after a catalog change. The copied order item is a snapshot of that purchase information.

**Q15:** Begin with `ProductSearchRequest.PageSize` and the framework's input validation boundary. It permits 1–100. Since the valid request hit the breakpoint, the invalid request's earlier rejection is consistent with model validation. Revisit [10](10-debugging.md).

## Tests and first change: Q16–Q18

**Q16:** The integration test using SQLite. An object-only test does not execute the EF query through the SQLite provider and cannot reveal its SQL translation behavior. Revisit [11](11-tests-as-examples.md).

**Q17:** Quantity alone does not fully specify line structure. A cart might contain the expected quantity on one line and still have an extra unwanted line. The requirement includes both one line and quantity 99. Revisit [12](12-first-change.md).

**Q18:** No. The domain test can use a new GUID and a new cart object. It checks the merge rule in memory. HTTP binding, real variant existence, stock, and persistence require different tests.

## One complete version of the first-change exercise

Paste this method inside the existing `CartTests` class if you want to compare it with your attempt. The file already imports the required domain types and the test project supplies xUnit imports. This is an exercise solution, not a test already installed by these docs.

```csharp
[Fact]
public void AddItem_MergeExactlyAtLimit_KeepsOneLine()
{
    var cart = new Cart();
    var variantId = Guid.NewGuid();

    cart.AddItem(variantId, 60);
    cart.AddItem(variantId, 39);

    var line = Assert.Single(cart.Items);
    Assert.Equal(99, line.Quantity);
}
```

Read it aloud: **make an empty cart; choose one identifier; add sixty; add thirty-nine of the same thing; require one line containing ninety-nine.** Then close this page and explain why no database is necessary.

## A worked worksheet for browsing

The caller wants up to two products matching "tee". It sends GET `/api/products?search=tee&pageSize=2`. Query values bind to `ProductSearchRequest`. `ProductsController.List` uses `ProductCatalogQuery.Apply`, counts matches, loads the page and related data, loads ratings, and maps a paged response. This read does not save entity changes. Invalid page size returns 400 before the action body. `CatalogSearchApiTests` verifies search behavior through HTTP and SQLite, but does not prove real payment reliability.

If you can retell that paragraph with a different product and page size, you are practicing the structure rather than just memorizing the words.
