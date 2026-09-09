# 08: Follow the data through different shapes

[Home](README.md) · Previous: [Checkout](07-checkout-storyboard.md) · Next: [Hands-on lab](09-hands-on-lab.md)

**Small outcome:** distinguish a product, a purchasable variant, a cart line, and an order line.

## One tee, several records

Imagine these simplified drawing labels:

| Thing | Example | Purpose |
| --- | --- | --- |
| Product `P1` | Classic Cotton Tee | Groups variants under a product name |
| Variant `V1` | Black / Medium, SKU `TEE-BLK-M`, price 19.99 USD | Identifies the specific thing a shopper chooses |
| Inventory for `V1` | On hand 5, reserved 0 | Tracks units for that variant |
| Cart item `C1` | Variant `V1`, quantity 2 | Records current shopping intention |
| Order item `O1` | Copied name, SKU, unit price, quantity, line total | Records what checkout ordered |

The actual IDs are GUIDs, not `P1` or `V1`. A SKU is another identifier with business meaning, such as `TEE-BLK-M`. Do not substitute a product ID where the API asks for a variant ID.

```text
Product
  ├── Variant A ── Inventory A
  └── Variant B ── Inventory B

Cart ── CartItem ── chosen Variant
Order ── OrderItem with copied purchase information
```

This picture simplifies relationships to show the jobs. Open [Product](../src/Agora.Domain/Entities/Product.cs), [ProductVariant](../src/Agora.Domain/Entities/ProductVariant.cs), [CartItem](../src/Agora.Domain/Entities/CartItem.cs), and [OrderItem](../src/Agora.Domain/Entities/OrderItem.cs) to see their actual fields.

## Three shapes that are easy to confuse

```text
HTTP JSON                C# objects                  SQLite records
request/response    <->   while code runs       <->   persisted data
```

These arrows represent explicit binding, mapping, queries, and saves. They do not mean that changing any one shape automatically updates the others.

- Changing JSON displayed in your editor does not change a saved order.
- Changing a tracked C# entity can become a database change when saved.
- Creating a response DTO decides what will be serialized; it is not a database save.

Repeat the distinction with the previous cart lesson: `cart.AddItem` changes an object; `SaveChangesAsync` saves tracked changes; `CartResponse.From` builds the outward-facing description.

## Current price versus purchase snapshot

`CartResponse.From` reads the variant's current price when building cart totals. Checkout copies the variant's price and names into `OrderItem`. If a product is renamed later, an existing order should still describe the purchase recorded at checkout.

For example, two units at 19.99 make a line total of 39.98 before order-level adjustments. The order's overall total also accounts for discount, tax, and shipping. A gift card is tender applied to that final total, not a product-price reduction.

## One storage detail to revisit later

[Money](../src/Agora.Domain/Common/Money.cs) holds a decimal amount and currency. The [SQLite converters](../src/Agora.Infrastructure/Persistence/SqliteValueConverters.cs) store monetary amounts in integer cents. Tax rates use a different scale. This is why query bounds require attention to precision rather than treating all numbers as interchangeable.

**Q13:** Which ID belongs in `AddCartItemRequest.ProductVariantId`: a product ID or a variant ID?

**Q14:** Why copy unit price into an order item rather than always reading today's variant price?

**Stop:** draw the five things from the table and explain each with one sentence. [Answers](14-answer-key.md).
