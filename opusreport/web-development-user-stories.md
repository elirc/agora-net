# 25 web-development user stories with full implementation plans

[Executive brief](executive-brief.md) · [PM status report](pm-status-report.md) · [Engineering report](engineering-report.md)
Existing story sets: [25 junior](../astradocs/16-junior-user-stories.md) · [30 mid-level](../astradocs/17-midlevel-user-stories.md) · [20 mid/senior](../astradocs/18-mid-senior-user-stories.md)

**Planning document only. None of the 25 stories below is implemented.** They were chosen after reviewing the working tree on 9 September 2026 against the 75 already-completed stories, specifically to cover **web-development topics the existing curriculum does not yet touch**. Every "current behavior" note was verified against the code in this repository. Expected results describe what you will build, not what the API returns today.

## Why these 25

The existing 75 stories cover commerce domain modelling, persistence, concurrency, authorization and durable messaging extremely well. What they do not cover is the **HTTP and operational surface** of a web application. Nothing in the repository currently implements CORS, OpenAPI, API versioning, response compression, output caching, conditional writes, correlation IDs, tracing, metrics, file upload, real-time streaming, full-text search, email-bearing account flows, or request idempotency.

These stories fill exactly those gaps. Each one also **reinforces a concept the codebase already teaches**, so they extend rather than replace what you have learned.

| Gap covered | Reinforces |
| --- | --- |
| HTTP caching, conditional writes, content negotiation | Optimistic concurrency ([ADR 0006](../docs/adr/0006-optimistic-concurrency.md)) |
| Middleware, correlation, tracing, metrics | The request pipeline in [Program.cs](../src/Agora.Api/Program.cs) |
| Idempotency, SSRF defence, lockout, token flows | Reserve→charge→commit ([ADR 0003](../docs/adr/0003-reserve-charge-commit.md)) and capability tokens |
| Upload, streaming, search, caching | Bounded reads and response-size budgets |
| Versioning, deprecation, OpenAPI | The published error contract and API reference |

---

## Difficulty tiers

| Tier | Stories | What changes | Typical effort |
| --- | --- | --- | --- |
| **Foundation** | WD-01 – WD-06 | One or two files, no migration, no package | Half a day each |
| **Intermediate** | WD-07 – WD-15 | New middleware or options, sometimes a migration or package | 1–3 days each |
| **Advanced** | WD-16 – WD-22 | New entities, workers, or cross-cutting behaviour | 3–5 days each |
| **Expert** | WD-23 – WD-25 | Architectural: observability, data lifecycle, money | 1–2 weeks each, design review first |

---

## Pick a story

