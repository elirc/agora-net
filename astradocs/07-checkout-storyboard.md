# 07: Checkout as a storyboard

[Home](README.md) · Previous: [Adding an item](06-adding-an-item.md) · Next: [Follow the data](08-follow-the-data.md)

**Small outcome:** explain reserve, commit, and release. The final section is a later-pass topic.

## Frame 1: a cart is ready

The cart contains two units. Saved-for-later lines are excluded from checkout. The controller translates the HTTP request into `CheckoutInput` and calls `CheckoutService.CheckoutAsync`.

Open [CheckoutController](../src/Agora.Api/Controllers/CheckoutController.cs) first. It is short. Then open [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs); read its `CheckoutAsync` method in chunks rather than all at once.

## Frame 2: validate and reserve

The service loads the cart, checks active lines, and resolves address, shipping, discount, and gift-card inputs. It calls `Reserve` on inventory for each active line. It calculates totals and creates an order snapshot, then saves the pending order and reservations.

Think of reservation as temporarily setting units aside. The numbers in this invented example begin at five units on hand:

| State | On hand | Reserved | Available = on hand − reserved |
| --- | --- | --- | --- |
| Before checkout | 5 | 0 | 5 |
| Reserve 2 | 5 | 2 | 3 |

Reservation did not change on-hand stock. It reduced what other shoppers can obtain.

## Frame 3A: payment succeeds

For an order requiring a gateway charge, the gateway accepts payment. The service marks the order paid, commits stock reservations, redeems any gift-card tender, records discount use, clears active cart lines, and saves those changes. Webhook dispatch follows that save.

| State | On hand | Reserved | Available |
| --- | --- | --- | --- |
| Before commit | 5 | 2 | 3 |
| Commit 2 | 3 | 0 | 3 |

The units were already unavailable while reserved. Committing converts that reservation into a stock deduction; it does not deduct the same two units from availability twice.

## Frame 3B: payment is explicitly declined

The service releases the reservation, removes the pending order, and saves the cleanup. The cart remains, and the gift card has not been redeemed. The API returns 402 for the payment failure.

| State | On hand | Reserved | Available |
| --- | --- | --- | --- |
| Before release | 5 | 2 | 3 |
| Release 2 | 5 | 0 | 5 |

## The same story in one picture

```text
Validate active cart and inputs
             |
Reserve stock; compute totals; save pending order and reservations
             |
   Does the gateway accept the required charge?
        /                              \
      yes                         explicit decline
       |                                |
Commit reservations                  Release reservations
Mark Paid; update tender/cart        Remove pending order
Save                                 Save; keep cart
Dispatch webhooks                    Return 402
Return created order
```

When the required gateway amount is zero, checkout skips the gateway and continues through paid-state handling. The diagram focuses on the nonzero-charge case.

## Stop here on the first pass

**Q11:** With on-hand 5 and reserved 2, what is available?

**Q12:** After committing those two reserved units, what are all three stock numbers?

[Answers](14-answer-key.md). Reproduce the tables on paper before reading further.

## Later pass: where the simple story stops

The current gateway is a deterministic fake for development and tests. An explicit decline is different from a timeout after a real gateway accepted a payment. A database failure after that charge also needs recovery. This code's two saves and external call do not form one atomic transaction across systems.

You do not need to implement that recovery during onboarding. Recognize the boundary and continue later with [the concurrency lesson](../docs/learning/08-concurrency.md) and [open review findings](../docs/learning/review-findings.md). A 409 or timeout is not, by itself, proof that retrying a payment workflow is safe.
