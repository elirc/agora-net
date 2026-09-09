# Architecture decision records

Short records of the decisions that shaped agora-net, written after the fact
from the code that implements them. Each one states the forces at the time, the
call, and what it cost — not just what the code does (that is
[architecture.md](../architecture.md)) but why it does it that way.

| # | Decision | Status |
| --- | --- | --- |
| [0001](0001-decimal-as-cents.md) | Store decimals as integer cents, rates as millionths | Accepted |
| [0002](0002-money-value-object.md) | Money is a non-negative value object that clamps at zero | Accepted |
| [0003](0003-reserve-charge-commit.md) | Checkout is a reserve → charge → commit/release pipeline | Accepted |
| [0004](0004-tender-ordering.md) | Totals order is discounts → tax → gift-card tender | Accepted |
| [0005](0005-derived-order-status.md) | Order status is derived from shipment coverage, never commanded | Accepted |
| [0006](0006-optimistic-concurrency.md) | Optimistic concurrency on stock, carts and gift cards only | Accepted |
| [0007](0007-hmac-webhook-signing.md) | Webhooks are HMAC-signed, logged per delivery, retried under a cap | Accepted |
| [0008](0008-guest-cart-tokens.md) | Guests are first-class: cart tokens and order-email authentication | Accepted |
| [0009](0009-catalog-query-contract.md) | Explicit catalog query validation and same-variant filtering | Accepted |

## Format

Context (the forces) → Decision (the call) → Consequences (what it bought and
what it cost). Records are immutable: to revisit one, add a new record that
supersedes it rather than editing history.
