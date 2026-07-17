# 2. Money is a non-negative value object that clamps at zero

Status: Accepted

## Context

Money appeared as bare `decimal` in early sprints. Bare decimals invite three
recurring bugs: silently adding USD to GBP, rounding drift from arithmetic done
at inconsistent precision, and negative totals from over-large discounts
reaching the payment gateway as a negative charge.

## Decision

`Agora.Domain.Common.Money` — an immutable record of `decimal Amount` + a
3-letter ISO `Currency`:

- the constructor rounds to 2 dp `MidpointRounding.AwayFromZero` (the
  commercial convention: 0.005 → 0.01) and validates the currency code;
- **negative amounts throw** `DomainException`;
- `Add`/`Subtract` throw on currency mismatch;
- **`Subtract` clamps at zero** instead of going negative.

`ProductVariant.Price` is an owned type (`PriceAmount` / `PriceCurrency`);
order and cart columns store the decimal via ADR-0001 with the currency on the
parent row.

## Consequences

- A discount larger than the subtotal produces a zero subtotal rather than a
  negative one, which is exactly the desired behavior at the one place it
  matters (`subtotal.Subtract(discountAmount)`) — no caller-side clamping, and
  `TotalsPipelineTests` pins the 100%-off case end to end.
- Rounding happens in one place, so `19.99 × 2` is 39.98 everywhere.
- Cross-currency arithmetic fails loudly at the boundary rather than producing a
  meaningless number.
- The clamp is a *silent* correction: `Subtract` cannot distinguish "discount
  exceeded subtotal, clamp is right" from "sign error upstream". It is a value
  object, so it has no context to judge — callers that need the difference must
  compare before subtracting. We accept this because the only subtraction paths
  in the domain are discount and refund math, where clamping is correct.
- A private parameterless constructor exists purely for EF materialization.