| ID | Tier | User benefit | Main practice | Migration | New package |
| --- | --- | --- | --- | --- | --- |
| [WD-01](#wd-01-correlation-id-on-every-response) | Foundation | Trace one request through the logs | Custom middleware | No | No |
| [WD-02](#wd-02-security-response-headers) | Foundation | Browser clients are protected by default | Defence in depth | No | No |
| [WD-03](#wd-03-machine-readable-error-codes) | Foundation | Clients branch on a stable code, not prose | Error contract design | No | No |
| [WD-04](#wd-04-deprecation-and-sunset-headers) | Foundation | Integrators learn a route is going away | API lifecycle | No | No |
| [WD-05](#wd-05-detailed-readiness-report) | Foundation | Operators see *which* dependency is down | Health check contracts | No | No |
| [WD-06](#wd-06-cache-control-for-public-catalog-reads) | Foundation | Catalog pages get faster and cheaper | Public vs private caching | No | No |
| [WD-07](#wd-07-cors-for-browser-clients) | Intermediate | A web storefront can call the API | Preflight and origins | No | No |
| [WD-08](#wd-08-an-openapi-document) | Intermediate | Clients generate SDKs from a schema | API discoverability | No | Yes |
| [WD-09](#wd-09-url-api-versioning) | Intermediate | Breaking changes without breaking clients | Versioning strategy | No | Optional |
| [WD-10](#wd-10-conditional-writes-with-if-match) | Intermediate | Two admins cannot silently overwrite each other | Conditional writes, 412 | No | No |
| [WD-11](#wd-11-response-compression) | Intermediate | Large catalog responses transfer faster | Compression and `Vary` | No | No |
| [WD-12](#wd-12-tiered-rate-limiting-with-standard-headers) | Intermediate | Clients can back off politely | Rate-limit policies | No | No |
| [WD-13](#wd-13-account-lockout-after-failed-logins) | Intermediate | Password guessing becomes impractical | Brute-force defence | Yes | No |
| [WD-14](#wd-14-email-verification-flow) | Intermediate | Only real address owners transact | Token lifecycle | Yes | No |
| [WD-15](#wd-15-password-reset-with-single-use-tokens) | Intermediate | Locked-out users recover safely | Single-use secrets | Yes | No |
| [WD-16](#wd-16-idempotency-keys-for-checkout) | Advanced | A retried checkout never double-charges | Durable operation identity | Yes | No |
| [WD-17](#wd-17-ssrf-protection-for-webhook-urls) | Advanced | Webhooks cannot probe internal networks | SSRF defence | No | No |
| [WD-18](#wd-18-product-image-upload) | Advanced | Admins upload images instead of pasting URLs | Multipart and content sniffing | Yes | No |
| [WD-19](#wd-19-order-status-event-stream) | Advanced | Buyers watch fulfillment live | Server-Sent Events | No | No |
| [WD-20](#wd-20-full-text-product-search-with-ranking) | Advanced | Relevant results, not just substring matches | SQLite FTS5 and ranking | Yes | No |
| [WD-21](#wd-21-timezone-correct-sales-reporting) | Advanced | "Yesterday" means the merchant's yesterday | Calendar vs instant | No | No |
| [WD-22](#wd-22-output-caching-with-explicit-invalidation) | Advanced | Hot catalog reads skip the database | Cache coherence | No | No |
| [WD-23](#wd-23-tracing-and-metrics-for-checkout) | Expert | Diagnose a slow checkout in production | OpenTelemetry | No | Yes |
| [WD-24](#wd-24-soft-delete-with-retention-and-purge) | Expert | Recover mistakes; honour erasure requests | Data lifecycle | Yes | No |
| [WD-25](#wd-25-multi-currency-with-rate-snapshots) | Expert | Shoppers see prices in their own currency | Money at scale | Yes | No |

**A good route:** WD-01 → WD-03 → WD-06 → WD-10 → WD-12 → WD-16 → WD-23. That path moves from a single middleware to full request observability, and each step reuses the previous one.

**Real prerequisites** (the only ones): WD-15 builds on the token pattern from WD-14. WD-22 builds on the cache semantics from WD-06. WD-23 builds on the correlation ID from WD-01. Everything else is independent.

---

## Before you start any story

1. **Commit the working tree first.** As of this review, 284 paths are untracked. Do not add new work on top of unsaved work. See the [engineering report](engineering-report.md).
2. Build completely before testing. `dotnet test --no-build` against a partially built assembly silently runs a *subset* of tests and reports success. Always `dotnet build Agora.slnx` to completion first.
3. The .NET 10 SDK is at `C:\Users\E\.dotnet\` and is **not on `PATH`**. Use [scripts/verify.ps1](../scripts/verify.ps1) or add it explicitly.
4. Establish your baseline: run the story's named test class before editing.
5. Write the smallest failing test for the new behaviour before implementing it.
6. Before submitting: `dotnet test Agora.slnx` in full, inspect your diff, and update [docs/api-reference.md](../docs/api-reference.md). Report the count you actually observed.

## Shared recipes for this story set

**Adding middleware.** There is no `Middleware/` folder yet; WD-01 creates it. Register middleware in [Program.cs](../src/Agora.Api/Program.cs) between `app.UseHttpLogging()` and `app.UseAuthentication()` unless a story says otherwise. Order matters: anything that must appear on *error* responses has to run **before** `app.UseExceptionHandler()`.

**Adding a package.** Only WD-08 and WD-23 require one (WD-09 optionally). Pin the exact version alongside the existing `10.0.10` references, and explain the dependency in your review note. Every other story uses the framework as-is.

**Testing headers.** `HttpResponseMessage` splits headers between `response.Headers` and `response.Content.Headers`. `Cache-Control` and `ETag` live on `response.Headers`; `Content-Type` and `Content-Encoding` live on `response.Content.Headers`. Asserting on the wrong collection is the most common failure in this story set.

**Testing middleware in isolation.** Use `AgoraApiFactory.WithWebHostBuilder` to override configuration per test, as [ProductionReadinessTests](../tests/Agora.Tests/Integration/ProductionReadinessTests.cs) already does for rate limits. Do not mutate shared fixture state.

**Fixtures.** Use [AgoraApiFactory.WithDbAsync](../tests/Agora.Tests/Integration/AgoraApiFactory.cs) and [TestAuth](../tests/Agora.Tests/Integration/TestAuth.cs). A class fixture shares one database across its methods, so give each scenario unique GUID-based emails, slugs and SKUs. Ownership tests need customer A, customer B and a real resource owned by B — a random missing ID proves nothing.

---

# Foundation tier

Small, self-contained changes. No migration, no new package, no background worker. Each one is a complete web-development concept in miniature.

## WD-01: Correlation ID on every response

**Status:** Planned; not implemented. **Tier:** Foundation. **Starting knowledge:** classes, `async`, one integration test.

**User story:** As an operator diagnosing a customer complaint, I want every response to carry a correlation ID that also appears in the log line for that request, so I can find the exact request in the logs from a screenshot.

**Current behavior:** [Program.cs](../src/Agora.Api/Program.cs) enables `AddHttpLogging` with method, path, status and duration. Nothing ties a log line to a specific response, and there is no `Middleware/` folder.

**Acceptance criteria**

- Every response — success, 4xx and 5xx alike — carries an `X-Correlation-Id` header.
- If the request supplies `X-Correlation-Id`, that value is echoed back; otherwise the server generates one.
- A supplied value is accepted only if it is 8–128 characters of ASCII letters, digits, hyphen or underscore. Anything else is replaced by a generated ID rather than rejected — observability must never fail a request.
- The value is attached to the logging scope, so log lines emitted during the request include it.
- The header appears on responses produced by the exception handler, which means the middleware runs **before** `UseExceptionHandler`.

**Files to open:** [Program.cs](../src/Agora.Api/Program.cs), [ApiHardeningTests.cs](../tests/Agora.Tests/Integration/ApiHardeningTests.cs). Proposed new files: `src/Agora.Api/Middleware/CorrelationIdMiddleware.cs`, `tests/Agora.Tests/Integration/CorrelationIdTests.cs`.

**Implementation plan**

1. Create `src/Agora.Api/Middleware/`. Write `CorrelationIdMiddleware` with the conventional shape: a constructor taking `RequestDelegate` and an `InvokeAsync(HttpContext)` method.
2. Read the inbound header. Write a private `static bool IsAcceptable(string?)` implementing the character and length rule. Generate `Guid.NewGuid().ToString("N")` when unacceptable.
3. Set the response header using `context.Response.OnStarting(...)`. Assigning directly is unreliable because a later component may have started the response.
4. Wrap the rest of the pipeline in `logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id })` so log lines inherit it.
5. Register with `app.UseMiddleware<CorrelationIdMiddleware>()` immediately after `app.UseHttpLogging()` and before `app.UseExceptionHandler()`.
6. Store the ID on `context.Items` under a public constant key so later stories (WD-03, WD-23) can read it without re-parsing.
7. Write `CorrelationIdTests`: a generated ID on a plain `GET /health`; an echoed valid ID; a rejected-and-replaced malformed ID; and an ID present on a 404.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~CorrelationIdTests"`. Demo with `curl -i http://localhost:5000/health -H "X-Correlation-Id: my-trace-1"`.

**Common trap:** setting the header after `await _next(context)`. By then the response may already be sent and the write is silently discarded. Use `OnStarting`.

**Done when:** all four cases pass, the ID appears on an error response, and you can explain why observability middleware registers before the exception handler.

---

## WD-02: Security response headers

**Status:** Planned; not implemented. **Tier:** Foundation. **Starting knowledge:** middleware (WD-01 helps but is not required).

**User story:** As a security reviewer, I want the API to send standard protective headers on every response, so that a browser consuming it cannot be tricked into MIME-sniffing, framing or leaking referrers.

**Current behavior:** No security headers are set anywhere. There is no `UseHttpsRedirection`, no HSTS, and `AllowedHosts` is `*`.

**Acceptance criteria**

- Every response carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` and `Referrer-Policy: no-referrer`.
- Outside Development, responses also carry `Strict-Transport-Security` with a configurable `max-age` (default one year).
- HSTS is **not** sent in Development, because it would poison `localhost` in the developer's browser for a year.
- A `SecurityHeaderOptions` class binds from a `SecurityHeaders` configuration section and is validated with `ValidateDataAnnotations().ValidateOnStart()`.
- Existing headers are never overwritten — a controller that deliberately set a value keeps it.

**Files to open:** [Program.cs](../src/Agora.Api/Program.cs), [appsettings.json](../src/Agora.Api/appsettings.json). Proposed new files: `src/Agora.Api/Middleware/SecurityHeadersMiddleware.cs`, `src/Agora.Api/SecurityHeaderOptions.cs`, `tests/Agora.Tests/Integration/SecurityHeadersTests.cs`.

**Implementation plan**

1. Write `SecurityHeaderOptions` with `SectionName = "SecurityHeaders"`, a `HstsMaxAgeDays` integer with `[Range(1, 730)]`, and an `EnableHsts` boolean.
2. Copy the validation registration pattern already used by `ReturnPolicyOptions` in [Program.cs](../src/Agora.Api/Program.cs) — `.Bind(...).ValidateDataAnnotations().ValidateOnStart()`.
3. Write the middleware. Use `OnStarting` again, and set each header only when `!context.Response.Headers.ContainsKey(name)`.
4. Inject `IHostEnvironment` and suppress HSTS when `IsDevelopment()`.
5. Register immediately after the correlation middleware.
6. Add the `SecurityHeaders` block to [appsettings.json](../src/Agora.Api/appsettings.json) with documented defaults.
7. Write tests for: the three always-on headers; HSTS absent in Testing/Development; HSTS present when the environment is overridden via `WithWebHostBuilder(b => b.UseEnvironment("Production"))`; and a controller-set header surviving untouched.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~SecurityHeadersTests"`. Demo with `curl -i http://localhost:5000/api/products`.

**Common trap:** enabling HSTS unconditionally. A developer who loads `https://localhost` once will have their browser force HTTPS on that host for a year, breaking unrelated local projects.

**Done when:** all header cases pass, startup fails fast on an out-of-range `max-age`, and you can explain what each header actually prevents.

---

## WD-03: Machine-readable error codes

**Status:** Planned; not implemented. **Tier:** Foundation. **Starting knowledge:** pattern matching, `ProblemDetails`.

**User story:** As a client developer, I want each error response to carry a stable machine-readable code, so I can branch on `INSUFFICIENT_STOCK` instead of string-matching an English sentence that may be reworded.

**Current behavior:** [DomainExceptionFilter](../src/Agora.Api/Filters/DomainExceptionFilter.cs) maps roughly 30 exception types to a status code and a human `Title`, and copies `Exception.Message` into `Detail`. Clients have nothing stable to branch on.

**Acceptance criteria**

- Every ProblemDetails produced by the filter includes an `errorCode` extension: `SCREAMING_SNAKE_CASE`, stable across releases.
- Codes are derived from an explicit mapping, never from `GetType().Name` — renaming a C# class must not break clients.
- Examples: `InsufficientStockException` → `INSUFFICIENT_STOCK`; `PaymentFailedException` → `PAYMENT_FAILED`; `InvalidGiftCardException` → `INVALID_GIFT_CARD`; bare `DomainException` → `DOMAIN_RULE_VIOLATION`.
- The existing `status`, `title` and `detail` values are unchanged, as are the two existing extensions (`issues`, `variants`).
- Every code appears in a table in the API reference.

**Files to open:** [DomainExceptionFilter.cs](../src/Agora.Api/Filters/DomainExceptionFilter.cs), [docs/api-reference.md](../docs/api-reference.md), [ApiHardeningTests.cs](../tests/Agora.Tests/Integration/ApiHardeningTests.cs). Proposed new file: `tests/Agora.Tests/Unit/ErrorCodeCatalogTests.cs`.

**Implementation plan**

1. Read the existing switch. Note it already returns a tuple `(statusCode, title)`; you are widening it to `(statusCode, title, errorCode)`.
2. Extend each arm with its literal code string. Do this mechanically, one arm at a time, so no exception type is missed.
3. Set `problem.Extensions["errorCode"] = errorCode;` before assigning `context.Result`.
4. Write `ErrorCodeCatalogTests` as a reflection test: enumerate every type assignable to `DomainException` in the Domain assembly and assert the filter yields a non-empty code for each. This makes a future unmapped exception fail the build rather than ship as `null`.
5. Add an assertion to an existing hardening test that a real 409 stock conflict carries `errorCode: "INSUFFICIENT_STOCK"`.
6. Add the code catalogue table to [docs/api-reference.md](../docs/api-reference.md) beside the existing error-contract section.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~ErrorCodeCatalogTests|FullyQualifiedName~ApiHardeningTests"`.

**Common trap:** generating the code from the type name at runtime. It is less code and it is wrong — it silently turns an internal refactor into a breaking API change.

**Done when:** the reflection test passes for every domain exception, the catalogue is documented, and you can explain why `title` is not a substitute for `errorCode`.

---

## WD-04: Deprecation and Sunset headers

**Status:** Planned; not implemented. **Tier:** Foundation. **Starting knowledge:** action filters or attributes, date formatting.

**User story:** As an integrator, I want a deprecated endpoint to tell me it is going away and when, so I can migrate before it is removed instead of discovering the removal in production.

**Current behavior:** No endpoint advertises deprecation. `POST /api/orders/{number}/fulfill` is a legacy route superseded by `POST /api/orders/{number}/fulfillments`, and it binds a type literally named `LegacyFulfillRequest` — but nothing tells a caller that.

**Acceptance criteria**

- A reusable `[Deprecated]` attribute marks an action and sets three headers: `Deprecation: true`, `Sunset` as an HTTP-date (RFC 7231), and `Link` with `rel="successor-version"` pointing at the replacement route.
- The attribute takes the sunset date as an ISO string and the successor as a relative path.
- The route continues to work exactly as before — deprecation announces, it does not degrade.
- The legacy fulfill route is the first consumer.
- An invalid date literal fails at startup or first use with a clear message, not silently.

**Files to open:** [OrdersController.cs](../src/Agora.Api/Controllers/OrdersController.cs), [FulfillmentsApiTests.cs](../tests/Agora.Tests/Integration/FulfillmentsApiTests.cs), [docs/api-reference.md](../docs/api-reference.md). Proposed new files: `src/Agora.Api/Filters/DeprecatedAttribute.cs`, `tests/Agora.Tests/Integration/DeprecationHeaderTests.cs`.

**Implementation plan**

1. Write `DeprecatedAttribute : ActionFilterAttribute` with constructor parameters `string sunsetUtc` and `string successorPath`.
2. Parse the date once in the constructor with `DateTimeOffset.ParseExact` and the round-trip format; throw `ArgumentException` on failure so a typo surfaces immediately.
3. Override `OnResultExecuting` and set the three headers. Format `Sunset` with `"R"`, which produces the required HTTP-date form.
4. Build the `Link` value as `<{successorPath}>; rel="successor-version"`.
5. Apply `[Deprecated("2027-01-01T00:00:00Z", "/api/orders/{number}/fulfillments")]` to the `Fulfill` action.
6. Write tests asserting the three headers on a successful legacy fulfill, that the body is unchanged, and that the modern route carries none of them.
7. Document the deprecation and its date in the API reference.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~DeprecationHeaderTests|FullyQualifiedName~FulfillmentsApiTests"`.

**Common trap:** formatting `Sunset` with `ToString()` or an ISO format. The specification requires an HTTP-date; `"R"` is the correct format specifier.

**Done when:** headers are present and correct, existing fulfillment behaviour is untouched, and you can explain why deprecation must not change behaviour.

---

## WD-05: Detailed readiness report

**Status:** Planned; not implemented. **Tier:** Foundation. **Starting knowledge:** health checks, JSON writing.

**User story:** As an operator, I want `/health/ready` to report each dependency separately with timing, so I can tell a database outage from a slow-but-working one without reading application logs.

**Current behavior:** [Program.cs](../src/Agora.Api/Program.cs) maps `/health/ready` with the default writer, which returns the single word `Healthy` as `text/plain`. One check is registered: [DatabaseHealthCheck](../src/Agora.Api/Health/DatabaseHealthCheck.cs). [HealthController](../src/Agora.Api/Controllers/HealthController.cs) serves liveness separately.

**Acceptance criteria**

- `/health/ready` returns `application/json` with `status`, `totalDurationMs`, and an `entries` array of `{ name, status, durationMs, description }`.
- Overall status is `Healthy`, `Degraded` or `Unhealthy`; HTTP status is 200 for Healthy/Degraded and 503 for Unhealthy.
- `Cache-Control: no-store` is set — a cached readiness probe is worse than none.
- Exception messages are **not** included in `description` outside Development. A readiness endpoint is usually unauthenticated and must not leak connection strings.
- The existing `/health` liveness endpoint and [HealthEndpointTests](../tests/Agora.Tests/Integration/HealthEndpointTests.cs) are unchanged.
- A second check named `migrations` reports whether pending EF migrations exist.

**Files to open:** [Program.cs](../src/Agora.Api/Program.cs), [DatabaseHealthCheck.cs](../src/Agora.Api/Health/DatabaseHealthCheck.cs), [HealthEndpointTests.cs](../tests/Agora.Tests/Integration/HealthEndpointTests.cs). Proposed new files: `src/Agora.Api/Health/HealthReportWriter.cs`, `src/Agora.Api/Health/PendingMigrationsHealthCheck.cs`, `tests/Agora.Tests/Integration/ReadinessReportTests.cs`.

**Implementation plan**

1. Write `HealthReportWriter.WriteAsync(HttpContext, HealthReport)` using `Utf8JsonWriter`. Set the content type and `no-store` explicitly.
2. Pass it via `app.MapHealthChecks("/health/ready", new HealthCheckOptions { ResponseWriter = HealthReportWriter.WriteAsync })`.
3. Inject `IHostEnvironment` into the writer and include `entry.Description` only in Development; otherwise emit a fixed string per status.
4. Write `PendingMigrationsHealthCheck` calling `db.Database.GetPendingMigrationsAsync()`. Report `Degraded` when any are pending — the app is serving, but it is serving against an old schema.
5. Register it as `.AddCheck<PendingMigrationsHealthCheck>("migrations")`.
6. Write tests: JSON shape and content type; both entries present by name; `no-store` header; 503 when a check is forced unhealthy via a test-only registration; liveness untouched.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~ReadinessReportTests|FullyQualifiedName~HealthEndpointTests"`. Demo with `curl -s localhost:5000/health/ready | jq`.

**Common trap:** returning 503 for `Degraded`. Degraded means "working with reduced capability"; returning 503 makes a load balancer remove a node that is still serving correctly.

**Done when:** the JSON contract is stable, no exception text leaks outside Development, and you can articulate the difference between liveness and readiness.

---

## WD-06: Cache-Control for public catalog reads

**Status:** Planned; not implemented. **Tier:** Foundation. **Starting knowledge:** HTTP caching basics.

**User story:** As a shopper on a slow connection, I want catalog pages to be cacheable by my browser and any shared cache, so repeat browsing is fast and the origin does less work.

**Current behavior:** `Cache-Control` is set 79 times in the codebase — but only as `private, no-store` (70), `no-store` (8) and `no-cache` (1), all protecting personal data. **No public catalog read sets any caching policy at all**, so shared caches must treat responses conservatively. Verify with `grep -n CacheControl` in [ProductsController.cs](../src/Agora.Api/Controllers/ProductsController.cs) — there are no matches.

**Acceptance criteria**

- Anonymous `GET /api/products`, `GET /api/products/{id}`, `GET /api/products/by-slug/{slug}` and `GET /api/categories` return `Cache-Control: public, max-age=60`.
- The same routes return `private, no-store` when the request is authenticated, because a signed-in response may vary by identity.
- Responses carry `Vary: Authorization` so a shared cache never serves an authenticated response to an anonymous client.
- The `max-age` is configurable through a `Caching:CatalogSeconds` setting (default 60, range 0–3600). Zero means `no-store`.
- **No existing `private, no-store` route changes.** Personal data caching policy is out of scope; touching it is a bug.

**Files to open:** [ProductsController.cs](../src/Agora.Api/Controllers/ProductsController.cs), [CategoriesController.cs](../src/Agora.Api/Controllers/CategoriesController.cs), [ProductsApiTests.cs](../tests/Agora.Tests/Integration/ProductsApiTests.cs). Proposed new files: `src/Agora.Api/Filters/PublicCacheAttribute.cs`, `tests/Agora.Tests/Integration/CatalogCachingTests.cs`.

**Implementation plan**

1. List every genuinely public catalog read. Write the list down before coding; anything personal stays out.
2. Write `PublicCacheAttribute : ActionFilterAttribute`. In `OnResultExecuting`, inspect `context.HttpContext.User.Identity?.IsAuthenticated`.
3. Anonymous: set `public, max-age={n}`. Authenticated: set `private, no-store`. Always append `Vary: Authorization`.
4. Read the configured duration from `IOptions<CachingOptions>` resolved from `context.HttpContext.RequestServices`; treat `0` as `no-store`.
5. Apply the attribute to the listed actions only.
6. Write tests: anonymous public header; authenticated private header; `Vary` present in both; `max-age=0` producing `no-store`; and an unchanged personal route such as `/api/me/orders` still returning `private, no-store`.
7. Document the policy in the API reference so integrators know responses may be up to 60 seconds stale.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~CatalogCachingTests"`. Demo by requesting a product with and without a bearer token and comparing headers.

**Common trap:** setting `public` on a route that varies by identity and forgetting `Vary`. A shared cache will then serve one user's response to another. This is a real vulnerability class, not a performance nit.

**Done when:** all five cases pass, no personal route changed, and you can explain what `Vary` protects against.

---

# Intermediate tier

These introduce configuration, new pipeline components, or a small schema change. Several are security-sensitive: read the whole plan before starting.

## WD-07: CORS for browser clients

**Status:** Planned; not implemented. **Tier:** Intermediate. **Starting knowledge:** the browser same-origin policy.

**User story:** As a storefront front-end developer, I want the API to accept cross-origin requests from approved origins, so a browser application can call it without a proxy.

**Current behavior:** No CORS service or middleware is registered. `grep -rn "AddCors\|UseCors" src/` returns nothing. Any browser call from another origin fails at the preflight.

**Acceptance criteria**

- Allowed origins come from configuration (`Cors:AllowedOrigins`), never hard-coded, and never `AllowAnyOrigin()` combined with credentials.
- A preflight `OPTIONS` from an allowed origin returns 204 with `Access-Control-Allow-Origin`, `-Methods`, `-Headers` and `Access-Control-Max-Age`.
- A request from an unlisted origin receives **no** `Access-Control-Allow-Origin` header. It is not a 403 — the browser enforces this, not the server.
- `Authorization` and `X-Agora-Order-Access` are in the allowed request headers; `X-Correlation-Id` (WD-01) and `ETag` are in the **exposed** response headers, or JavaScript cannot read them.
- Credentials are allowed only when the origin list is non-empty and does not contain `*`; startup fails if that combination is configured.

**Files to open:** [Program.cs](../src/Agora.Api/Program.cs), [appsettings.json](../src/Agora.Api/appsettings.json). Proposed new files: `src/Agora.Api/CorsOptions.cs`, `tests/Agora.Tests/Integration/CorsPolicyTests.cs`.

**Implementation plan**

1. Add `CorsOptions` with `AllowedOrigins` as `string[]` and `AllowCredentials` as `bool`; bind from the `Cors` section.
2. Validate on start: if `AllowCredentials` is true and any origin is `*`, throw with an explanatory message. This combination is forbidden by the spec and browsers reject it anyway.
3. Register a single named policy with `WithOrigins(...)`, `AllowAnyMethod()`, `WithHeaders("Authorization", "Content-Type", "X-Agora-Order-Access", "X-Correlation-Id")`, `WithExposedHeaders("ETag", "X-Correlation-Id")` and `SetPreflightMaxAge(TimeSpan.FromHours(1))`.
4. Call `app.UseCors(policyName)` **after** `UseHttpLogging`/correlation and **before** `UseAuthentication`. Placement matters: a preflight carries no credentials and must not reach auth.
5. Add a `Cors` section to [appsettings.json](../src/Agora.Api/appsettings.json) with an empty array default, so the API is closed until deliberately opened.
6. Write tests using `WithWebHostBuilder` to inject an allowed origin: allowed-origin preflight; disallowed origin receives no allow header; exposed headers listed; a normal same-origin request unaffected.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~CorsPolicyTests"`. Demo with `curl -i -X OPTIONS localhost:5000/api/products -H "Origin: https://shop.example" -H "Access-Control-Request-Method: GET"`.

**Common trap:** believing CORS is an access control. It is a **browser** protection. A rejected origin still executes the request server-side unless you also check authorization. CORS never replaces `[Authorize]`.

**Done when:** all four cases pass, the default configuration allows nothing, and you can explain why credentials plus wildcard is refused.

---

## WD-08: An OpenAPI document

**Status:** Planned; not implemented. **Tier:** Intermediate. **New package required.** **Starting knowledge:** attributes, JSON schema concepts.

**User story:** As an integrator, I want a machine-readable OpenAPI description of the API, so I can generate a client rather than transcribing 214 endpoints from prose.

**Current behavior:** No OpenAPI or Swashbuckle package is referenced. The only contract description is [docs/api-reference.md](../docs/api-reference.md), 531 lines of prose covering roughly 116 distinct routes — well short of the 214 endpoints that exist.

**Acceptance criteria**

- `GET /openapi/v1.json` serves a valid OpenAPI 3.x document describing every controller action.
- Both security schemes appear: HTTP bearer JWT, and the `X-Agora-Api-Key` API-key scheme used by [IntegrationKeyAuthenticationHandler](../src/Agora.Api/Auth/IntegrationKeyAuthenticationHandler.cs).
- Actions annotated with `[Authorize(Roles = "Admin")]` are marked as requiring the bearer scheme.
- Error responses reference a shared `ProblemDetails` schema, including the `errorCode` extension if WD-03 is done.
- The document is served in Development by default and gated behind a `Swagger:Enabled` flag elsewhere.
- A test asserts the document parses and contains a known route, so the schema cannot silently break.

**Files to open:** [Agora.Api.csproj](../src/Agora.Api/Agora.Api.csproj), [Program.cs](../src/Agora.Api/Program.cs), [docs/api-reference.md](../docs/api-reference.md). Proposed new file: `tests/Agora.Tests/Integration/OpenApiDocumentTests.cs`.

**Implementation plan**

1. Add `Microsoft.AspNetCore.OpenApi` pinned to the same `10.0.x` line as the other ASP.NET packages. Prefer the built-in package over Swashbuckle — fewer dependencies, and this project deliberately keeps them minimal.
2. Register `builder.Services.AddOpenApi()` and map the endpoint with `app.MapOpenApi()` under the environment/flag condition.
3. Add a document transformer registering both security schemes and applying bearer requirements to actions carrying an authorize attribute.
4. Sweep the controllers adding `[ProducesResponseType]` for the documented status codes. Do this per feature area, not in one pass — it is the bulk of the work and is easy to get wrong in bulk.
5. Set `<GenerateDocumentationFile>true</GenerateDocumentationFile>` so existing XML doc comments become descriptions. Expect new warnings; fix or scope the suppression deliberately.
6. Write `OpenApiDocumentTests`: fetch the document, parse it as JSON, assert `openapi` version, assert both security schemes exist, and assert a known path such as `/api/products` is present with a `get` operation.
7. Update the API reference to point at the generated document as the authoritative contract, keeping the prose for semantics the schema cannot express.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~OpenApiDocumentTests"`. Demo by opening `/openapi/v1.json`.

**Common trap:** treating the generated document as complete documentation. A schema shows *shapes*; it cannot express that totals follow discounts → tax → gift-card tender. Keep the prose for meaning.

**Done when:** the document validates, both schemes are described, and the reference explains which document governs what.

---

## WD-09: URL API versioning

**Status:** Planned; not implemented. **Tier:** Intermediate. **Starting knowledge:** routing, controller inheritance.

**User story:** As an integrator, I want the API to be explicitly versioned, so a breaking change ships as a new version instead of breaking my running client.

**Current behavior:** Routes are unversioned (`[Route("api/products")]`). There is no mechanism to introduce a breaking change without breaking every existing client at once.

**Acceptance criteria**

- All existing routes remain reachable **unchanged** at their current unversioned paths. This is non-negotiable: 214 endpoints have callers.
- The same actions are additionally reachable under `/api/v1/...`.
- Every response carries `X-Api-Version` naming the version that served it.
- A worked example demonstrates a v2 that differs from v1 on one route, proving the mechanism carries real divergence rather than only duplicating paths.
- Requesting an unsupported version returns 400 with a ProblemDetails listing supported versions.
- The versioning strategy is recorded as a new ADR.

**Files to open:** [Program.cs](../src/Agora.Api/Program.cs), [ProductsController.cs](../src/Agora.Api/Controllers/ProductsController.cs), [docs/adr/](../docs/adr/). Proposed new files: `docs/adr/0010-api-versioning.md`, `tests/Agora.Tests/Integration/ApiVersioningTests.cs`.

**Implementation plan**

1. Decide URL-segment versioning and write the ADR **first**, covering: why URL over header, how long a version is supported, and what counts as breaking. The decision matters more than the code.
2. Choose the mechanism. `Asp.Versioning.Mvc` is the conventional package; alternatively use a route-template constant plus a second `[Route]` attribute, which adds no dependency. Record the trade-off in the ADR.
3. Add the versioned route alongside the existing one so both resolve to the same action. Verify no route ambiguity at startup.
4. Add `X-Api-Version` via a small filter or the middleware from WD-01/WD-02.
5. Build the worked v2 example on one narrow route — a renamed or restructured field on `GET /api/products/{id}` is enough. Keep v1's shape byte-identical.
6. Write tests: unversioned route unchanged; `/api/v1/` equivalent returns the same body; v2 returns the changed shape; unknown version returns 400 with supported versions listed; the header is present.
7. Document the version policy and deprecation timeline in the API reference, linking WD-04's `Sunset` mechanism.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~ApiVersioningTests|FullyQualifiedName~ProductsApiTests"`.

**Common trap:** versioning everything at once and calling it done. Version numbers without a written support policy just move the breakage; the ADR is the deliverable.

**Done when:** old routes still work, v1 and v2 both resolve, the ADR is committed, and you can name three changes that would *not* require a new version.

---

## WD-10: Conditional writes with If-Match

**Status:** Planned; not implemented. **Tier:** Intermediate. **Starting knowledge:** ETags, optimistic concurrency ([ADR 0006](../docs/adr/0006-optimistic-concurrency.md)).

**User story:** As an admin editing a product, I want my update rejected if someone else changed it since I loaded the page, so two admins cannot silently overwrite each other's work.

**Current behavior:** Conditional **reads** exist in exactly one place — [ProductInsightsController](../src/Agora.Api/Controllers/ProductInsightsController.cs) computes a SHA-256 ETag and honours `If-None-Match` for 304. Conditional **writes** exist nowhere. `grep -rn "If-Match"` returns no results. Optimistic concurrency exists in the database layer for `InventoryItem`, `Cart` and `GiftCard`, but it is not exposed over HTTP.

**Acceptance criteria**

- `GET /api/products/{id}` returns a strong `ETag` derived from the product's current state.
- `PUT /api/products/{id}` requires `If-Match`. A missing header returns **428 Precondition Required**; a stale value returns **412 Precondition Failed**; a matching value proceeds.
- `If-Match: *` succeeds if the product exists.
- A 412 makes **no** database change — prove it by re-reading in a fresh scope.
- The ETag changes after a successful update, and the old one then fails.
- 412 and 409 remain distinct: 412 is "your precondition failed"; 409 stays for a database-level concurrency conflict.

**Files to open:** [ProductsController.cs](../src/Agora.Api/Controllers/ProductsController.cs), [ProductInsightsController.cs](../src/Agora.Api/Controllers/ProductInsightsController.cs) (the ETag model to copy), [DomainExceptionFilter.cs](../src/Agora.Api/Filters/DomainExceptionFilter.cs). Proposed new files: `src/Agora.Api/Queries/ProductETag.cs`, `tests/Agora.Tests/Integration/ConditionalWriteTests.cs`.

**Implementation plan**

1. Read the ETag computation in `ProductInsightsController.ReviewSummary`. Note that it hashes **the exact bytes it returns** — a second serialization could produce a different validator. Reuse that discipline.
2. Write `ProductETag.For(Product)` producing a quoted hex SHA-256 over the fields that constitute a meaningful change: name, slug, description, active flag, tax category, and each variant's id/price/SKU in a deterministic order.
3. Set the ETag on the single-product GET responses.
4. In `PUT`, parse `Request.Headers.IfMatch` with `EntityTagHeaderValue.TryParseList`. Use **strong** comparison here — weak validators are explicitly not valid for `If-Match`.
5. Load the product, compute its current tag, and compare before mutating. Return 428 when the header is absent, 412 when it does not match.
6. Return ProblemDetails for both, with `errorCode` values `PRECONDITION_REQUIRED` and `PRECONDITION_FAILED` if WD-03 is done.
7. Write tests: happy path with a fresh tag; missing header → 428; stale tag → 412 plus a fresh-scope read proving no write; `*` succeeds; tag changes after update; the old tag then fails.
8. Document the requirement in the API reference — this is a **breaking change** for existing `PUT` callers, so pair it with WD-09 or a migration window.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~ConditionalWriteTests|FullyQualifiedName~ProductsApiTests"`.

**Common trap:** accepting weak validators for `If-Match`. `W/"abc"` means "semantically equivalent", which is not a strong enough guarantee to authorize an overwrite.

**Done when:** all six cases pass, the no-write-on-412 proof uses a fresh scope, and you can explain 412 versus 409.

---

## WD-11: Response compression

**Status:** Planned; not implemented. **Tier:** Intermediate. **Starting knowledge:** content negotiation.

**User story:** As a shopper on a mobile connection, I want large catalog responses compressed, so pages load faster and I use less data.

**Current behavior:** No compression is configured. `grep -rn "ResponseCompression" src/` returns nothing. A 100-product page ships uncompressed JSON.

**Acceptance criteria**

- Brotli and Gzip are enabled for `application/json` and `application/problem+json`.
- A request sending `Accept-Encoding: br` receives `Content-Encoding: br`; a request sending none receives an uncompressed body.
- Responses carry `Vary: Accept-Encoding`.
- Compression is **disabled for HTTPS by default** (`EnableForHttps = false`) unless explicitly enabled in configuration, with the BREACH-attack rationale written in a code comment.
- Compression does not apply to the packing-slip renderer or any binary artifact that is already compressed.
- Existing response bodies are byte-identical after decompression — no test may weaken because of this change.

**Files to open:** [Program.cs](../src/Agora.Api/Program.cs), [ProductsApiTests.cs](../tests/Agora.Tests/Integration/ProductsApiTests.cs), [PackingSlipRenderer.cs](../src/Agora.Api/Rendering/PackingSlipRenderer.cs). Proposed new file: `tests/Agora.Tests/Integration/CompressionTests.cs`.

**Implementation plan**

1. Register `AddResponseCompression` with both providers and the explicit MIME type list. Do not use the default list blindly; enumerate what you intend to compress.
2. Set `EnableForHttps` from configuration, defaulting to `false`, and comment why: compressing attacker-influenced content alongside secrets over TLS enables BREACH.
3. Call `app.UseResponseCompression()` early — before the endpoints that produce the bodies, and after correlation/security headers.
4. Configure Brotli and Gzip levels explicitly (`Fastest` is usually right for an API; `Optimal` costs CPU per request for marginal bytes).
5. Confirm `HttpClient` in tests does **not** auto-decompress, or you cannot assert on `Content-Encoding`. Construct the client via `factory.CreateDefaultClient()` with an explicit handler if needed.
6. Write tests: `br` requested and honoured; `gzip` requested and honoured; no `Accept-Encoding` yields no `Content-Encoding`; `Vary` present; decompressed body equals the uncompressed body for the same request.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~CompressionTests"`. Demo with `curl -s -H "Accept-Encoding: br" -o /dev/null -w '%{size_download}\n' localhost:5000/api/products`.

**Common trap:** asserting on `Content-Encoding` while the test handler transparently decompresses and strips it. Check your handler configuration before concluding the feature is broken.

**Done when:** all five cases pass, HTTPS compression is off by default with the reason documented, and you can explain BREACH in one sentence.

---

## WD-12: Tiered rate limiting with standard headers

**Status:** Planned; not implemented. **Tier:** Intermediate. **Starting knowledge:** the existing checkout limiter.

**User story:** As an API client, I want to know my remaining quota and when to retry, so I can back off correctly instead of hammering the API into a 429 loop.

**Current behavior:** [Program.cs](../src/Agora.Api/Program.cs) defines exactly one policy, `"checkout"`, a per-IP fixed window from [CheckoutRateLimitOptions](../src/Agora.Api/CheckoutRateLimitOptions.cs) (default 10/minute). It returns a bare 429 with **no** `Retry-After` and no quota headers. Every other route — including login and the report endpoints — is unlimited.

**Acceptance criteria**

- Three named policies: `checkout` (existing limits preserved exactly), `auth` (stricter, protecting login and registration), and `reports` (protecting expensive admin aggregation).
- Every 429 includes `Retry-After` in seconds and a ProblemDetails body.
- Successful responses on limited routes include `RateLimit-Limit`, `RateLimit-Remaining` and `RateLimit-Reset`.
- Authenticated requests partition by customer ID; anonymous requests partition by IP. One user behind a shared NAT must not exhaust everyone's quota.
- Each policy is independently configurable, and existing checkout behaviour and tests are unchanged.

**Files to open:** [Program.cs](../src/Agora.Api/Program.cs), [CheckoutRateLimitOptions.cs](../src/Agora.Api/CheckoutRateLimitOptions.cs), [AuthController.cs](../src/Agora.Api/Controllers/AuthController.cs), [AdminReportsController.cs](../src/Agora.Api/Controllers/AdminReportsController.cs), [ProductionReadinessTests.cs](../tests/Agora.Tests/Integration/ProductionReadinessTests.cs). Proposed new file: `tests/Agora.Tests/Integration/RateLimitPolicyTests.cs`.

**Implementation plan**

1. Generalise `CheckoutRateLimitOptions` into a `RateLimitOptions` holding a dictionary of named policy settings. Keep the existing `RateLimiting:Checkout` keys working so current configuration and tests still bind.
2. Write a partition-key helper: authenticated → `"cust:{customerId}"` via the existing `User.GetCustomerId()` extension; anonymous → `"ip:{remoteIp}"`.
3. Register the three policies with that shared partition logic.
4. Replace the bare `RejectionStatusCode` with an `OnRejected` callback that writes `Retry-After` from the limiter's `RetryAfter` metadata plus a ProblemDetails body.
5. Add the quota headers in the same callback and in a small filter for successful responses; take the values from the limiter statistics rather than recomputing them.
6. Apply `[EnableRateLimiting("auth")]` to login/register and `[EnableRateLimiting("reports")]` to the report actions.
7. Write tests: existing checkout limit still enforced at the configured number; auth policy triggers on its own threshold; `Retry-After` present and numeric; quota headers on a success; two different customers do not share a partition.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~RateLimitPolicyTests|FullyQualifiedName~ProductionReadinessTests"`.

**Common trap:** partitioning solely by IP. Corporate NAT and mobile carriers put thousands of users behind one address; the first heavy user locks out the rest.

**Done when:** all five cases pass, existing checkout tests are untouched, and you can justify each policy's numbers.

---

## WD-13: Account lockout after failed logins

**Status:** Planned; not implemented. **Tier:** Intermediate. **Migration required.** **Starting knowledge:** the auth flow, `TimeProvider`.

**User story:** As a customer, I want my account temporarily locked after repeated wrong passwords, so an attacker cannot guess it by brute force.

**Current behavior:** [Customer](../src/Agora.Domain/Entities/Customer.cs) stores only `Id`, `Email`, `PasswordHash`, `FullName`, `Role` and `CreatedAt`. Login in [AuthController](../src/Agora.Api/Controllers/AuthController.cs) verifies the PBKDF2 hash with no attempt tracking; unlimited guesses are permitted.

**Acceptance criteria**

- After 5 consecutive failures, the account locks for 15 minutes. Both numbers are configurable.
- A successful login resets the counter to zero.
- A locked account returns the **same** response as a wrong password — status, body and shape identical. Revealing lockout state confirms the address exists.
- The lock expires automatically; no admin action is needed.
- Timing uses the injected `TimeProvider` (as [AuthenticationTimeProvider](../src/Agora.Infrastructure/Services/AuthenticationTimeProvider.cs) already does), so tests move the clock instead of sleeping.
- Counter updates persist even on the failure path.
- Complements but does not replace WD-12's `auth` rate limit: one defends an account, the other defends the endpoint.

**Files to open:** [Customer.cs](../src/Agora.Domain/Entities/Customer.cs), [AuthController.cs](../src/Agora.Api/Controllers/AuthController.cs), [AuthenticationTimeProvider.cs](../src/Agora.Infrastructure/Services/AuthenticationTimeProvider.cs), [AuthApiTests.cs](../tests/Agora.Tests/Integration/AuthApiTests.cs). Proposed new files: `tests/Agora.Tests/Unit/AccountLockoutTests.cs`, `tests/Agora.Tests/Integration/LoginLockoutApiTests.cs`.

**Implementation plan**

1. Add `FailedLoginCount` (int) and `LockedUntil` (`DateTimeOffset?`) to `Customer`. Put the transitions in **domain methods** — `RegisterFailedLogin(now, maxAttempts, lockDuration)` and `RegisterSuccessfulLogin()` — not in the controller.
2. Write `AccountLockoutTests` as pure unit tests over those methods first: below threshold, at threshold, expiry boundary just before/at/after, and reset on success.
3. Create the migration with the pinned EF tool. Both columns must be nullable or defaulted so existing rows upgrade cleanly. Inspect the generated migration and the model snapshot.
4. In `Login`, check the lock **before** verifying the password, and return the standard invalid-credentials response when locked.
5. On a wrong password, call `RegisterFailedLogin` and save. On success, call `RegisterSuccessfulLogin` and save.
6. Verify the password even when the account is locked, or skip it deliberately with a constant-time consideration — document which you chose and why, since it affects timing-based account enumeration.
7. Write API tests: five failures then a locked sixth; identical response bodies for wrong-password and locked; a successful login after the clock advances past expiry; counter reset after success.
8. Add an upgrade test seeding a pre-migration customer row and proving login still works after upgrade.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~AccountLockout|FullyQualifiedName~LoginLockout|FullyQualifiedName~AuthApiTests"`.

**Common trap:** returning a distinct "account locked" message. It is friendlier and it hands an attacker a free account-enumeration oracle.

**Done when:** boundary cases pass on a controlled clock, responses are indistinguishable, the upgrade test passes, and you can explain why this and rate limiting are both needed.

---

## WD-14: Email verification flow

**Status:** Planned; not implemented. **Tier:** Intermediate. **Migration required.** **Starting knowledge:** hashing, token lifecycles, [GuestOrderAccessService](../src/Agora.Infrastructure/Services/GuestOrderAccessService.cs) as the model.

**User story:** As a merchant, I want new accounts to confirm their email address, so order confirmations reach real inboxes and typo'd addresses are caught early.

**Current behavior:** Registration creates an account and immediately issues a JWT. No address confirmation exists, and the project has no email transport at all.

**Acceptance criteria**

- Registration creates an unverified customer and a verification token valid for 24 hours.
- `POST /api/auth/verify-email` with a valid token marks the account verified and consumes the token.
- Only a SHA-256 **digest** is stored. The plaintext token is returned once at issue time and never again — the exact pattern `GuestOrderAccessService.Issue` already uses.
- Tokens are single-use; a second attempt returns 410 Gone.
- An expired or unknown token returns the same generic failure. No enumeration.
- Requesting a new token invalidates outstanding ones.
- There is **no real email sending.** A `IVerificationNotifier` interface with a `LoggingVerificationNotifier` writes the link to the log, mirroring how `FakePaymentGateway` stands in for a provider. Real delivery is a separate story.
- Unverified accounts can still browse and build carts; decide and document whether checkout requires verification.

**Files to open:** [GuestOrderAccessService.cs](../src/Agora.Infrastructure/Services/GuestOrderAccessService.cs) (the pattern to copy), [Customer.cs](../src/Agora.Domain/Entities/Customer.cs), [AuthController.cs](../src/Agora.Api/Controllers/AuthController.cs). Proposed new files: `src/Agora.Domain/Entities/EmailVerificationToken.cs`, `src/Agora.Infrastructure/Services/EmailVerificationService.cs`, `tests/Agora.Tests/Integration/EmailVerificationApiTests.cs`.

**Implementation plan**

1. Read `GuestOrderAccessService` closely: `{id:N}.{secret}` token format, `RandomNumberGenerator.GetBytes(32)`, SHA-256 digest storage, `CryptographicOperations.FixedTimeEquals` comparison, strict length parsing. Reuse all of it.
2. Add `EmailVerificationToken` with `Id`, `CustomerId`, `SecretDigest`, `CreatedAt`, `ExpiresAt`, `ConsumedAt`. Add `EmailVerifiedAt` to `Customer`.
3. Add an EF configuration with a partial unique index over unconsumed tokens per customer, following the pattern in `GuestOrderCredentialConfiguration`.
4. Create the migration. Existing customers must upgrade sensibly — decide whether they are grandfathered as verified, and write that decision into the migration and the ADR.
5. Write `EmailVerificationService` with `Issue(customer)` and `VerifyAsync(token)`. Keep plaintext in the in-memory result only.
6. Define `IVerificationNotifier` in Domain, implement `LoggingVerificationNotifier` in Infrastructure, and register it. Do not put a URL template in the domain layer.
7. Add the endpoints and wire issuance into registration.
8. Write tests: happy path; reuse → 410; expired → generic failure; unknown → identical generic failure; reissue invalidates the previous token; digest-only storage proven by reading the row; upgrade test for pre-existing customers.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~EmailVerification"`.

**Common trap:** storing the plaintext token so it can be re-sent. That turns your database into a credential store. Reissue a new token instead.

**Done when:** all seven cases pass, no plaintext is persisted, the grandfathering decision is documented, and you can explain why failures are indistinguishable.

---

## WD-15: Password reset with single-use tokens

**Status:** Planned; not implemented. **Tier:** Intermediate. **Migration required.** **Prerequisite:** WD-14 (reuses its token pattern and notifier).

**User story:** As a customer who forgot my password, I want a secure way to set a new one, so I can recover my account without contacting support.

**Current behavior:** No reset mechanism exists. A customer who forgets their password is permanently locked out.

**Acceptance criteria**

- `POST /api/auth/forgot-password` **always** returns 202 Accepted, whether or not the address exists. Anything else enumerates accounts.
- Tokens live 1 hour — deliberately shorter than verification tokens, because this one changes a credential.
- `POST /api/auth/reset-password` takes the token and a new password, applies the existing password policy, and re-hashes with [Pbkdf2PasswordHasher](../src/Agora.Infrastructure/Services/Pbkdf2PasswordHasher.cs).
- A successful reset **revokes all active login sessions** for that customer, using the existing [AuthenticationSessionService](../src/Agora.Infrastructure/Services/AuthenticationSessionService.cs). An attacker holding a stolen token must lose access.
- A successful reset also clears any WD-13 lockout.
- Tokens are single-use and invalidated by a completed reset or by issuing a newer one.
- Only digests are stored; the same `FixedTimeEquals` comparison is used.
- The forgot-password endpoint uses WD-12's `auth` rate-limit policy.

**Files to open:** [AuthController.cs](../src/Agora.Api/Controllers/AuthController.cs), [Pbkdf2PasswordHasher.cs](../src/Agora.Infrastructure/Services/Pbkdf2PasswordHasher.cs), [AuthenticationSessionService.cs](../src/Agora.Infrastructure/Services/AuthenticationSessionService.cs), [LoginSessionsApiTests.cs](../tests/Agora.Tests/Integration/LoginSessionsApiTests.cs). Proposed new files: `src/Agora.Domain/Entities/PasswordResetToken.cs`, `src/Agora.Infrastructure/Services/PasswordResetService.cs`, `tests/Agora.Tests/Integration/PasswordResetApiTests.cs`.

**Implementation plan**

1. Reuse the WD-14 token entity shape. Keep it a **separate** table: different lifetime, different consequences, different audit needs.
2. Add the migration and inspect it.
3. Write `PasswordResetService.RequestAsync(email)`. Look up the customer; if absent, do nothing but still return the same result to the caller. Keep the work roughly constant-time.
4. Write `ResetAsync(token, newPassword)` inside a transaction: validate the token, re-hash the password, consume the token, revoke sessions, clear lockout, save, commit. All of it or none of it.
5. Confirm how `AuthenticationSessionService` revokes sessions, and call it inside that same transaction.
6. Reuse the WD-14 notifier abstraction. Do not add a second one.
7. Write tests: happy path; old password rejected afterwards; a JWT issued before the reset is rejected afterwards; token reuse fails; expired token fails; unknown email still returns 202; a weak new password is rejected and the token remains usable; lockout cleared.
8. Write one explicit test that a pre-reset bearer token stops working — the highest-value assertion in this story.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~PasswordReset|FullyQualifiedName~LoginSessions"`.

**Common trap:** resetting the password without revoking sessions. The attacker who caused the reset keeps their existing session, so the reset achieves nothing.

**Done when:** all eight cases pass, especially session revocation, and you can explain why this token expires faster than a verification token.

---

# Advanced tier

These change behaviour across request boundaries, add entities and workers, or touch money and security directly. Expect a design review before implementation.

## WD-16: Idempotency keys for checkout

**Status:** Planned; not implemented. **Tier:** Advanced. **Migration required.** **Starting knowledge:** transactions, [ADR 0003](../docs/adr/0003-reserve-charge-commit.md), the open L6 finding.

**User story:** As a shopper whose connection dropped during checkout, I want my retry to return the original order rather than charging me twice, so a network failure never costs me money.

**Current behavior:** [CheckoutService.CheckoutAsync](../src/Agora.Infrastructure/Services/CheckoutService.cs) has no operation identity. A client that retries after a timeout runs the whole pipeline again: it reserves stock again, creates a second order, and charges the gateway a second time. The repository's own [review findings](../docs/learning/review-findings.md) list this as open work (L6), and the [feature backlog](../docs/learning/feature-backlog.md) sketches it as L6. This story is the concrete plan.

**Acceptance criteria**

- `POST /api/checkout` accepts an `Idempotency-Key` header (client-generated, 16–128 characters).
- The first request with a key executes normally and stores the key with a **request fingerprint** and the resulting order number.
- A repeat with the same key **and the same fingerprint** returns the original 201 response body, with `Idempotency-Replayed: true`.
- A repeat with the same key but a **different** fingerprint returns 422 — the key identifies one specific operation.
- A concurrent duplicate (second request arriving while the first is in flight) returns **409 with `Retry-After`**, not a second charge. A unique index on the key makes the database the arbiter rather than an application-level check.
- Records expire after 24 hours; a background sweep removes them.
- Requests **without** the header behave exactly as today. This must not break 214 existing endpoints' worth of callers.
- A failed checkout (declined payment) does **not** record a reusable success; the decline itself may be replayed, but a retry with a new payment token must be able to proceed. Decide and document which.

**Files to open:** [CheckoutService.cs](../src/Agora.Infrastructure/Services/CheckoutService.cs), [CheckoutController.cs](../src/Agora.Api/Controllers/CheckoutController.cs), [CheckoutApiTests.cs](../tests/Agora.Tests/Integration/CheckoutApiTests.cs), [WebhookOutboxWorker.cs](../src/Agora.Infrastructure/Services/WebhookOutboxWorker.cs) (the worker pattern to copy). Proposed new files: `src/Agora.Domain/Entities/IdempotencyRecord.cs`, `src/Agora.Infrastructure/Services/IdempotencyService.cs`, `tests/Agora.Tests/Integration/CheckoutIdempotencyTests.cs`.

**Implementation plan**

1. **Design before coding.** Write down the state machine: `InProgress → Completed` and `InProgress → Failed`. Decide what happens on process death mid-operation. Get this reviewed.
2. Add `IdempotencyRecord`: `Key` (unique index), `Fingerprint` (hash of the meaningful request fields), `Status`, `OrderNumber`, `ResponseBody`, `CreatedAt`, `ExpiresAt`.
3. Compute the fingerprint over cart token, email, address, shipping method, discount and gift-card codes — **not** the payment token, which legitimately changes between retries.
4. Create the migration with a unique index on `Key`. Inspect the snapshot.
5. In the controller, read the header. Absent → call the existing path unchanged. Present → route through `IdempotencyService`.
6. Insert the `InProgress` record in its own transaction **before** starting checkout. A unique-constraint violation here means a concurrent duplicate: return 409 with `Retry-After`.
7. Run checkout. On success, update the record to `Completed` with the serialized response, inside the same transaction that marks the order paid where possible.
8. On replay, compare fingerprints before returning the stored body, and set `Idempotency-Replayed: true`.
9. Write a cleanup worker following `WebhookOutboxWorker`: hosted service, `Enabled` and poll options, disabled under the `Testing` environment, exception-tolerant loop.
10. Write tests: no header behaves as today; replay returns the identical body and header; same key with a changed cart → 422; a genuine barrier-based concurrent duplicate → exactly one order and one charge; expired record allows reuse; a declined payment follows your documented rule.
11. Use a counting or recording `IPaymentGateway` test double to assert **exactly one** charge. This is the assertion that proves the feature.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~CheckoutIdempotency|FullyQualifiedName~CheckoutApiTests"`.

**Common trap:** checking "does this key exist?" and then inserting. Two concurrent requests both see "no" and both proceed. The unique index must be what rejects the duplicate — let the database decide.

**Done when:** the concurrent-duplicate test proves one order and one charge, existing checkout tests are untouched, and you can explain why the payment token is excluded from the fingerprint.

**Note:** this story bounds *client retries*. It does **not** fix the separate open problem of a crash after the gateway accepts but before the completion transaction commits. That needs a reconciliation sweep and is deliberately out of scope here.

---

## WD-17: SSRF protection for webhook URLs

**Status:** Planned; not implemented. **Tier:** Advanced. **Starting knowledge:** networking, DNS, the webhook subscription flow.

**User story:** As a platform operator, I want webhook destinations restricted to public internet addresses, so a tenant cannot use our outbound requests to reach our internal network.

**Current behavior:** [WebhooksController](../src/Agora.Api/Controllers/WebhooksController.cs) stores `request.Url.Trim()` with no validation beyond that. An admin can subscribe to `http://169.254.169.254/latest/meta-data/` (cloud metadata), `http://localhost:5000/api/...` (loopback) or `http://10.0.0.5/` (private range), and the worker will faithfully send signed requests there. The current [FakeWebhookSender](../src/Agora.Infrastructure/Services/WebhookService.cs) makes no real connection, so the risk lands the moment a real sender is implemented — which makes this the right time to build the guard.

**Acceptance criteria**

- Only `http` and `https` schemes are accepted; `file:`, `gopher:`, `ftp:` and the rest are rejected at subscription time.
- Rejected destination categories: loopback (`127.0.0.0/8`, `::1`), link-local (`169.254.0.0/16`, `fe80::/10`), private ranges (`10/8`, `172.16/12`, `192.168/16`), unique-local (`fc00::/7`), multicast, broadcast, and `0.0.0.0`.
- Both the literal host **and** its resolved addresses are checked. `http://internal.example.com/` resolving to `10.0.0.5` must be rejected.
- Validation happens at **subscribe/update time** (fast feedback) **and again immediately before sending** (DNS can change after approval — this is DNS rebinding).
- A configuration flag allows loopback in Development and Testing, or the existing test suite cannot run.
- Rejections return 422 with a clear reason and a stable `errorCode`.
- Redirects are not followed by the sender, or each hop is re-validated. Document which you chose.

**Files to open:** [WebhooksController.cs](../src/Agora.Api/Controllers/WebhooksController.cs), [Webhook.cs](../src/Agora.Domain/Entities/Webhook.cs), [WebhookOutboxWorker.cs](../src/Agora.Infrastructure/Services/WebhookOutboxWorker.cs), [WebhooksApiTests.cs](../tests/Agora.Tests/Integration/WebhooksApiTests.cs). Proposed new files: `src/Agora.Infrastructure/Services/WebhookDestinationValidator.cs`, `tests/Agora.Tests/Unit/WebhookDestinationValidatorTests.cs`.

**Implementation plan**

1. Write the blocked-range list as a table in a comment first, with the CIDR and the reason for each. Review the table before writing code.
2. Implement `WebhookDestinationValidator` with two entry points: `ValidateSyntax(url)` (scheme, port, parseability, no credentials in the URL) and `ValidateResolvedAsync(url, ct)` (DNS resolution plus per-address range checks).
3. Use `IPAddress.IsLoopback`, `IsIPv6LinkLocal`, `IsIPv6UniqueLocal`, and explicit byte checks for the IPv4 private ranges. Handle IPv4-mapped IPv6 addresses (`::ffff:10.0.0.5`) — a very common bypass.
4. Add `AllowPrivateDestinations` to `WebhookOutboxOptions`, defaulting to `false` and set to `true` in `appsettings.Testing.json`.
5. Call syntax + resolution validation in the create and update actions; return 422 on failure.
6. Call resolution validation again in `WebhookDeliverySender.SendAsync` just before dispatch. On failure, finish the attempt with reason code `DestinationBlocked` rather than throwing — reuse the existing attempt-outcome vocabulary.
7. Write unit tests as a theory over the whole blocked table, plus IPv4-mapped IPv6, a URL with embedded credentials, a non-HTTP scheme, and an allowed public address.
8. Write an API test proving a blocked URL cannot be subscribed, and a worker test proving a destination that becomes private after approval is not delivered to.
9. Document the policy in the webhook API reference so integrators understand why their internal endpoint is refused.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~WebhookDestination|FullyQualifiedName~WebhooksApiTests"`.

**Common trap:** validating only at subscription time. An attacker registers a public host, passes validation, then repoints DNS at `10.0.0.5`. Re-validation before send is what closes it.

**Done when:** the full blocked table passes as a theory, IPv4-mapped addresses are handled, re-validation before send is proven, and you can explain DNS rebinding.

---

## WD-18: Product image upload

**Status:** Planned; not implemented. **Tier:** Advanced. **Migration required.** **Starting knowledge:** multipart requests, streams, content types.

**User story:** As a catalog admin, I want to upload an image file directly, so I do not have to host images elsewhere and paste URLs.

**Current behavior:** [ProductImage](../src/Agora.Domain/Entities/ProductImage.cs) stores `Url`, `AltText` and `SortOrder` only. There is no upload endpoint, no stored bytes, and no static file middleware. Images must already exist somewhere else.

**Acceptance criteria**

- `POST /api/admin/products/{id}/images` accepts `multipart/form-data` with a file plus optional alt text.
- Accepted types: JPEG, PNG, WebP. Maximum 5 MB, configurable.
- The content type is determined by **inspecting magic bytes**, not by trusting the client's `Content-Type` or the file extension.
- Oversized uploads are rejected with 413 **without buffering the whole body into memory**. Use the existing [CatalogImportBodyLimitAttribute](../src/Agora.Api/Filters/CatalogImportBodyLimitAttribute.cs) as the precedent for setting a per-endpoint limit.
- Stored bytes are addressed by content hash, so re-uploading an identical file does not duplicate storage.
- `GET /api/products/{id}/images/{imageId}` serves the bytes with the correct content type, a strong `ETag` (the content hash), and `Cache-Control: public, max-age=31536000, immutable` — safe precisely because the URL is content-addressed.
- Deleting an image row does not delete bytes still referenced by another row.
- Existing URL-based images keep working; the two mechanisms coexist.

**Files to open:** [ProductImage.cs](../src/Agora.Domain/Entities/ProductImage.cs), [CatalogEditingController.cs](../src/Agora.Api/Controllers/CatalogEditingController.cs), [CatalogImportBodyLimitAttribute.cs](../src/Agora.Api/Filters/CatalogImportBodyLimitAttribute.cs), [ReportExportConfiguration.cs](../src/Agora.Infrastructure/Persistence/ReportExportConfiguration.cs) (existing binary-artifact storage to copy). Proposed new files: `src/Agora.Domain/Entities/ImageAsset.cs`, `src/Agora.Infrastructure/Services/ImageAssetService.cs`, `tests/Agora.Tests/Integration/ProductImageUploadApiTests.cs`.

**Implementation plan**

1. Study how report exports already persist binary artifacts. Reuse that storage approach rather than inventing a second one.
2. Add `ImageAsset`: `Id`, `ContentHash` (unique index), `ContentType`, `ByteLength`, `Bytes`, `CreatedAt`. Add a nullable `ImageAssetId` to `ProductImage`, keeping `Url` for existing rows.
3. Write the magic-byte sniffer: JPEG `FF D8 FF`, PNG `89 50 4E 47 0D 0A 1A 0A`, WebP `RIFF....WEBP`. Read only the first 16 bytes to decide.
4. Create the migration. Existing image rows must remain valid with a null asset ID — add an upgrade test proving it.
5. Add the upload action with `[DisableFormValueModelBinding]`-style streaming, or read `IFormFile` with an explicit `MultipartBodyLengthLimit`. Enforce the size limit before reading the whole stream.
6. Hash with SHA-256 while streaming. Look up the hash; reuse the existing asset when present, otherwise insert.
7. Add the serving action returning `File(bytes, contentType)` with the ETag and immutable cache headers. Honour `If-None-Match` with 304, following the [ProductInsightsController](../src/Agora.Api/Controllers/ProductInsightsController.cs) model.
8. Make deletion reference-aware: remove the `ProductImage` row, and remove the asset only when no rows reference it.
9. Write tests: valid JPEG/PNG/WebP; a `.jpg` file whose bytes are actually a PDF → 422; oversized → 413; identical re-upload creates one asset and two rows; serving returns correct type and ETag; `If-None-Match` → 304; deleting one of two references keeps the bytes; upgrade test for legacy URL rows.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~ProductImageUpload"`.

**Common trap:** trusting `IFormFile.ContentType`. It is client-supplied text. A file claiming `image/png` can contain anything; only the bytes are evidence.

**Done when:** the disguised-file test passes, deduplication is proven, legacy rows still work, and you can explain why content-addressed URLs may be cached forever.

---

## WD-19: Order status event stream

**Status:** Planned; not implemented. **Tier:** Advanced. **Starting knowledge:** long-lived responses, cancellation tokens, the order lifecycle.

**User story:** As a shopper waiting on an order, I want its status to update live, so I do not have to refresh to see that it shipped.

**Current behavior:** Order status is available only by polling `GET /api/orders/{number}`. There is no streaming endpoint, no SSE and no WebSocket support anywhere in the project.

**Acceptance criteria**

- `GET /api/orders/{number}/events` returns `text/event-stream` and holds the connection open.
- Authorization uses the **existing** [GuestOrderAccessService.EnsureCanReadAsync](../src/Agora.Infrastructure/Services/GuestOrderAccessService.cs) — admin, owner, or valid guest token. A streaming endpoint is not an authorization exception.
- An initial `snapshot` event carries the current status immediately, so a client never waits for a change to render.
- Subsequent `status` events are emitted when status changes.
- A comment heartbeat (`: keep-alive`) every 20 seconds prevents proxy timeouts.
- The stream ends cleanly when the order reaches a terminal state (`Fulfilled`, `Cancelled`, `Refunded`), or when the client disconnects, or at a 10-minute cap.
- `HttpContext.RequestAborted` is honoured everywhere; a disconnect must not leave a database context or loop running.
- Response buffering is disabled, or clients see nothing until the stream closes.
- A concurrent-connection cap per customer prevents resource exhaustion.

**Files to open:** [OrdersController.cs](../src/Agora.Api/Controllers/OrdersController.cs), [GuestOrderAccessService.cs](../src/Agora.Infrastructure/Services/GuestOrderAccessService.cs), [OrderStateApiTests.cs](../tests/Agora.Tests/Integration/OrderStateApiTests.cs). Proposed new files: `src/Agora.Api/Controllers/OrderEventsController.cs`, `tests/Agora.Tests/Integration/OrderEventStreamTests.cs`.

**Implementation plan**

1. Decide the change-detection mechanism and write down the trade-off. Polling the database on an interval inside the request is simplest and honest for SQLite; an in-process `Channel` published by the fulfillment path is lower-latency but only works single-node. Pick one, document why, and note what changes under multiple instances.
2. Set `Content-Type: text/event-stream`, `Cache-Control: no-store`, `X-Accel-Buffering: no`, and disable buffering via `IHttpResponseBodyFeature.DisableBuffering()`.
3. Authorize **before** writing any bytes. Once the stream starts you cannot change the status code.
4. Write the snapshot event, then loop: check for a change, write a `status` event if changed, write a heartbeat if the interval elapsed, and await a short delay against `RequestAborted`.
5. Format events strictly: `event: status\ndata: {json}\n\n`. The blank line terminates the event; omitting it means the client never dispatches.
6. Flush after every write. Without `FlushAsync` the data sits in the buffer.
7. Enforce the terminal-state exit, the 10-minute cap and the per-customer connection cap.
8. Write tests: snapshot arrives immediately; a status change mid-stream produces an event; an unauthorized caller is rejected **before** any body bytes; terminal status closes the stream; cancellation exits promptly. Use `HttpCompletionOption.ResponseHeadersRead` and read the stream incrementally with a timeout, or the test will hang.
9. Document the endpoint, including that it is unsuitable behind a buffering proxy without configuration.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~OrderEventStream"`. Demo with `curl -N localhost:5000/api/orders/ORD-.../events -H "Authorization: Bearer ..."`.

**Common trap:** writing tests with `client.GetAsync` and awaiting the full body. That waits for the stream to close and hangs the suite. Always use `ResponseHeadersRead` plus a read timeout.

**Done when:** all five cases pass without hanging, disconnects are clean, and you can explain why authorization must precede the first byte.

---

## WD-20: Full-text product search with ranking

**Status:** Planned; not implemented. **Tier:** Advanced. **Migration required.** **Starting knowledge:** SQL, indexes, the existing catalog query.

**User story:** As a shopper, I want search results ordered by relevance, so the product I meant appears first instead of being buried alphabetically.

**Current behavior:** `GET /api/products?search=` performs a `LIKE` filter in [ProductCatalogQuery](../src/Agora.Api/Queries/ProductCatalogQuery.cs) with LIKE metacharacters escaped (a fix already documented in the [review findings](../docs/learning/review-findings.md)). Results are ordered by the requested sort — name, price or date — never by relevance. A match in the product name ranks the same as one buried in the description.

**Acceptance criteria**

- A new `sort=relevance` option ranks by full-text score; it is the default **only** when `search` is supplied, preserving every existing default.
- A name match outranks a description-only match.
- Multi-word queries match products containing all terms.
- The index covers name, description and SKU, and stays in sync on insert, update and delete.
- Searching with no query term behaves exactly as today.
- Special characters in user input cannot inject FTS query syntax — quoting or tokenizing is mandatory.
- All existing catalog search tests continue to pass unchanged, including the same-variant price-range counterexample test.
- Deep paging remains bounded; relevance ordering still needs a deterministic tiebreak by ID.

**Files to open:** [ProductCatalogQuery.cs](../src/Agora.Api/Queries/ProductCatalogQuery.cs), [ProductSearchRequest.cs](../src/Agora.Api/Contracts/ProductSearchRequest.cs), [CatalogSearchApiTests.cs](../tests/Agora.Tests/Integration/CatalogSearchApiTests.cs), [AgoraDbContext.cs](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs). Proposed new files: `src/Agora.Infrastructure/Persistence/ProductSearchIndex.cs`, `tests/Agora.Tests/Integration/RelevanceSearchApiTests.cs`.

**Implementation plan**

1. Confirm FTS5 availability. The project already references `SQLitePCLRaw.bundle_e_sqlite3`, which includes FTS5 — verify with a probe query before building on it.
2. Read `ProductCatalogQuery` fully. The same-variant predicate rule is a hard-won correctness property; relevance ordering must not disturb it.
3. Write a migration containing raw SQL that creates the `products_fts` virtual table plus triggers for insert, update and delete on the products table. EF cannot model this — hand-write it and inspect it carefully.
4. Backfill existing rows in the same migration.
5. Add `relevance` to the accepted sort values, rejecting it with 400 when `search` is absent — ranking without a query is meaningless.
6. Sanitize the query: tokenize on whitespace, strip FTS operators, and wrap each token in double quotes. Never concatenate raw input into an FTS `MATCH`.
7. Execute the ranked ID lookup via `FromSqlInterpolated`, then compose the existing filters over those IDs so all current predicates still apply. Add `ThenBy(p => p.Id)` for stable ties.
8. Weight the name column above description using FTS5's `bm25()` column weights.
9. Write tests: name beats description; multi-word requires all terms; punctuation in the query does not error; `sort=relevance` without `search` returns 400; stable order across two identical requests; and a full run of the existing catalog search class.
10. Add a trigger test: update a product name and confirm the old term no longer matches and the new one does.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~RelevanceSearch|FullyQualifiedName~CatalogSearchApiTests"`.

**Common trap:** letting the FTS table drift from the products table. Without delete and update triggers, search returns products that no longer exist — the trigger test is what catches it.

**Done when:** ranking is demonstrably correct, the existing search suite is untouched, injection is impossible, and you can explain how the index stays consistent.

---

## WD-21: Timezone-correct sales reporting

**Status:** Planned; not implemented. **Tier:** Advanced. **Starting knowledge:** `DateTimeOffset`, IANA time zones, the report endpoints.

**User story:** As a merchant in Berlin, I want daily sales bucketed by **my** calendar day, so the totals match my accounting instead of splitting each day across two UTC buckets.

**Current behavior:** [AdminReportsController](../src/Agora.Api/Controllers/AdminReportsController.cs) buckets in memory using `BucketKey(DateTimeOffset paidAt, string interval)`, which calls `paidAt.UtcDateTime.Date`. Every bucket boundary is UTC midnight. A merchant in UTC+2 sees each of their days split across two buckets, and the [feature backlog](../docs/learning/feature-backlog.md) already flags UTC boundary correctness (L4) as an open concern.

**Acceptance criteria**

- Reports accept an optional `timeZone` query parameter holding an IANA identifier such as `Europe/Berlin`.
- Day, week and month buckets are computed in that zone. Default remains `UTC`, so every existing response is byte-identical when the parameter is omitted.
- Bucket labels are calendar dates in the requested zone, not instants.
- An unknown or malformed identifier returns 400 with a clear message; the API never silently falls back to UTC.
- DST transitions are handled: a 23-hour day and a 25-hour day each remain exactly one bucket.
- `from` and `to` are interpreted as instants (they are `DateTimeOffset`), while bucketing is calendar-based. Document this distinction explicitly — it is the crux of the story.
- Week bucketing states its start day; ISO weeks start Monday.
- Existing report tests pass unchanged.

**Files to open:** [AdminReportsController.cs](../src/Agora.Api/Controllers/AdminReportsController.cs), [AdminReportsApiTests.cs](../tests/Agora.Tests/Integration/AdminReportsApiTests.cs), [ReportTestScenario.cs](../tests/Agora.Tests/Integration/ReportTestScenario.cs). Proposed new files: `src/Agora.Api/Queries/ReportCalendar.cs`, `tests/Agora.Tests/Unit/ReportCalendarTests.cs`.

**Implementation plan**

1. Write the distinction down first: an *instant* is a point on the timeline; a *calendar day* is a local construct. `from`/`to` are instants; buckets are calendar. Most timezone bugs come from conflating these.
2. Create `ReportCalendar` wrapping a `TimeZoneInfo`, with `BucketKey(DateTimeOffset instant, string interval)` converting via `TimeZoneInfo.ConvertTime` before truncating.
3. Resolve zones with `TimeZoneInfo.FindSystemTimeZoneById`. .NET 10 accepts IANA identifiers on Windows, but verify on this machine and note any ICU dependency.
4. Write `ReportCalendarTests` as pure unit tests **before** touching the controller: UTC matches today's behaviour exactly; a UTC+2 zone shifts boundaries as expected; the spring-forward 23-hour day is one bucket; the autumn 25-hour day is one bucket; an instant at local midnight lands in the right bucket; ISO week spans a New Year correctly.
5. Add the parameter to both report actions, validate it, and pass the calendar into the bucketing path.
6. Replace the `BucketKey` call site. Keep the method signature stable so unrelated code is untouched.
7. Add API tests: default matches the previously recorded response byte-for-byte; an explicit zone produces different, correct buckets; an invalid zone returns 400.
8. Document in the API reference that totals are unchanged and only *grouping* moves — revenue must never differ between two zone renderings of the same range.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~ReportCalendar|FullyQualifiedName~AdminReportsApiTests"`.

**Common trap:** converting with a fixed offset instead of a zone. `UTC+2` is not `Europe/Berlin`; it is right for half the year and wrong for the other half. Only a zone knows DST.

**Done when:** DST boundary tests pass, the UTC default is provably unchanged, and you can explain why the grand total must be identical across zones while buckets differ.

---

## WD-22: Output caching with explicit invalidation

**Status:** Planned; not implemented. **Tier:** Advanced. **Prerequisite:** WD-06 (its `Vary` and public/private reasoning). **Starting knowledge:** caching, cache invalidation, tags.

**User story:** As a shopper during a traffic spike, I want popular catalog pages served from cache, so the site stays fast when the database is under load.

**Current behavior:** No server-side caching exists — `grep -rn "OutputCache\|IMemoryCache" src/` returns nothing. Every catalog request hits SQLite, which is single-writer and becomes the bottleneck first under read load.

**Acceptance criteria**

- Anonymous `GET /api/products` and `GET /api/products/{id}` are served from an output cache for a configurable 60 seconds.
- Cache entries are **tagged** (`catalog`, and `product:{id}`) so they can be evicted precisely.
- Any catalog write — product create, update, delete, variant edit, image change, import commit — evicts the affected tags **after** the transaction commits, never before.
- Authenticated requests **bypass the cache entirely**. Personalised pricing (quantity tiers, saved preferences) must never be served to another user.
- The cache key includes the full normalized query string, so `?page=2` and `?page=3` are distinct entries.
- A response served from cache is byte-identical to a fresh one.
- Caching is disabled in the `Testing` environment by default, following the precedent set by the outbox and export workers, and enabled explicitly by tests that exercise it.
- A stale read is impossible after a write returns 200 — test this directly.

**Files to open:** [Program.cs](../src/Agora.Api/Program.cs), [ProductsController.cs](../src/Agora.Api/Controllers/ProductsController.cs), [CatalogEditingController.cs](../src/Agora.Api/Controllers/CatalogEditingController.cs), [CatalogImportsController.cs](../src/Agora.Api/Controllers/CatalogImportsController.cs). Proposed new files: `src/Agora.Api/Caching/CatalogCacheTags.cs`, `tests/Agora.Tests/Integration/OutputCacheTests.cs`.

**Implementation plan**

1. **Enumerate every catalog write path before caching anything.** Missing one produces stale data that looks like a phantom bug. There are more than the obvious three — check the import, sync, cloning and editing controllers.
2. Register `AddOutputCache` with a named policy: 60-second expiry, vary by query and by the `Authorization` header's presence, and `NoCache` when authenticated.
3. Define tag constants in one place. Never build tag strings inline at call sites.
4. Apply `[OutputCache(PolicyName = ...)]` to the two read actions only.
5. Inject `IOutputCacheStore` into the write paths and call `EvictByTagAsync` **after** `SaveChangesAsync` succeeds and any transaction commits. Evicting inside a transaction that later rolls back caches the wrong state.
6. Wire `product:{id}` eviction for single-product writes and `catalog` for list-affecting writes; a create or delete affects both.
7. Disable by default under `Testing`; add a config flag so tests can turn it on deliberately.
8. Write tests: a second identical anonymous request is served from cache (assert via a counter or timing marker); an update evicts and the next read shows the new value; an authenticated request never receives a cached body; different query strings are separate entries; a rolled-back write leaves the cache untouched.
9. Document the staleness window in the API reference, consistent with WD-06's `max-age`.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~OutputCache|FullyQualifiedName~ProductsApiTests"`.

**Common trap:** caching authenticated responses because the key "includes the token". It usually does not by default — and if it does, you have built a per-user cache with almost no hit rate and a large memory cost. Bypass instead.

**Done when:** the no-stale-read-after-write test passes, every write path evicts, authenticated bypass is proven, and you can name each write path you found.

---

# Expert tier

Architectural work. Each of these changes how the system is operated, how its data ages, or how its money behaves. Write a design document and get it reviewed before writing code.

## WD-23: Tracing and metrics for checkout

**Status:** Planned; not implemented. **Tier:** Expert. **New package required.** **Prerequisite:** WD-01. **Starting knowledge:** the checkout pipeline, `Activity`, `Meter`.

**User story:** As an on-call engineer, I want to see where a slow checkout spent its time and how often checkouts fail by reason, so I can diagnose an incident from telemetry instead of reading source code.

**Current behavior:** Observability is one line of HTTP logging per request — method, path, status, duration. There is no tracing, no metrics, and no way to see *inside* a request. When checkout is slow, nothing distinguishes pricing from stock reservation from the gateway call.

**Acceptance criteria**

- OpenTelemetry tracing covers ASP.NET Core, EF Core and `HttpClient`.
- [CheckoutService](../src/Agora.Infrastructure/Services/CheckoutService.cs) emits custom spans for its distinct phases: pricing, reservation, order persistence, gateway charge, and the completion transaction.
- The gateway span records outcome and failure reason as attributes, and sets the span status to error on a decline.
- Custom metrics: a checkout counter tagged by outcome (`succeeded`, `declined`, `insufficient_stock`); a histogram of checkout duration; a counter of webhook deliveries by outcome, reusing the existing vocabulary (`Succeeded`, `Failed`, `Unknown`) from [WebhookOutboxWorker](../src/Agora.Infrastructure/Services/WebhookOutboxWorker.cs).
- The WD-01 correlation ID is attached to the root span so a log line and a trace can be joined.
- **No personal data in attributes.** No email addresses, no full addresses, no payment tokens, no gift-card codes. Write the allow-list explicitly.
- An OTLP exporter is configurable; the console exporter is the default in Development so the feature is usable with no infrastructure.
- Telemetry is disabled by default under `Testing`, with a test-only in-memory exporter used to assert on spans.
- **Zero behaviour change.** A failure in the telemetry pipeline must never fail a checkout.

**Files to open:** [Program.cs](../src/Agora.Api/Program.cs), [CheckoutService.cs](../src/Agora.Infrastructure/Services/CheckoutService.cs), [WebhookOutboxWorker.cs](../src/Agora.Infrastructure/Services/WebhookOutboxWorker.cs), [Agora.Api.csproj](../src/Agora.Api/Agora.Api.csproj). Proposed new files: `src/Agora.Infrastructure/Telemetry/AgoraTelemetry.cs`, `tests/Agora.Tests/Integration/TelemetryTests.cs`.

**Implementation plan**

1. Write the telemetry design first: span names, the attribute allow-list, metric names and units, and cardinality limits. **Never tag a metric with an order number or customer ID** — that is unbounded cardinality and it will take down a metrics backend.
2. Add `OpenTelemetry.Extensions.Hosting` plus the ASP.NET Core, EF Core and OTLP exporter packages, pinned.
3. Create `AgoraTelemetry` holding a static `ActivitySource` and `Meter` with a single shared name constant. Put it in Infrastructure so both the worker and the services can use it.
4. Register tracing and metrics in [Program.cs](../src/Agora.Api/Program.cs), gated by configuration, defaulting to console in Development and off in Testing.
5. Instrument `CheckoutAsync` phase by phase. Use `using var activity = AgoraTelemetry.Source.StartActivity("checkout.pricing")`. `StartActivity` returns null when no listener is attached — use `?.` everywhere and never assume a span exists.
6. Record metrics at the outcome points, including the decline path that currently throws `PaymentFailedException`.
7. Instrument the webhook sender's three outcomes, mapping the existing reason codes to metric tags.
8. Attach the correlation ID from `HttpContext.Items` to the root activity as a tag.
9. Write `TelemetryTests` using an in-memory `ActivityListener`: a successful checkout produces the expected span names in order; a declined checkout marks the gateway span as error; no span attribute contains an email address or payment token; and telemetry being disabled leaves checkout behaviour identical.
10. Write a runbook note: what to look at first when checkout latency alerts.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~TelemetryTests|FullyQualifiedName~CheckoutApiTests"`. Demo by running with the console exporter and performing a checkout.

**Common trap:** high-cardinality metric tags. Tagging a counter with order number creates one time series per order. This is the most common way to break a metrics system, and it is invisible in local testing.

**Done when:** span order is asserted, the no-personal-data test passes, cardinality is bounded by design, and you can explain the difference between a trace and a metric.

---

## WD-24: Soft delete with retention and purge

**Status:** Planned; not implemented. **Tier:** Expert. **Migration required.** **Starting knowledge:** EF query filters, the existing export feature, data protection principles.

**User story:** As a merchant, I want deleted records recoverable for a defined window and then permanently removed, so I can undo mistakes while still honouring data-retention promises.

**Current behavior:** Deletion is inconsistent. `WebhookSubscription` has an `IsDeleted` flag honoured by the outbox query; most other entities are hard-deleted. There is no retention policy, no restore path, no purge worker, and no erasure mechanism. [AccountExportService](../src/Agora.Infrastructure/Services/AccountExportService.cs) can export a customer's data but nothing can erase it.

**Acceptance criteria**

- A shared soft-delete convention: `DeletedAt` and `DeletedBy` on chosen entities, applied through an EF **global query filter** so ordinary queries exclude deleted rows automatically.
- Applies to a **deliberately chosen** list — products, categories, reviews, wishlists — and explicitly **not** to orders, order items, fulfillments, returns or gift-card entries. Financial and legal records are never soft-deleted; document each inclusion and exclusion.
- Admin restore endpoints recover a soft-deleted row within the retention window.
- An `IgnoreQueryFilters()` admin view lists deleted rows.
- A purge worker permanently removes rows past retention (default 30 days, configurable), following the [WebhookOutboxWorker](../src/Agora.Infrastructure/Services/WebhookOutboxWorker.cs) pattern: hosted service, options, disabled under `Testing`, exception-tolerant.
- Purge respects referential integrity: a product referenced by a historical order item is **never** purged. Order history must remain readable forever.
- A customer erasure path anonymises personal fields on records that must be retained for accounting, rather than deleting the financial record.
- The existing `WebhookSubscription.IsDeleted` behaviour is either migrated onto the convention or explicitly excluded — do not leave two mechanisms undocumented.
- An ADR records the retention policy.

**Files to open:** [AgoraDbContext.cs](../src/Agora.Infrastructure/Persistence/AgoraDbContext.cs), [Webhook.cs](../src/Agora.Domain/Entities/Webhook.cs), [AccountExportService.cs](../src/Agora.Infrastructure/Services/AccountExportService.cs), [WebhookOutboxWorker.cs](../src/Agora.Infrastructure/Services/WebhookOutboxWorker.cs). Proposed new files: `src/Agora.Domain/Entities/ISoftDeletable.cs`, `src/Agora.Infrastructure/Services/RetentionPurgeWorker.cs`, `docs/adr/0011-soft-delete-and-retention.md`.

**Implementation plan**

1. **Write the ADR first.** Table by table, decide soft-delete, hard-delete or never-delete, and give the reason. This classification *is* the feature; the code is mechanical afterwards.
2. Define `ISoftDeletable` with `DeletedAt` and `DeletedBy`. Implement it only on the chosen entities.
3. Apply global query filters in `OnModelCreating`, iterating the model's entity types rather than writing each filter by hand.
4. Create the migration. Verify the generated SQL adds nullable columns with indexes on `DeletedAt` where purge will scan.
5. **Audit every existing query for filter interactions.** A global filter silently changes results everywhere, including in joins — an unfiltered principal with a filtered dependent can produce surprising results. Run the full 820-test suite after this step alone, before adding any new behaviour.
6. Change the chosen delete endpoints from `Remove` to setting the fields. Verify each returns the same status code as before.
7. Add restore and deleted-list endpoints with `IgnoreQueryFilters()`.
8. Write the purge worker. Select candidates past retention, check inbound references before deleting each, and delete in bounded batches to avoid a long-running transaction.
9. Write the anonymisation path for erasure: replace email and name with a deterministic placeholder on retained financial records; leave amounts and dates intact.
10. Write tests: a soft-deleted product disappears from public queries; an admin can see and restore it; an order referencing a soft-deleted product still renders fully; purge removes an unreferenced expired row; purge **refuses** a referenced row; anonymisation preserves totals; and a full-suite regression proving the global filters broke nothing.

**Test and demo:** `dotnet test Agora.slnx` in full — this story's main risk is action at a distance, so the whole suite is the test.

**Common trap:** adding a global query filter and testing only the new endpoints. The filter changes every existing query touching that entity. The 820-test regression is not optional here.

**Done when:** the ADR classifies every table, the full suite passes, purge refuses referenced rows, and you can explain why an order is never soft-deleted.

---

## WD-25: Multi-currency with rate snapshots

**Status:** Planned; not implemented. **Tier:** Expert. **Migration required.** **Starting knowledge:** [Money](../src/Agora.Domain/Common/Money.cs), [ADR 0001](../docs/adr/0001-decimal-as-cents.md), [ADR 0002](../docs/adr/0002-money-value-object.md), the pricing pipeline.

**User story:** As an international shopper, I want to see prices and pay in my own currency, so I know what I am spending without doing conversion myself.

**Current behavior:** [Money](../src/Agora.Domain/Common/Money.cs) carries an ISO currency code and `EnsureSameCurrency` throws on mismatched arithmetic — good discipline that this story must preserve. But there is no conversion anywhere. The [review findings](../docs/learning/review-findings.md) state plainly that "currency filtering does not convert currencies": `?currency=EUR` filters to variants already priced in EUR rather than converting. A product priced only in USD is invisible to a EUR shopper.

**Acceptance criteria**

- An `ExchangeRate` entity stores `FromCurrency`, `ToCurrency`, `Rate`, `EffectiveFrom` and `Source`, keeping full history. Rates are never updated in place.
- Rates are stored at high precision using a dedicated converter, following the millionths precedent from [ADR 0001](../docs/adr/0001-decimal-as-cents.md) — cents would destroy a rate like `0.9213`.
- A `displayCurrency` query parameter converts catalog prices for display, marking responses as converted with the rate and its timestamp.
- **Checkout charges in the store's base currency unless a variant is genuinely priced in the requested currency.** Display conversion and settlement are different things; conflating them causes real financial loss.
- When an order *is* placed in a converted currency, the order stores the **rate snapshot used** — order totals must never change because a rate moved afterwards.
- Refunds and returns use the **order's stored rate**, never the current one. This is the single most important rule in the story.
- Rounding is applied once, at the end of a calculation chain, and the rounding rule is documented. Converting each line separately and summing gives a different total than converting the sum.
- `Money.EnsureSameCurrency` is **not** relaxed. Conversion is an explicit operation producing a new `Money`, never an implicit coercion.
- Missing rate for a requested pair returns 422, never a silent 1:1 fallback.
- Existing single-currency behaviour is byte-identical when no `displayCurrency` is supplied.

**Files to open:** [Money.cs](../src/Agora.Domain/Common/Money.cs), [SqliteValueConverters.cs](../src/Agora.Infrastructure/Persistence/SqliteValueConverters.cs), [CheckoutPricingService.cs](../src/Agora.Infrastructure/Services/CheckoutPricingService.cs), [ReturnService.cs](../src/Agora.Infrastructure/Services/ReturnService.cs), [Order.cs](../src/Agora.Domain/Entities/Order.cs), [MoneyTests.cs](../tests/Agora.Tests/Unit/MoneyTests.cs). Proposed new files: `src/Agora.Domain/Entities/ExchangeRate.cs`, `src/Agora.Domain/Services/CurrencyConverter.cs`, `tests/Agora.Tests/Unit/CurrencyConversionTests.cs`.

**Implementation plan**

1. **Write the design document first and have it reviewed.** State explicitly: what is display-only, what is settlement, when a rate is captured, and which rate a refund uses. Getting this wrong loses money quietly and is very hard to detect later.
2. Read [ADR 0001](../docs/adr/0001-decimal-as-cents.md) and the existing millionths converter for tax rates. Reuse that exact technique for rate storage; do not invent a second precision scheme.
3. Add `ExchangeRate` with a unique index on `(FromCurrency, ToCurrency, EffectiveFrom)`. Never update a row — insert a new effective-dated one.
4. Write `CurrencyConverter.Convert(Money, targetCurrency, ExchangeRate)` as a **pure domain function**. No database access, no ambient clock. Rate selection is the caller's job.
5. Write `CurrencyConversionTests` before any wiring: exact-boundary rounding, a rate of 1, an identity conversion, a missing rate, and a chain of conversions proving convert-then-sum differs from sum-then-convert. Pick one and document it.
6. Add rate lookup with effective-date selection: the latest rate whose `EffectiveFrom` is at or before the reference instant.
7. Add `displayCurrency` to catalog reads. Convert for display only, and include `convertedFrom`, `rate` and `rateAsOf` in the response so the client can show a disclosure.
8. Add rate-snapshot columns to `Order` (`SettlementCurrency`, `DisplayCurrency`, `RateUsed`, `RateAsOf`). Create the migration; existing orders backfill as base-currency with a null rate, and an upgrade test must prove old orders still render.
9. Update [ReturnService](../src/Agora.Infrastructure/Services/ReturnService.cs) and the refund path to read the order's stored rate. Write an explicit test: place an order, change the rate substantially, then refund — the refund must match the original amount.
10. Add an admin rate-management endpoint and seed a small rate table in development.
11. Run the full suite. Money touches nearly everything; a regression here is a financial bug.

**Test and demo:** `dotnet test --filter "FullyQualifiedName~CurrencyConversion|FullyQualifiedName~MoneyTests|FullyQualifiedName~RefundTender"` then the full suite.

**Common trap:** converting at refund time with the current rate. The customer paid €92 when the rate was 0.92; refunding at 0.95 returns €95. Repeated across a catalogue this is a slow, silent loss — and it looks correct in every unit test that does not move the rate.

**Done when:** the rate-moves-then-refund test passes, `EnsureSameCurrency` is intact, the display/settlement boundary is documented, and you can explain why rounding order changes the total.

---

# Coverage summary

What these 25 stories add to the curriculum, relative to the 75 already complete:

| Web-development area | Previously covered | Added by |
| --- | --- | --- |
| Middleware and the request pipeline | Not at all | WD-01, WD-02, WD-11 |
| HTTP caching semantics | Conditional reads only, in one controller | WD-06, WD-10, WD-22 |
| Content negotiation and compression | Not at all | WD-11, WD-18 |
| CORS and browser security | Not at all | WD-02, WD-07 |
| API lifecycle: versioning, deprecation, schema | Not at all | WD-04, WD-08, WD-09 |
| Error contract for machines | Human-readable only | WD-03 |
| Rate limiting | Checkout only, no headers | WD-12 |
| Account security flows | Login and sessions only | WD-13, WD-14, WD-15 |
| Request idempotency | Identified as open, not built | WD-16 |
| Outbound request security | Not at all | WD-17 |
| File handling | Not at all | WD-18 |
| Real-time / streaming | Not at all | WD-19 |
| Search relevance | Substring matching only | WD-20 |
| Time zones and calendars | UTC only, flagged as a limit | WD-21 |
| Observability: tracing and metrics | One log line per request | WD-01, WD-23 |
| Data lifecycle and erasure | Export only, no deletion | WD-24 |
| Multi-currency | Explicitly not converted | WD-25 |

## Concepts these stories reinforce

Every story deliberately re-uses something the codebase already teaches, so the curriculum compounds rather than branching:

- **Capability tokens with digest storage** ([GuestOrderAccessService](../src/Agora.Infrastructure/Services/GuestOrderAccessService.cs)) → WD-14, WD-15
- **Lease-based durable work** ([WebhookOutboxWorker](../src/Agora.Infrastructure/Services/WebhookOutboxWorker.cs)) → WD-16, WD-24
- **Options validated at startup** (`ReturnPolicyOptions`) → WD-02, WD-06, WD-12
- **ETag computed over the exact returned bytes** ([ProductInsightsController](../src/Agora.Api/Controllers/ProductInsightsController.cs)) → WD-10, WD-18
- **Same-variant predicate correctness** ([ProductCatalogQuery](../src/Agora.Api/Queries/ProductCatalogQuery.cs)) → WD-20
- **Millionths precision for rates** ([ADR 0001](../docs/adr/0001-decimal-as-cents.md)) → WD-25
- **Migration upgrade tests that downgrade and re-upgrade** → WD-13, WD-14, WD-18, WD-24, WD-25
- **Controlled clocks instead of sleeping** (`AuthenticationTimeProvider`) → WD-13, WD-14, WD-15, WD-21

## Suggested sequence

**If you want breadth quickly:** WD-01 → WD-02 → WD-03 → WD-05 → WD-06. Five foundation stories teach the whole middleware and HTTP-header surface in about a week.

**If you want depth in one area:**
- *HTTP semantics:* WD-06 → WD-10 → WD-11 → WD-22
- *Security:* WD-02 → WD-07 → WD-13 → WD-15 → WD-17
- *Operability:* WD-01 → WD-05 → WD-12 → WD-23
- *Data and money:* WD-21 → WD-24 → WD-25

**If you want the single highest-value story:** WD-16. Idempotency is the concept most often missing from production APIs, it closes a gap this repository has already identified in its own review findings, and it exercises transactions, unique constraints, concurrency and API design at once.

---

*These 25 stories were selected on 9 September 2026 after reviewing the working tree against the 75 completed stories. Every "current behavior" statement was verified against the code in this repository at that time. None of the stories has been implemented.*
