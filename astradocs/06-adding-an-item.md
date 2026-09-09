# 06: Add an item to a cart, three ways

[Home](README.md) · Previous: [Code reading](05-read-code-slowly.md) · Next: [Checkout storyboard](07-checkout-storyboard.md)

**Small outcome:** distinguish changing an object from saving it, and checking stock from reserving it.

## Way 1: shopper language

A shopper has an empty cart and asks to add two units of a tee variant. The API finds the cart and the variant, checks the operation, adds or merges a cart line, and saves the result. The response contains the cart's active items and current pricing.

Adding to the cart checks available stock but **does not reserve it**. Another shopper can still buy those units before this shopper checks out. Checkout must check stock again.

## Way 2: follow the actual method

Open [CartsController.AddItem](../src/Agora.Api/Controllers/CartsController.cs). Read in this order:

1. `LoadCart` loads the cart with tracking. No cart means 404.
2. A query loads the requested variant with its product and inventory. Missing variant or inactive product is rejected with 422.
3. `cart.AddItem(variant.Id, request.Quantity)` applies cart rules and changes the object. Open [Cart.AddItem](../src/Agora.Domain/Entities/Cart.cs): an existing variant merges into its existing line, and quantities must stay within 1–99.
4. For a new line, the controller explicitly marks the cart item as added in EF when necessary.
5. The controller compares the merged quantity with current available stock. Too much means 409. That path returns before `SaveChangesAsync`.
6. `SaveChangesAsync` persists the tracked changes on the success path.
7. `CartResponse.From(cart)` builds the response shape, including live variant prices.

There is a useful subtlety here: the cart object can already have changed at step 3 even if step 5 returns a conflict. In that failure path the controller does not save it. **Memory changed** and **database changed** are different claims.

## Way 3: a before-and-after table

This is an invented example with stock 5 and no reservations:

| Moment | Cart quantity in this request's object | Cart quantity saved in SQLite | Stock on hand | Reserved |
| --- | --- | --- | --- | --- |
| Before adding | 0 | 0 | 5 | 0 |
| After `cart.AddItem(..., 2)` | 2 | 0 | 5 | 0 |
| After successful save | 2 | 2 | 5 | 0 |
| After response | 2 in the returned cart | 2 | 5 | 0 |

The response is a DTO describing the cart. It is not the saved database row itself. [Follow the data](08-follow-the-data.md) repeats this using separate boxes.

## Say the same idea a different way

The cart is a shopping intention. A reservation sets units aside during checkout. The order is a record created by checkout. Those three jobs deserve different objects and workflows.

**Q8:** After adding two units to a cart, did reserved stock become two?

**Q9:** Which operation saves the cart: `cart.AddItem(...)` or `db.SaveChangesAsync(...)`?

**Q10:** If the cart contains two units of a variant and you add three more of that same variant, how many lines and units should there be, assuming enough stock?

**Stop:** use the table to explain the difference between a changed C# object and a saved row. [Answers](14-answer-key.md).
