# Agora — Engineering Report

**Prepared:** 9 September 2026
**Subject:** Technical state of `agora-net`
**Audience:** Software engineers joining or reviewing this codebase

---

## TL;DR

A .NET 10 / ASP.NET Core e-commerce backend, clean-layered, SQLite-backed, with 820 passing tests. The domain modelling and concurrency work are genuinely good — better than most commerce codebases you will inherit. The gaps are at the edges: both external integrations are fakes, there is no CI, and most of the code is uncommitted.

**Verified locally on 2026-09-09:**

```
dotnet build Agora.slnx   ->  Build succeeded. 0 Warning(s), 0 Error(s)
dotnet test  Agora.slnx   ->  Passed! Failed: 0, Passed: 820, Skipped: 0, Total: 820  (2m 53s)
```

> **Environment note:** the .NET 10 SDK (10.0.400) is installed at `C:\Users\E\.dotnet\` and is **not on `PATH`**. Invoke it explicitly or use `scripts/verify.ps1`, which already falls back to that location.
>
> Also: run a **complete** build before `dotnet test --no-build`. A partially-written test assembly silently discovers a subset of tests (I hit exactly this and got 559 of 820) — the run reports success on whatever it found.

---

## Solution layout

```
Agora.slnx
├── src/Agora.Domain          ~3,100 LOC   entities, Money, domain services — zero dependencies
├── src/Agora.Infrastructure  ~3,800 LOC   EF Core persistence, services, workers, fakes
│   └── Migrations           ~50,400 LOC   26 migrations (generated)
├── src/Agora.Api             ~7,500 LOC   controllers, contracts, auth, filters, queries
└── tests/Agora.Tests        ~17,100 LOC   146 files, 820 tests (unit + integration)
```

Dependencies are deliberately minimal: EF Core 10 + SQLite, JWT bearer auth, xUnit. No AutoMapper, no MediatR, no FluentValidation. Everything is hand-rolled and readable.

| Metric | Value |
| --- | --- |
| Endpoints (`[HttpGet/Post/Put/Delete]`) | 214 |
| Controllers | 60 |
| Domain entities | 46 |
| EF migrations | 26 |
| Tests | 820 (683 `[Fact]`/`[Theory]` methods, 104 `[InlineData]` rows) |

---

## Architecture

Straightforward clean layering, correctly enforced — `Agora.Domain` has **no project references at all**, so the dependency rule cannot be violated by accident.

**Key design decisions** (documented as ADRs in [docs/adr/](../docs/adr/)):

| ADR | Decision | Why it matters |
| --- | --- | --- |
| 0001 | `decimal` stored as integer cents; tax rates as millionths | Cents would round 9.5% tax to 10%. The millionths converter is a separate deliberate mechanism. |
| 0002 | `Money` is a non-negative value object, subtraction clamps at zero | Prevents negative-total bugs when a discount exceeds subtotal |
| 0003 | Checkout is reserve → charge → commit/release | Stock cannot be oversold across concurrent buyers |
| 0005 | Order status is **derived** from shipment coverage, never commanded | Status cannot be corrupted by a bad write |
| 0006 | Optimistic concurrency on `InventoryItem`, `Cart`, `GiftCard` only | Scoped where contention actually happens |

### The checkout pipeline

[`CheckoutService.CheckoutAsync`](../src/Agora.Infrastructure/Services/CheckoutService.cs) is the most important method in the codebase:

1. Price the cart via `CheckoutPricingService` (side-effect free — shared with the read-only quote endpoint)
2. Reserve stock per line
3. Persist pending order + reservations **in one save**
4. Charge `IPaymentGateway` for `total − giftCardTender`
   - On decline: release reservations, remove the order, leave the gift card untouched, throw → 402
5. Open a transaction: redeem gift card, issue guest credential, `MarkPaid`, commit reservations, register discount use, clear cart, **stage** webhook events, save, commit

Totals order is **discounts → tax → gift card tender**. Note step 5's ordering: the provider call is already complete before the transaction opens, so only local writes sit inside it.

### Durable webhook delivery

This is the strongest piece of infrastructure in the repo, and it is worth reading before anything else. `WebhookService.StageAsync` writes an `OutboxEvent` plus frozen per-subscription deliveries **inside the caller's business transaction** — the caller owns `SaveChanges`. A `WebhookOutboxWorker` background service then transports committed events with:

- **Lease-based claiming** with generation counters, so a stalled worker cannot double-send
- **Expired-lease recovery** marking the orphaned attempt `Unknown` rather than inventing an outcome
- **Per-attempt records** with reason codes (`LeaseExpired`, `Timeout`, `TransportError`, `HttpRejected`)
- **Uncertainty modelled explicitly** — a timeout is recorded as `Unknown`, not as a failure

The distinction between "we know it failed" and "we don't know" is handled properly. That is rare.

---

## What is done well

- **Money and rounding.** `Money` enforces 2 dp away-from-zero, non-negative, currency-matched. SQLite converters keep ordering/range queries translatable.
- **Concurrency is tested, not assumed.** Real barrier-based race tests (`ConcurrencyEdgeTests`, `WishlistConcurrencyTests`, `StockReservationEdgeTests`, outbox claim/late-ack tests).
- **Migration upgrade paths are tested.** Tests seed current data, downgrade to the exact predecessor migration, then upgrade again. Very few teams do this.
- **Authorization is resource-level, not just role-level.** `GuestOrderAccessService.EnsureCanReadAsync` handles admin / owner / valid-guest-token and throws `NotFoundException` rather than `Forbid` — no existence oracle. Guest tokens use `FixedTimeEquals` against a SHA-256 digest and travel in the `X-Agora-Order-Access` **header**, not the URL, so they stay out of request-path logs.
- **Integration keys cannot escalate.** `IntegrationKeyAuthenticationHandler` issues no subject and no role claim, only `scope` claims — so a machine credential cannot inherit admin powers.
- **Session revocation is real.** `OnTokenValidated` checks a `sid` claim against `AuthenticationSessionService`, so JWTs are revocable rather than valid-until-expiry.
- **Consistent error contract.** `DomainExceptionFilter` maps every domain exception to RFC 7807 ProblemDetails with correct status codes (409 conflicts, 422 semantic, 402 payment).

---

## Findings

### 1. Both external integrations are fakes, registered unconditionally — P0

[`Program.cs:106-107`](../src/Agora.Api/Program.cs#L106-L107)

```csharp
builder.Services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
builder.Services.AddSingleton<IWebhookSender, FakeWebhookSender>();
```

No environment guard. `FakePaymentGateway` approves any token not equal to `tok_fail` or prefixed `fail`; `FakeWebhookSender` succeeds unless the URL contains `fail`. In Production this silently confirms orders without collecting money.

*Fix:* real implementations behind the existing interfaces, registered per-environment, and a startup guard that refuses to boot with a fake gateway outside Development/Testing.

### 2. Default JWT signing key ships in config — P1

[`appsettings.json`](../src/Agora.Api/appsettings.json) carries `"SigningKey": "agora-dev-signing-key-change-me-in-production-0123456789abcdef"`. `JwtOptions` has no validation attributes and no `ValidateOnStart`, so a deployment that forgets to override it boots happily with a publicly known key — anyone can mint admin tokens.

*Fix:* `.ValidateDataAnnotations().ValidateOnStart()` on `JwtOptions`, a minimum-length rule, and an explicit rejection of the known placeholder value outside Development.

Note the contrast: `ReturnPolicyOptions` **does** use `ValidateDataAnnotations().ValidateOnStart()`. The pattern exists; it just was not applied to the security-critical option.

### 3. Checkout has no reconciliation for an accepted-charge-then-crash — P1 (known)

In `CheckoutAsync`, the gateway call at step 4 sits between durable saves. Declines are cleaned up correctly, but if the process dies after the provider accepts and before the step-5 transaction commits, you are left with money captured, an order stuck `Pending`, and reservations held. Nothing reconciles this on restart.

The team identified this themselves (tracked as L6 in `review-findings.md`) — it remains genuinely open. It needs a durable intent record written *before* the provider call plus a recovery sweep, not a small patch.

### 4. SQLite is the only provider — P1

`Microsoft.EntityFrameworkCore.Sqlite` is the sole provider package. Single-writer, file-based. A production move means a provider swap plus reviewing the SQLite-specific pieces: `SqliteValueConverters`, `LocalSqliteWriteAttribute`, partial unique indexes, and the `AUTOINCREMENT` catalog sequence annotation.

### 5. Missing web hardening — P1

No `UseHttpsRedirection`, no CORS policy, no security headers, no OpenAPI/Swagger package. `AllowedHosts` is `*`.

### 6. No CI, no container, no analyzer config — P1

No `.github/`, no `Dockerfile`, no `.editorconfig`, no `Directory.Build.props`. The only automation is [`scripts/verify.ps1`](../scripts/verify.ps1), which is decent — it checks local Markdown links across 107 documents before running tests — but must be invoked by hand.

### 7. Documentation drift — P2

[`docs/learning/review-findings.md`](../docs/learning/review-findings.md) still marks as **Open**:

| Listed as open | Actual state |
| --- | --- |
| L5 — `OrdersController` does not enforce caller identity | **Closed.** `GetByNumber`, `ListFulfillments` and `Cancel` all call `EnsureCanReadAsync`; `Refund` is `[Authorize(Roles = "Admin")]`. |
| L7 — `WebhookService.DispatchAsync` sends before saving | **Closed.** Replaced by the staged outbox + worker described above. `DispatchAsync` no longer exists. |
| L5 — guest credentials appear in request paths | **Closed.** Guest tokens now travel in the `X-Agora-Order-Access` header. |

The README repeats these stale caveats on its front page. `docs/architecture.md` predates the outbox, login sessions, integration keys and export jobs entirely. Fixing this is cheap and makes the codebase read as what it actually is.

### 8. Style inconsistency in the newest code — P2

133 lines exceed 160 characters, concentrated in the Phase 2 services — `PurchaseOrderService` (18), `AccountExportService` (16), `WarehouseCoordinationService` (10), `InventoryCountService` (10). Multiple statements per line is common there:

```csharp
attempt.Finish(WebhookAttemptOutcome.Unknown, now, reasonCode: "LeaseExpired"); delivery.ExpireLease(now);
```

Phase 1 code does not read like this. An `.editorconfig` plus a formatting pass would make the newest and most complex code as approachable as the oldest.

Related: `DomainExceptionFilter` is now a 30-branch switch mixing fully-qualified and imported type names. It works, but it is the natural seam for splitting exception mapping per feature area.

### 9. Working tree is almost entirely uncommitted — P0 (process)

284 untracked paths, 67 modified tracked files, +3,714/−827 in the tracked diff. 98 of 146 test files are untracked. Phase 1 was delivered as 17 clean PRs; Phase 2 has no commits at all. Everything in this report about Phase 2 describes code that exists on exactly one disk.

---

## Getting productive

```bash
# The SDK is not on PATH:
export PATH="$HOME/.dotnet:$PATH"          # or use scripts/verify.ps1

