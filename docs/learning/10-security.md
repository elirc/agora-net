# Understand who may do what

**Outcome:** review identity, ownership, and credentials at a concrete boundary. This lesson describes the repository; it does not certify deployment security.

JWT validation answers whether the API accepts a caller's identity. An Admin role check restricts a class of actions. Resource ownership asks whether this specific caller may access this specific cart, order, address, or return. These are separate questions.

## Start with a permission matrix

Choose one route and list anonymous, customer A, customer B, and admin behavior. For an address owned by A, test B with A's actual address ID. A random nonexistent ID only tests absence, not isolation. Inspect `AuthzMatrixTests` and `AddressBookApiTests` for the existing patterns.

The current `OrdersController` allows order lookup, cancellation, and refunds using an order number without authentication on those actions. This is a known review finding, not evidence of ownership enforcement. A serious follow-up needs an account-owner policy plus a designed guest credential. Adding `[Authorize]` everywhere would break existing guest flows without solving guest access requirements.

Guest cart tokens and gift-card codes act as credentials. Their possession grants access. Trace where they occur in URLs, logs, test output, and error messages. Do not put real tokens in documentation or progress notes. The development seeder's account and signing-key defaults are learning conveniences that need a deliberate deployment configuration.

## A local threat-model exercise

Draw client -> API -> database and API -> external sender. List the assets: addresses, order history, stock, gift-card balances, payment references. For each boundary ask what an untrusted caller controls and which check limits it. A URL that is harmless to `FakeWebhookSender` may become a server-side request risk when replaced by a real HTTP client; review destination and redirect handling as part of that future feature.

**Exercise:** write failing tests for account B attempting to read, cancel, and refund account A's order. Draft the guest recovery flow before implementing the authorization fix. Identify what responses should reveal about resource existence.

**Checkpoint:** produce a route matrix and one abuse case with a reproducible local test. **Stretch:** design token rotation and expiry without losing legitimate guest access. See the concrete scope in backlog ticket L5.
