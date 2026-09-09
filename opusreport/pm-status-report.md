# Agora — Product Status Report

**Prepared:** 9 September 2026
**Subject:** Delivery status, scope inventory and release readiness for `agora-net`
**Audience:** Product / delivery management

---

## Executive summary

Agora is an e-commerce platform backend delivered in two distinct phases: **15 sprints** producing the core commerce platform (all merged to `main`), followed by a **75-story implementation bootcamp** adding advanced catalog, warehouse, access-control and durable-messaging capability (complete and verified, but **not yet committed**).

Everything builds. Everything passes. The blocker to calling this "delivered" is not engineering quality — it is **release hygiene**: two-thirds of the work has never been committed to version control, and no automated pipeline exists to prove the build independently.

| Health indicator | Status |
| --- | --- |
| Build | ✅ Clean — 0 warnings, 0 errors |
| Automated tests | ✅ **820 / 820 passing**, 0 failed, 0 skipped (2m 53s) |
| Planned scope | ✅ **75 / 75** bootcamp stories complete; 15 / 15 sprints merged |
| Version control | ❌ **284 untracked paths, 67 modified files** uncommitted |
| CI/CD | ❌ None |
| Production integrations | ❌ Payment + webhook transport are simulations |
| Documentation accuracy | ⚠️ Substantially complete, partially stale |

*Verified independently on 9 September 2026 by rebuilding the solution and running the full suite — not taken from project self-reporting.*

---

## Scope delivered

### Phase 1 — Core platform (sprints 1–15, all merged via PR)

| Sprint | Capability |
| --- | --- |
| 1–2 | Solution scaffold, core domain model, EF Core persistence |
| 3–4 | Catalog CRUD, search/filter/pagination, inventory, guest carts |
| 5–6 | Checkout, orders, payments, discounts; error-contract hardening |
| 7–8 | JWT auth, accounts, admin role, address book, shipping methods |
| 9–10 | Reviews and ratings, wishlists, saved carts |
| 11–12 | Returns (RMA) and refunds, tax zones, gift cards |
| 13–15 | Partial fulfillment, admin reporting, webhooks, production hardening |

### Phase 2 — Implementation bootcamp (75 stories, complete, uncommitted)

| Tier | Count | Representative capability |
| --- | --- | --- |
| Junior | 25 | Pagination flags, SKU lookup, category slugs, stock flags, review filters |
| Mid-level | 30 | Product tags/collections, draft cloning, cart merge and templates, saved searches, gift-card ledgers, packing slips, fulfillment queue, webhook health |
| Mid/Senior | 20 | Catalog import and sync feed, category trees and option schemas, purchase orders, inventory counts, revocable sessions, scoped API keys, guest order credentials, **durable webhook outbox with replay**, background export jobs, cursor-based history |

Each story required implementation, meaningful verification, API documentation *and* learning material before being marked complete — a notably strict definition of done.

---

## Current surface area

| Measure | Count |
| --- | --- |
| API endpoints | 214 |
| Controllers | 60 |
| Domain entities | 46 |
| Database migrations | 26 |
| Hand-written code | ~31,500 lines |
| Test files / tests | 146 files / 820 tests |
| Documentation files | 107 |

---

## Risk register

### P0 — Act immediately

**Two-thirds of the codebase is uncommitted.**
284 untracked paths and 67 modified files sit in the working tree, including 98 of the 146 test files and the majority of Phase 2 feature code. There is no branch, no PR, no backup, no review trail. A disk failure loses months of work; there is also no way for anyone else to see, review or reproduce it.

*Recommendation: commit to a branch and push today. This is the highest value-per-hour action available.*

### P0 — Blocks any revenue use

**Payment and webhook delivery are simulated, unconditionally.**
`FakePaymentGateway` and `FakeWebhookSender` are the only implementations registered, in all environments including Production. The fake gateway approves every payment token that is not literally `tok_fail`. Deploying as-is would mean orders confirmed with no money collected.

*Mitigating factor: both sit behind clean interfaces (`IPaymentGateway`, `IWebhookSender`), so replacement is contained work, not redesign.*

### P1 — Blocks production deployment

| Gap | Detail |
| --- | --- |
| **SQLite only** | Single-file, single-writer database. No server provider configured. Migration to PostgreSQL/SQL Server needed before real traffic. |
| **Development signing key in config** | A placeholder JWT key ships in `appsettings.json` with no startup check that it was replaced. Silent, dangerous default. |
| **No transport security config** | No HTTPS redirection, no CORS policy, no security headers. |
| **No CI/CD** | No build server, no container image. All verification is manual. |
| **No API schema** | No OpenAPI/Swagger — external consumers must read prose docs. |

### P2 — Quality and clarity

| Issue | Detail |
| --- | --- |
| **Documentation drift** | `docs/learning/review-findings.md` lists order authorization and webhook durability as *open*; both are now **implemented**. The README repeats these stale caveats, so the project reads as less mature than it is. |
| **Architecture doc lags** | `docs/architecture.md` does not describe the outbox worker, login sessions, integration keys or export jobs. |
| **ADR coverage stops at Phase 1** | Nine ADRs cover core decisions; Phase 2's significant decisions live only in bootcamp lessons. |
| **Known open engineering item** | Checkout still calls the payment provider between durable saves with no reconciliation path if the process dies after an accepted charge. Correctly identified by the team and still genuinely open. |

---

## Release readiness checklist

Ordered by dependency, not by effort.

- [ ] **Commit and push the working tree** *(hours — do first)*
- [ ] Refresh `review-findings.md`, README and `architecture.md` to reflect what shipped *(hours)*
- [ ] Add CI running build + full test suite on every push *(days)*
- [ ] Enforce non-default JWT signing key at startup; move secrets out of config *(days)*
- [ ] Add HTTPS redirection, CORS policy and security headers *(days)*
- [ ] Publish an OpenAPI document *(days)*
- [ ] Implement a real payment provider behind `IPaymentGateway` *(weeks)*
- [ ] Implement a real HTTP webhook sender behind `IWebhookSender` *(days)*
- [ ] Migrate persistence to a production database provider *(weeks)*
- [ ] Containerise and define a deployment target *(weeks)*
- [ ] Close the checkout payment-reconciliation gap *(weeks — design first)*

---

## What is genuinely strong here

It would misrepresent this project to lead only with gaps. Worth recording:

- **The test suite is real.** 820 tests covering concurrency races, migration upgrade paths, transactional rollback, exact monetary boundaries and authorization matrices — not surface-level coverage.
- **The definition of done was enforced.** Stories required tests, docs *and* teaching material. That discipline is visible in the result.
- **The engineering journal is unusually honest.** It records failed regressions (760/809, then 812/818, then 820/820), explains root causes, and explicitly refuses to claim green results it had not earned. That is a trustworthy delivery signal.
- **The hard problems were solved properly.** Money arithmetic, stock reservation under concurrency, derived order state, durable event delivery with leases and attempt history — these are where commerce systems usually rot, and here they are correct and proven.

---

## Recommended next sprint

1. **Preserve the work** — commit, push, open a PR for Phase 2. *(Non-negotiable, first.)*
2. **Tell the truth in the docs** — reconcile findings, README and architecture with what actually shipped.
3. **Automate the proof** — CI running build + tests, so "820 passing" is a fact anyone can see rather than a claim.

Only after those three does it make sense to debate production integrations — and that debate should start with a decision on whether Agora is a training asset or a product, because the answer changes everything downstream.