dotnet build Agora.slnx                     # full build first — see the warning at the top
dotnet test  Agora.slnx                     # 820 tests, ~3 min
dotnet run --project src/Agora.Api          # migrates + seeds a demo catalog in Development
```

Dev seed data: 3 categories, 8 products, 14 variants, discount codes `WELCOME10` / `SAVE5` / expired `EXPIRED10`, shipping `standard` / `express` / `freight`, US and GB tax zones, admin `admin@agora.dev` / `AdminPass123!`.

Adding a migration:

```bash
dotnet tool run dotnet-ef -- migrations add <Name> \
  --project src/Agora.Infrastructure --startup-project src/Agora.Api
```

Integration tests boot the whole API through `WebApplicationFactory` against a private in-memory SQLite connection **per test class**, with rate limits relaxed via `appsettings.Testing.json`. The outbox worker and export worker are disabled under the `Testing` environment so tests drive them explicitly.

### Reading order

1. [`Money.cs`](../src/Agora.Domain/Common/Money.cs) — the value-object discipline everything else follows
2. [`CheckoutService.cs`](../src/Agora.Infrastructure/Services/CheckoutService.cs) — the core transaction
3. [`WebhookOutboxWorker.cs`](../src/Agora.Infrastructure/Services/WebhookOutboxWorker.cs) — leases, generations, uncertainty
4. [`GuestOrderAccessService.cs`](../src/Agora.Infrastructure/Services/GuestOrderAccessService.cs) — capability tokens done right
5. [`docs/adr/`](../docs/adr/) — the nine core decisions
6. [`astradocs/bootcamp/journal.md`](../astradocs/bootcamp/journal.md) — the build log, including honest failure accounts

---

## Suggested technical priorities

| # | Work | Effort |
| --- | --- | --- |
| 1 | Commit and push everything to a branch | Hours |
| 2 | `.editorconfig` + `Directory.Build.props` (warnings-as-errors, analyzers) | Hours |
| 3 | CI: build + full test suite on push | Days |
| 4 | `JwtOptions` startup validation; reject the placeholder key | Days |
| 5 | Refresh `review-findings.md`, README, `architecture.md`; add ADRs 0010+ for Phase 2 decisions | Days |
| 6 | HTTPS redirection, CORS, security headers, OpenAPI | Days |
| 7 | Real `IWebhookSender` (HTTP with retry/timeout) | Days |
| 8 | Real `IPaymentGateway` + environment guard against the fake | Weeks |
| 9 | Checkout payment reconciliation (design first) | Weeks |
| 10 | Production database provider | Weeks |

---

## Closing assessment

The parts that are hard to get right are right: money arithmetic, stock reservation under contention, derived order state, capability-based access, durable messaging with honest uncertainty handling. The parts that are missing are the parts that are well-understood and mechanical: CI, containers, real integrations, a production database.

That is a much better position to be in than the reverse. Treat the findings above as a hardening backlog, not a rescue plan — and commit the working tree before doing anything else.
