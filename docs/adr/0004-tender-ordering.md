# 4. Totals order is discounts → tax → gift-card tender

Status: Accepted

## Context

An order carries three reductions — a discount code, a gift card, and possibly
free shipping — and one addition, tax. The order in which they apply changes
the money, so it is a decision, not an implementation detail:

- Taxing before discounting overcharges tax on money the customer never paid.
- Treating a gift card as a discount (before tax) *undercharges* tax: a gift
  card is stored value being spent, not a price reduction, and taxing authorities
  care about the difference.
- Testing a free-shipping threshold against the pre-discount subtotal gives away
  shipping the customer didn't earn.

## Decision

One fixed order, implemented in `CheckoutService` and pinned by
`TotalsPipelineTests`:

```
discount           = code.CalculateDiscount(subtotal)      // percent or fixed, clamped
discountedSubtotal = subtotal − discount
tax                = Σ line.discountedAmount × zoneRate(line.taxCategory)
shipping           = method.CalculateCharge(discountedSubtotal, totalWeight)
total              = discountedSubtotal + tax + shipping
giftCardTender     = min(card.Balance, total)
gatewayCharge      = total − giftCardTender
```

Three details carry weight:

- The discount is **prorated across lines** by rate (`discount / subtotal`), so
  per-category tax is computed on each line's discounted share. A cart mixing a
  standard-rate and a zero-rate product taxes each correctly under a
  basket-wide discount.
- Free-shipping thresholds compare against the **discounted** subtotal, and are
  inclusive (`>=`): exactly 50.00 ships free.
- The gift card is **tender, applied after tax** to the final total; only the
  remainder reaches the gateway.

The invariant `total = subtotal − discount + tax + shipping` holds to the cent
on every order.

## Consequences

- Tax is charged on what the customer actually pays for goods, per tax
  category, and the gift card doesn't distort it.
- Because the split is stored (`GiftCardAmount` alongside `Total`), every refund
  path can reconstruct what each tender paid and return it to its source — see
  ADR-0005 and `RefundTenderTests`. Refunds are only possible *because* the
  ordering is recorded, not just applied.
- Per-line rounding means the sum of line taxes can differ by a cent from tax on
  the basket total. We round per line, away from zero, and pin the conservation
  case (`PercentDiscount_RoundingConservesCents_AcrossLines`).
- Exactly one discount code and one gift card per order. Stacking would make
  proration and the refund split materially harder, and nothing needs it.
- Tax rates are read at checkout and the resulting amounts are frozen onto the
  order. Changing a zone's rate never rewrites history.
