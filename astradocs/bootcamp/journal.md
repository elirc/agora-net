# Implementation journal

[Bootcamp home](README.md) | [Live tracker](story-tracker.md)

This journal records actual work and verification. Planned steps are labeled as plans; no historical test count is reused as proof of a new implementation.

## 2026-09-08: Scope and baseline

**User priority:** implement all user stories while making the upskilling material the main deliverable. Scope is 25 junior + 30 mid-level + 20 mid/senior stories, 75 total. This supersedes the earlier instruction to write plans only.

**Observed starting state:** the three specifications and earlier onboarding materials exist. Catalog search already has a query helper and literal LIKE escaping. Existing changes from prior work are present and must be preserved. The ordinary integration fixture uses one in-memory SQLite connection and EnsureCreated, so it cannot prove migration upgrades or realistic competing-connection behavior.

**Decision:** work through coherent modules and keep a per-story tracker. Each module gets actual code links, worked examples, exercises with explanations, and verification evidence. Additive DTO/query work comes first; schema, access-contract changes, and workers follow with their own regression and rollout checks.

**Validation started:** a focused pre-change baseline for MoneyTests, CartTests, and InventoryItemTests, using the installed .NET SDK. Results are pending at this entry. Command: `dotnet test Agora.slnx --no-restore --filter "FullyQualifiedName~MoneyTests|FullyQualifiedName~CartTests|FullyQualifiedName~InventoryItemTests" --logger "trx;LogFileName=baseline-domain.trx" --results-directory artifacts/bootcamp/baseline -m:1`. The process limits reported processor count to two to avoid excessive contention on this workstation.

**Next implementation:** computed response values, explicit variant weight, stable product mapping, category/catalog filters, and their focused acceptance tests. No stories are marked complete yet.

## Junior module implementation and first failing tests

**Baseline result:** all 30 selected domain tests passed (zero failures/skips). The TRX is at `artifacts/bootcamp/baseline/baseline-domain.trx`.

**Observed red test:** BootcampResponseContractTests ran against the pre-response-change assembly: 13 failed, zero passed. Failures were missing JSON fields and the intentionally reversed variant ordering, not setup/restore errors. Evidence: `artifacts/bootcamp/module-01/module-01-red.trx`.

**Implemented, not yet verified:** the 25 junior stories now have implementation changes: computed fields, explicit weight mapping, stable image/variant order, category/catalog/review/shipping/wishlist/account filters, named-enum parsing, address lookup, wishlist clear, and reversed-report-date validation. A shared QueryRules helper contains only three concrete reusable operations: literal substring escaping, widened page validation, and named-enum parsing.

**Teaching decisions:** SKU stays inside the existing same-variant Any predicate. Counts are derived from response collections rather than stored columns. Ownership stays in database predicates. Category parent validation runs before field assignment. Tests use counterexamples and fresh-context assertions after rejected writes.

**Verification in progress:** BootcampJuniorApiTests exercises these contracts through HTTP with scoped fixture data; BootcampResponseContractTests checks pure response semantics and serialization. The current run writes `artifacts/bootcamp/junior/junior-acceptance.trx`. Completion statuses remain pending until the run and relevant regressions pass.

**First green acceptance result:** 28 tests passed, zero failed/skipped. This includes all 13 previously failing response tests. The HTTP cases cover the junior feature families; existing tests supply additional regression scenarios.

**Boundary review after green:** country format must be checked before uppercasing. Some non-ASCII letters can uppercase to ASCII; accepting them would violate the promised ASCII-only input rule. Validation now checks the trimmed original characters first, with a long-s counterexample. Added explicit maximum-weight acceptance and valid-parent/root update checks. These additions require another build and regression run before completion.

## Junior regression complete; bounded reads begin

**Observed regression result:** 483 tests passed, zero failed/skipped. Evidence: `artifacts/bootcamp/junior/junior-regression.trx`. Command: `dotnet test Agora.slnx --no-restore --nologo --logger "trx;LogFileName=junior-regression.trx" --results-directory artifacts/bootcamp/junior -m:1 -p:UseSharedCompilation=false`, with `DOTNET_PROCESSOR_COUNT=2`. Test execution took 7 minutes 19 seconds. The build emitted two existing nullable warnings in AddressBookApiTests and AdminReportsApiTests. All 25 junior stories now have implementation, acceptance/regression evidence, API reference changes, and modules 1–3.

**Evidence boundary:** this regression does not verify later files written while it was running. Product comparison, review summaries, and the subsequent wishlist schema changes require their own new build and tests.

**MS-03/MS-17 implementation:** ProductInsightsController batches comparison reads, rejects the entire comparison when an ID is unusable, and constructs output in input order. Review summaries group only approved reviews, fill five buckets, and hash the actual response bytes for conditional GET. ProductInsightsApiTests includes command-count observation for two versus four products and moderation/validator counterexamples. Module 4 teaches these choices through concrete examples, code traces, and exercises. Verification is pending.

**MS-14/MS-15 work started:** private notes have an independent note revision; wishlist membership has its own revision. Note updates intentionally avoid the stock-observation helper. Observation writes remain subject to EF concurrency checks and use the existing documented 409 mapping if a note changes concurrently; they do not silently overwrite the note. Copying preserves source rows and skips existing target variants. One SaveChanges transaction combines inserts with the conditional parent update. An all-skipped copy still issues a conditional parent check but does not advance its revision. Existing add/remove/clear/move paths participate; product deletion also advances affected wishlist revisions before its cascade. Migration and focused acceptance tests are still pending.

## Bounded-read acceptance passed; wishlist migration generated

**Observed result:** ProductInsightsApiTests plus the existing WishlistsApiTests passed: 17 total, zero failures/skips, 1 minute 7 seconds of test execution. Evidence: `artifacts/bootcamp/module-04/insights-wishlist.trx`. The comparison query-count assertion passed for two and four products. The exact-byte ETag, validator list/weak/wildcard behavior, and moderation changes passed. A new nullable header-parser warning was corrected by filtering null header entries; that correction requires the next build. Existing product/catalog regressions will run in the next full suite before the two mid-level stories are marked complete.

**Migration generated and inspected:** `20260908190324_WishlistNotesAndMembership` adds exactly three columns: nullable Note, NoteVersion default zero, and MembershipVersion default zero. The snapshot adds the note length and two concurrency tokens. No existing values are rewritten by Up. The tool ran with the Testing environment and `--no-build` against the newly compiled model. No application database was upgraded by this command.

**New verification:** WishlistEditingApiTests covers private-note input, ownership, stale edits, copying/overlap/no-op behavior, fresh stock observations, source preservation, and the existing membership routes including product cascades. WishlistConcurrencyTests uses separate connections and controlled stale reads to check conditional saves, unique membership, and rollback. WishlistMigrationTests creates a preceding-schema fixture with rows, upgrades it, and checks identity preservation plus null/zero defaults. These tests are written but their results are pending in the full `wishlist-migration-regression.trx` run.

**Learning content:** module 5 traces the ownership predicate, two version checks, source/target sets, the one-save transaction, and old-schema verification. The personal learning log supplies worked wrong predictions and a reusable template; it is separate from this implementation evidence journal.

## Catalog organization implementation started

**Evidence boundary:** after the wishlist build compiled the application assemblies and began its test phase, work started on MS-01/MS-02 in source. The running wishlist suite cannot verify these subsequent changes.

**Implementation:** tags use normalized immutable slugs, a unique slug index, a product/tag join key, and Product.TagVersion. Assignment validates the whole requested set before replacing membership. Collections store ordered product links under a collection revision, keep drafts private, preserve inactive members for editors, and filter active members before public count/paging. Product deletion advances affected collection revisions before cascading children.

**Refactor:** ProductReadQueries now supplies the response-data includes and approved-review aggregation shared by ordinary product reads and collection pages. It includes tags in batches and uses split queries. Product update also loads this full response graph so changing metadata cannot accidentally return an empty tag list.

**Pending evidence:** CatalogOrganizationApiTests and CatalogOrganizationPersistenceTests cover request behavior, stale replacements, and a preceding-schema upgrade. The migration and build are still pending. Workshop 4b teaches the set/sequence distinction, complete replacement, unique constraints, and the difference between admin membership and public visibility. No catalog-organization story is marked complete yet.

## Wishlist upgrade and concurrency regression passed

**Observed full result:** 499 tests passed, zero failed/skipped, in 16 minutes 48 seconds of test execution. Evidence: `artifacts/bootcamp/module-05/wishlist-migration-regression.trx`. This includes the wishlist preceding-schema upgrade, five independent-connection concurrency cases, note/copy HTTP acceptance, the product-deletion membership audit, comparison query counts, and review-summary validators. It also reran the existing product/catalog suites. MS-03, MS-14, MS-15, and MS-17 are now complete; together with the 25 junior stories, 29 of 75 are verified.

**Build isolation:** while that Debug test process was running, catalog organization was compiled under the separate BootcampCatalog configuration. That build succeeded with zero warnings/errors. The CatalogOrganization migration was generated with `dotnet ef migrations add CatalogOrganization --project src/Agora.Infrastructure --startup-project src/Agora.Api --configuration BootcampCatalog --no-build` in Testing. Its test results are still pending. A test-analyzer suggestion in WishlistConcurrencyTests was corrected after the Debug build; the equivalent assertion will compile in the next run.

**MS-06 started:** draft cloning reads the source graph in one query with a 51st-variant sentinel, validates exact SKU mappings, creates new objects and option dictionaries, resets inventory, and excludes operational membership/history. Shared ProductInputRules keeps creation and cloning identity lengths, trimming, and request-SKU uniqueness aligned. API and pure graph-aliasing tests are written; build/test/lesson completion remains pending. This feature needs no schema change.

**Test-command correction:** invoking `dotnet test Agora.slnx --configuration BootcampCatalog` failed before any tests ran with MSB4126: that configuration is not listed in the solution. The API project build had accepted the custom configuration. The corrected command targets `tests/Agora.Tests/Agora.Tests.csproj` directly with the same configuration, keeping outputs isolated without changing solution configuration. This was a command/setup failure, not a failed feature assertion.

## Variant and gallery editing underway

**Scope:** MS-04 adds a variant revision and a domain edit method that validates every replacement value before assignment, keeps SKU/currency immutable, and copies normalized options. MS-05 adds a separate product gallery revision, exact-permutation ordering, bounded additions, and removal with compact positions. Admin endpoints perform expected-version checks; EF tokens provide the second check at persistence. Migration and tests are not yet verified.

**Integration decision:** ordinary new-product creation now accepts at most ten initial images, matching the gallery-add limit. Existing larger galleries remain readable/reorderable/removable. Draft cloning retains the explicit MS-06 promise to preserve the source gallery, including a larger legacy gallery; a cloned oversized gallery cannot accept further gallery additions until reduced below ten. No migration deletes old images. This preserves legacy catalog values while bounding ordinary new additions.

**Evidence boundary:** the catalog-organization/cloning test build finished before these editing changes were compiled. Its running BootcampCatalog tests cannot verify variant/gallery editing. A separate Debug API build is preparing that next migration.

**Catalog red result observed:** the published-collection read returned HTTP 500 in `Collections_preserve_editorial_order_filter_inactive_members_and_keep_drafts_private`. The original shape projected a product navigation from ordered collection items and then applied split response includes. The revised shape first selects the bounded ordered product IDs, batch-loads products from the product root, and explicitly restores input order. Missing/inactive products during an intervening change are omitted safely. The rerun is pending; the exact provider exception has not been captured, so the query-shape diagnosis remains an inference until verification.

**Build-boundary correction:** the variant-edit DTO began referencing VariantOptionsJsonConverter after the running Debug build had already evaluated its source-file list. That build could not find the newly added file (CS0246). The converter exists under Contracts; a fresh project build is running with the complete file list. API source additions are now paused until that build completes. The converter itself rejects repeated raw JSON option keys before normal dictionary deserialization could silently overwrite them; domain normalization separately rejects trimmed/case-insensitive duplicates.

**Catalog run completed red:** `artifacts/bootcamp/module-04b/catalog-organization.trx` reports 89 tests: 88 passed, one failed, zero skipped, in 6 minutes 7 seconds. The failure was the published-collection read described above. Cloning, tags, their migration/concurrency checks, and the selected regressions passed in this run, but the catalog group remains pending until the corrected collection read is verified. The next Debug run will include that fix plus variant/gallery editing and their migration.

**Root cause confirmed from the TRX application log:** EF raised InvalidOperationException because Include was applied after Select projected a different entity through a navigation. The first rejected include was `p => p.Variants`. This confirms the earlier query-shape diagnosis. Loading page IDs and then querying the Product root avoids that unsupported shape while explicitly restoring collection order. The database unique-SKU exceptions elsewhere in the log belong to the passing intentional rollback test, not additional failed tests.

**Editing build and migration:** the fresh Debug API build passed with zero warnings/errors. `20260908195106_VariantAndGalleryRevisions` was generated and inspected: it adds only ProductVariants.Version and Products.ImageRevision, both defaulting to zero. Workshop 4d and the API reference now explain the endpoints, validation boundaries, historical order snapshots, legacy galleries, and the new columns.

**Additional cloning boundary:** new image IDs can reverse visible image order when old SortOrder values tie. The cloner now assigns sorted fresh IDs in the source's visible order, preserving both sort values and tie order. A unit test covers this interaction between new identity and stable mapping; it is included in the next run.

**Verification in progress:** the Debug test-project run writes `artifacts/bootcamp/module-04d/catalog-editing.trx`. It includes catalog organization, the collection query fix, cloning, variant/gallery acceptance and independent-connection tests, migration upgrades, product/search/review insights, wishlists, tax, checkout, and cart regressions. API/domain source is held stable for this build. Results remain pending.

## Catalog workshops verified: 34 stories complete

**Observed result:** `artifacts/bootcamp/module-04d/catalog-editing.trx` reports 147 passed, zero failed/skipped, in 3 minutes 50 seconds. The formerly failing published-collection read now passes. The run includes all new catalog feature tests, their migration/concurrency checks, cloning alias/rollback/tie-order checks, real checkout snapshot behavior, strict option parsing, legacy gallery limits, and the selected product/cart/checkout/tax/wishlist regressions. MS-01, MS-02, MS-04, MS-05, and MS-06 are now complete. Total verified stories: 34 of 75.

**Next module begun:** MS-30 has an admin webhook-health route and a database aggregate query. It captures one TimeProvider instant, selects deliveries by creation time, computes current outcome counts and lifetime attempts, and obtains overall/page counts inside one short transaction. It exposes only safe counts and subscription IDs, not subscription/delivery entities. TimeProvider.System is registered for this and upcoming time-dependent features. Tests and the operational-report lesson are in progress; this source was added after the catalog application build and is not covered by its passing result.

## Webhook health verified: 35 stories complete

**Observed result:** `dotnet test tests/Agora.Tests/Agora.Tests.csproj --no-restore --filter 'FullyQualifiedName~Webhook' --logger 'trx;LogFileName=webhook-health.trx' --results-directory artifacts/bootcamp/module-06a -m:1 -p:UseSharedCompilation=false` passed 18 tests, zero failed/skipped, in 4 minutes 49 seconds. Evidence: `artifacts/bootcamp/module-06a/webhook-health.trx`. This covers the new cohort report and existing webhook behavior. MS-30 is complete; total 35 of 75.

**Teaching evidence:** [Workshop 6a](06a-webhook-health.md) works the counts by hand, repeats cohort selection through a birthday analogy and a code trace, explains weighted overall ratios and exclusive end dates, and includes five questions with answers. The report counts current outcomes and lifetime attempts for a creation cohort; it does not invent timestamped attempt history.

**Next work:** MS-22 renders an encoded packing slip from order snapshots with a 500-line sentinel and separately aggregated fulfillment quantities. MS-27 derives the fulfillment queue from ordered minus shipped quantities, filtering eligible orders before pagination and avoiding inventory eligibility checks. Their runtime code was added after the webhook test build; those 18 tests do not verify these new routes. Tests, lessons, and packing-slip print inspection remain pending.

## Fulfillment reports: automated checks passed

**Observed result:** `artifacts/bootcamp/module-06b/fulfillment-reports.trx` reports 25 passed, zero failed/skipped, in 4 minutes 17 seconds. The filter selected PackingSlip, FulfillmentQueue, FulfillmentsApiTests, and CheckoutApiTests. It verifies partial/full quantities, snapshot output, hostile text encoding, forbidden callers/states, line limits, inconsistent quantities, filtered pagination, paid-time boundaries, and zero-stock queue inclusion. MS-27 is complete; total 36 of 75. MS-22 remains awaiting its separate print-layout inspection.

**Teaching:** [Workshop 6b](06b-packing-work-and-safe-documents.md) repeats stock-versus-work through two equations, a partial-shipment example, a request trace, and explain-back exercises. It also explains why a narrow renderer model and text-node encoding are separate controls.

**Print setup:** a temporary console project under ignored `artifacts/bootcamp/print-preview` invokes the actual renderer for two-line and 65-line documents with long names and addresses. Windows application control rejected the generated native apphost; invoking the compiled managed DLL through the installed dotnet runtime succeeded. Edge headless print output is being generated for visual inspection. This is a development-only artifact, not an added application dependency or PDF endpoint.

**Stock reports started:** MS-08 adds one optional, versioned reorder policy per variant; MS-09 separately calculates demand from payment-time cohorts and approved returns. Workshop 6c and tests are written. The BootcampInventory API build passed with one nullable warning, then the source annotation was corrected. The policy migration is being generated from that compiled model. These changes are not part of the passing Debug fulfillment report run.

**Print inspection found a defect:** Edge generated a one-page two-line slip and a nine-page 65-line slip. I visually inspected rasterized page 1 of the short document and pages 2 and 9 of the long document. Item names, literal script-like text, repeated table headers, and row/page boundaries were visible without clipping. However, the Remaining header wrapped its final letter onto a second line. MS-22 stays pending while quantity-column widths are corrected and the printed output is inspected again. The ignored inspection tool installs PyMuPDF only into the artifact directory; it is not a product dependency.

## Stock-policy checks passed; replenishment exposed a provider limitation

**Observed result:** `artifacts/bootcamp/module-06c/stock-reports.trx` reports 27 tests: 24 passed, three failed, zero skipped, in 2 minutes 15 seconds. All reorder-policy API, report, independent-connection concurrency, migration, existing inventory/admin-report, and fulfillment-queue checks passed. All three failures are ReplenishmentReportApiTests receiving HTTP 500. MS-08 is complete; total 37 of 75. MS-09 remains in progress.

**Migration evidence:** `20260908203717_InventoryReorderPolicies` creates only the optional policy table, keyed by variant ID with cascade deletion. The upgrade test preserves on-hand/reserved/revision values and confirms no default policies were inserted. The new queue SQL-count test also passed, checking a fixed command count for one versus three orders and no inventory-table reads or data mutations.

**Failure diagnosis:** the TRX application log records EF InvalidOperationException translating the replenishment consistency predicate. The query tested an entire left-joined anonymous aggregate object for null. The revised projection makes the returned-unit sum nullable and coalesces that scalar to zero instead. It retains separate sales/return aggregates and database-side filtering. A rerun must verify this fix; the report is not being switched to unbounded in-memory evaluation.

**Bulk adjustment build:** the new receipt entities, canonical command, transactional service, admin endpoints, and DI/error mappings compiled successfully in Debug with zero warnings/errors. HTTP, fingerprint, independent-connection replay, injected database-failure rollback, and upgrade tests are written but not yet run. The next migration will add receipt history without changing existing stock. The running stock report tests used the earlier BootcampInventory build and do not cover this batch feature.

## Packing slip visual check complete: 38 stories complete

**Correction and evidence:** quantity columns now use 14% width with non-wrapping header labels; SKU uses 16%, and body text can wrap long unbroken values. The temporary print project compiles the actual renderer source directly. Its build passed with zero warnings/errors. Edge regenerated `packing-2-v2.pdf` (one page) and `packing-65-v2.pdf` (nine pages). I visually inspected the short page and pages 2 and 9 of the long document: Remaining now stays on one line, quantity columns align, long names wrap inside their column, table headers repeat, and complete rows remain readable through the last page. This is inspected headless browser print output, not a claim that an interactive print-preview window was opened. MS-22 is complete; total 38 of 75.

**Integration gap found and closed in source:** the batch request needs an inventory revision, but the existing public stock response exposed quantities and variant ID without that revision. InventoryResponse now additionally returns the stock `version`, enabling clients to construct commands from GET `/api/inventory/{sku}`. This is separate from product-variant editing and reorder-policy revisions. A batch API test verifies that observation path. The next test run covers the additive contract change.

**Current verification:** the receipt migration has been generated. A Debug test-project run targets batch/fingerprint/rollback/upgrade checks, the replenishment scalar-null fix, reorder policies, inventory/admin reports, packing/queue, checkout, and fulfillments. It writes `artifacts/bootcamp/module-06d/stock-batches-and-reports.trx`. Results are pending. Local file links last passed across 68 Markdown documents; the subsequently added batch workshop will be included in the next documentation check.

## Operational module verified: 40 stories complete

**Observed result:** `artifacts/bootcamp/module-06d/stock-batches-and-reports.trx` reports 57 passed, zero failed/skipped, in 2 minutes 46 seconds. This verifies the corrected replenishment query, bulk stock receipts and public stock revision observation, canonical fingerprints, the explicitly coordinated independent-connection replay race, database-trigger rollback, migration upgrades, and the selected inventory/report/packing/queue/checkout/fulfillment regressions. MS-07 and MS-09 are now complete; all six stories in module 6 are verified. Total: 40 of 75.

**Query lesson confirmed:** making the left-joined returned-unit aggregate nullable and using scalar coalescing fixes EF translation while preserving database-side aggregation and the hand-calculated results. The empty cohort, boundary, returned-sales, and pagination tests all pass. The first 24-pass/3-fail run remains useful evidence of why a compiled LINQ expression still needs provider integration tests.

**Next account workflows:** MS-20 projects only real order/shipment/return timestamps into a bounded owned timeline. MS-21 matches historical variant IDs to the current catalog and creates a new cart only after full validation. MS-11 combines two cart states, retains target line IDs, and saves both revisions together. These source changes were added after the operational test application's compilation and are not covered by its 57 passing tests. The cart writer audit, account tests, and worked lessons remain in progress.

## Account timeline, repeat purchase, and cart merging underway

**Writer audit:** cart claiming now uses a domain method that advances the revision when ownership changes, while an already-owned repeat claim leaves it alone. Product deletion now starts its transaction before discovering affected wishlist/collection/cart parents, advances affected cart revisions, and commits parent changes with the catalog cascade. This closes a gap where membership could otherwise change without invalidating an observed cart version.

**Implementation:** CartResponse exposes Cart.Version. A pure combination helper defines active/saved overlap, quantities, all-line currency checks, and current active-stock validation. Cart.ReplaceContents preserves existing target line IDs and validates the full proposed shape before mutation. The timeline uses real timestamped sources, separate total counts, bounded prefixes, and a stable final merge. Reorder validates historical identity against current catalog records before persisting one new cart.

**Teaching and verification:** workshops 5b and 5c now cover historical evidence versus current shopping intent, bounded timeline merging, all four cart overlap cases, two-parent revisions, cascade audits, and retry differences. The running Debug test project selects the new timeline/reorder/merge/domain/concurrency tests plus cart, catalog, wishlist, and checkout regressions. Evidence destination: `artifacts/bootcamp/module-05b/account-cart-workflows.trx`. Results remain pending; no additional stories are marked complete yet.

## Owned history and repeat purchase verified; merge correction pending

**Observed result:** `artifacts/bootcamp/module-05b/account-cart-workflows.trx` reports 109 tests: 108 passed, one failed, zero skipped, in 5 minutes 56 seconds. The timeline, repeat purchase, writer-audit, stale-two-cart rollback, and selected existing regression tests passed. The only failure was the merge scenario introducing a new target line, which returned 409. MS-20 and MS-21 are complete; total 42 of 75. MS-11 remains in progress.

**Merge correction:** the service previously decided whether to add a new line by consulting EF's current entry state after relationship discovery. It now records original target line IDs before replacement and explicitly marks every newly created target line Added. This avoids relying on generated-key child discovery to distinguish insertion from update. The hypothesis is that the discovered child was treated as an update and produced a concurrency conflict; the initial test did not print its ProblemDetails body. The assertion now includes that body if the rerun fails. Verification of the correction remains pending.

**Next storage workflows:** saved searches persist a versioned, typed whitelist and reuse ProductReadQueries.Page with ordinary catalog search. Recent products require an explicit authenticated POST, serialize upsert/retention, and filter active products before the visible limit. Review reports have a separate terminal-resolution lifecycle and never mutate the source review. The BootcampAccounts API build passed with zero warnings/errors; its migration is being generated. HTTP, independent-connection barrier, and upgrade tests are written; their results and new workshops are pending. The last documentation check passed local links in 71 Markdown documents.

**Merge SQL evidence:** inspection of the failed run found `UPDATE CartItems SET CartId, IsSavedForLater, ProductVariantId, Quantity ... WHERE Id ... RETURNING 1` immediately before the failing scenario's application shutdown. That full-field update is consistent with the newly discovered child being treated as an existing generated-key entity. Explicit original-target identity tracking is now the insertion decision; the next run verifies the outcome.

**Storage migration and next run:** `20260908211353_CustomerCatalogWorkflows` adds only SavedCatalogSearches, RecentlyViewedProducts, and ReviewReports, with the intended unique/index/cascade rules. It does not change existing catalog/review rows. Workshops 5d and 5e plus API contracts now explain the new behavior. The BootcampAccounts test-project run writes `artifacts/bootcamp/module-05d/customer-catalog-workflows.trx` and includes new API/domain/independent-connection/upgrade checks, the merge insertion fix, timelines/reorder, and catalog/cart/review/wishlist regressions. Results are pending.

## Account storage and the merge correction verified

**Observed result:** the BootcampAccounts run finished with **160 passed, zero failed, zero skipped**, in 2 minutes 41 seconds. Evidence: `artifacts/bootcamp/module-05d/customer-catalog-workflows.trx`. The merge case with a new target child now succeeds. Saved-search capacity races, concurrent explicit views, duplicate review reports, stale resolution, customer-catalog upgrades, and the selected cart/catalog/account regressions passed. MS-11, MS-13, MS-16, and MS-18 are complete: **46 of 75 stories**.

**Next teaching step:** templates separate a durable shopping intention from current catalog authority. Their implementation reuses the pure combination rules and extracts the now-verified explicit child insertion operation into CartCombinationWriter. The BootcampAccounts binary predates that extraction; the template verification run must cover merging again. Template source, HTTP scenarios, and a separate build are underway. They are not included in the 160-test result above.

## Full account checkpoint and the next pricing boundary

**Full regression:** the already compiled BootcampAccounts snapshot passed **572 tests, zero failed, zero skipped**, in 7 minutes 22 seconds. Evidence: `artifacts/bootcamp/module-05d/account-full-regression.trx`. This snapshot includes the 46 completed stories and predates cart templates and shared checkout pricing.

**Templates:** API build passed with zero warnings/errors. Migration `20260908212656_CartTemplates` creates only the template/line tables, unique membership, owner cascade, and historical variant IDs without a variant FK. Workshop 5f and API documentation explain live-price application and the two races. The first test-project build caught a fixture using an unavailable parameterless InventoryItem constructor. It was corrected to `new InventoryItem(variant.Id, 100)`; that failed build ran no tests. Template verification is included in the next combined run, along with merge tests covering the shared writer extraction.

**Pricing extraction:** CheckoutPricingService now owns loads, selection validation, observed stock checks, and totals. Quote uses no-tracking reads; checkout consumes the same calculation before its existing reservation/payment sequence. Characterization constraints come from the existing totals, tax/gift, shipping, and reservation tests. The old weight calculation used Int32 multiplication/summing; valid 30 × 99 × 1,000,000 gram input exceeded that range. Weight now widens before multiplication and remains long through shipping calculation, with a concrete boundary test.

**Preferences and schedules:** saved preferences use owner-keyed rows, nullable address SET NULL, create-only/null versus exact-revision updates, explicit opt-in, independent address/method precedence, and use-time revalidation. Discount schedules add nullable StartsAt and preserve the other redeemability guards. Both workflows share the captured pricing clock. The BootcampPricing API build passed with zero warnings/errors before a small explicit anonymous-opt-in 401 guard was added. Migration `20260908213639_CheckoutPreferencesAndDiscountSchedules` was generated against the matching model; upgrade verification is pending.

**Current verification:** the BootcampPricing test run writes `artifacts/bootcamp/module-07a/quotes-preferences-schedules-templates.trx`. It selects quote/preference/schedule tests, their upgrade/concurrency checks, templates and merge regressions, and existing checkout/discount/tax/gift/shipping/address/reservation tests. Workshops 7a/7b explain calculations, no-side-effect evidence, input precedence, and exact time boundaries. No stories in this batch are marked complete until results arrive. The last completed docs check covered 74 Markdown documents, before these two pricing workshops were added.

## Pricing verified; template provider translation corrected

**Observed result:** the combined run completed **139 tests: 133 passed, six failed, zero skipped**, in 1 minute 54 seconds. All quote, checkout-preference, discount-schedule, migration/revision checks for those features, and selected existing pricing regressions passed. MS-10, MS-19, and MS-28 are complete, bringing the tracker to **49 of 75**. This is not a wholly green run: all six failures belong to templates.

**Actual failure and correction:** the template creation query's limited nested Include translated to SQL APPLY, unsupported by SQLite. That prevented creation in three API and three persistence scenarios. The service now selects the owned cart ID and queries bounded active CartItems directly into snapshots. Its cap check remains in the same write transaction. The correction needs rerunning; MS-12 remains in progress. The docs checker passed local links across 76 Markdown files.

**Next source boundary:** after the pricing API compilation, return eligibility, evidence links, manual shipment tracking, and internal support notes began. Return creation now shares policy/quantity/refund estimation with preview; new local collection writes are separate from refund execution. These changes are not represented by the pricing test result. Operational-history migration, test evidence, and teaching chapters remain outstanding.

## Operational-history implementation and teaching coverage

**Build and schema:** BootcampHistory API built with zero warnings/errors. Migration `20260908214457_OperationalHistory` adds evidence, support notes, tracking events, and Unknown/zero tracking fields on existing fulfillments. It invents no past events. ReturnPolicy is nullable configuration validated at startup, not schema. The generated migration awaits the written upgrade/cascade checks.

**Shared return calculation:** preview and creation now use one policy/remaining-quantity/refund estimator. Creation serializes current capacity and insertion without a gateway call in that transaction. Existing approval remains independent of the new-submission deadline. Tests cover 5 purchased minus 1 requested minus 2 approved = 2 remaining, 38.88 estimated refund under the fixture's order-effective rates, exact deadline boundaries, disabled policy, missing timestamps, and invalid startup configuration.

**Separate histories:** evidence uses account ownership through the order and a serialized five-link cap; it is allowed after approval and cannot implicitly refund. Tracking has a complete transition table, required observed revision, and one event per saved revision. Support notes reject Pending orders, derive authorship/time on the server, retain historical actor IDs, and stay out of other response/payload paths. Workshops 7c and 7d explain these distinctions through arithmetic, analogies, request traces, transition tables, race timelines, and questions with answers.

**Running evidence:** BootcampHistory tests write `artifacts/bootcamp/module-07c/operational-history-and-templates.trx`. The selection includes new HTTP/domain/independent-connection/upgrade checks, the corrected SQLite template query, merge regressions, and existing return/fulfillment/pricing suites. Results are pending; no additional completion claims yet.

## Templates and operational history verified

**Observed result:** BootcampHistory completed **313 passed, zero failed, zero skipped**, in 5 minutes 11 seconds. Evidence: `artifacts/bootcamp/module-07c/operational-history-and-templates.trx`. The corrected template projection executed successfully on SQLite, including capacity/application races and upgrades. Return policy/startup/remaining-capacity tests, evidence cap/isolation tests, all tracking transition pairs and its writer race, support-note privacy/attribution, and operational-history upgrades passed. MS-12 and MS-23 through MS-26 are complete: **54 of 75 stories**.

**Last midlevel story underway:** the gift-card writer audit found issuance, checkout redemption, order cancellation/full-refund credit, and RMA credit. GiftCardAccounting stages balance and entry changes without independently saving or calling providers. The ledger uses card Version, signed whole-cent entries, safe source IDs, and a history-preserving card relationship. Its API build passed with zero warnings/errors. The ordinary card DTO gains a non-secret ID so administrators can navigate to the ID-based ledger report. New HTTP, forced-SQL-failure, competing-redemption, and migration-backfill tests plus workshop 7e are written; the migration generation/backfill and verification remain in progress. These ledger changes are newer than the 313-test binary.

## Ledger backfill and the first senior graph feature

**Ledger migration:** `20260908215834_GiftCardLedger` creates the entry table and unique card/version index, restricts card deletion, and inserts OpeningBalance rows from existing stored Balance/Version values. SQL copies cents directly. Recording time is evaluated by SQLite when the migration is applied, using Unix seconds converted to UTC ticks; no past activity is fabricated. Each opening row reuses its card GUID as an ID in the separate entry table. The full BootcampLedger regression is running with evidence destination `artifacts/bootcamp/module-07e/midlevel-full-regression.trx`.

**Category-tree work:** after the ledger API snapshot compiled, SS-02 began. CategoryTreeRules uses iterative memoized parent walks, visited sets, a 5,000-node cap, and root-depth-one/max-depth-ten validation. CategoryTreeService serializes the global revision and proposed topology with every category create/update/delete/move. Read-only integrity diagnostics preserve legacy data, and breadcrumbs reject invalid requested paths. The writer audit includes the original CategoriesController endpoints, so a new move rule cannot be bypassed through old PUT.

**Teaching and tests:** workshop 8a explains the two-root write-skew example, subtree depth, shared revisions, and explicit legacy remediation. Pure/API/persistence tests cover loops, missing parents, 5,000/5,001 nodes, stale global writes, incompatible competing moves, legacy migration, and the old update route. Build and migration verification remain underway. The last completed documentation link check covered 79 files before workshop 8a was added. No senior story is yet marked complete.

## Midlevel completion checkpoint: 641 passing tests

The complete BootcampLedger regression passed 641 tests with zero failures or skips in 13 minutes 31 seconds. Evidence: artifacts/bootcamp/module-07e/midlevel-full-regression.trx. This includes all 25 junior and all 30 midlevel stories, including gift-card opening-balance migration, atomic ledger writes, rollback, and competing redemptions. The category-tree and option-schema work was outside this compiled test snapshot and remains unverified until its dedicated checks run.

Learning checkpoint: explain why an old card with an initial value of 100 and a current balance of 35 receives an opening entry of 35. Then explain why an atomic local refund entry does not prove exactly-once behavior at a remote payment gateway. See Workshop 7e for worked answers.

## Senior integration: proposals, policies, stock, and access

The user requested GPT-5.6 Sol subagents. Three bounded work streams added schema/fulfillment policy validation, warehouse documents, and login/guest access, while the coordinator implemented import staging, quantity pricing, shared wiring, migrations, and teaching integration. Implementation status remains separate from verification: the verified baseline is 55 stories and 641 passing tests.

A schema migration review caught duplicate creation of CategoryTreeStates. The schema build contained an older compiled model snapshot even though the preceding tree migration already existed on disk. The duplicate Up/Down operations were removed; the target designer/snapshot still correctly contains both tables. This is a concrete reason to review generated migrations and run an actual sequential upgrade.

The first combined senior API build failed with two compiler errors and one warning: a DeliveryCalendarController helper named Response hid ControllerBase.Response. Renaming the helper to ToResponse resolved that source defect. This was a build failure, not a failing behavioral test. No senior story was marked complete from it.

The quantity-pricing audit found every runtime CartResponse.From call and moved it through one batch-loading response factory. Cart currency now follows active lines, and mixed-currency activation is rejected before persistence. The import audit added an explicit streaming request-size bound so TestServer/chunked requests also reject bodies larger than 1 MiB. The guest-access audit found safe new reads were insufficient while old cancellation and owned-order-list projections could still expose gift-card/payment identifiers; those projections are being tightened before the next test run.
## First senior behavioral checkpoint: 141 passed, 13 failed

The BootcampSeniorBatch focused run completed 154 tests in 4 minutes 22 seconds: 141 passed, 13 failed, zero skipped. Artifact: artifacts/bootcamp/module-08/senior-policy-integration.trx. Import (7), category tree (8), option schemas (19), quantity pricing (13), warehouse API/persistence (10), login sessions (10), and guest-specific access tests (7) passed, alongside catalog and totals regressions.

Five failures were shipping/calendar API requests returning 500. Their positional record parameters combined JsonRequired and Range in a property-targeted attribute list. ASP.NET Core record validation expects Range on the constructor parameter; moving it to the property is rejected at runtime. Splitting property-targeted JsonRequired from parameter-targeted Range fixes the source. This demonstrates why successful compilation cannot replace an HTTP integration test.

The remaining eight failures were old guest-return/refund fixtures using order number/email or anonymous financial routes. The new access contract intentionally rejects those requests. Positive guest-return fixtures now retain the checkout-only credential; administrative cancellation/refund fixtures authenticate an admin. Negative no-credential and foreign-resource assertions remain required. These edits still need a rerun; no green result is claimed for them yet.

The compiled test snapshot predates operational holds, assignment enforcement, integration-key wiring, private export, and outbox changes. Those ongoing features were not verified by these 141 passing tests.
The supplemental compiled-snapshot warehouse domain run passed all six tests in 450 ms (module-08/warehouse-domain.trx). With API/persistence coverage already passing, SS-01/02/03/04/07/08/11 are verified: 62 of 75 stories complete. The 13 failing checks described above and later integrations remain outstanding; a final full regression is still required.

### Durable-work build integration

The first combined durable-work build found missing exception/service namespace imports, then exposed a missing hosting abstraction dependency in Infrastructure. Background workers use `BackgroundService` and host lifetime interfaces; an Entity Framework reference does not provide that dependency implicitly. Added the explicit matching `Microsoft.Extensions.Hosting.Abstractions` package and restored the projects. These were compile failures, not passing runtime checks.

A worker review also moved lease timestamps after transaction acquisition: waiting for a database lock can consume the remaining lease, so a timestamp taken before that wait is not fresh enough to authorize publication. The report-export review found a cleanup starvation case: repeatedly selecting the oldest expired job metadata would revisit already-removed blobs forever. Cleanup now selects existing artifacts joined to expired jobs. Both examples belong in the bootcamp because the apparently small query/clock placement changes the actual guarantee.

### The final additive schema

Generated `20260908231245_DurableWorkAndCatalogSynchronization` after the combined API build passed with zero warnings/errors. Inspected the catalog sequence's SQLite AUTOINCREMENT annotation, revision-zero default, singleton watermark seed, integration-key digest constraint, active-hold uniqueness, assignment generation identity, order-history composite index, outbox attempt uniqueness, and report artifact relationship.

Added explicit legacy webhook backfill: freeze the current subscription URL, set the visible attempt-history floor to old AttemptCount + 1, and make remaining Pending/Failed deliveries due. Preserve original payload/signature/status/count; keep EventId null and create no fictional event/attempt rows. Stop old workers before applying the migration. Full runtime and upgrade verification is still pending at this checkpoint.

The upgrade-fixture audit also corrected current-model inserts into downgraded product tables. Tests now seed current data, downgrade to the exact predecessor, and upgrade again, so adding CatalogRevision does not accidentally turn an unrelated migration test into an invalid-column insert.

### Test-project integration checkpoint

The complete test build exposed missing namespace imports in a new webhook test, followed by three missing cancellation-token arguments and a constructor fixture that had not supplied the new catalog mutation service. Corrected the test wiring without weakening assertions. A misplaced import in my first correction caused another compiler error; moved it before the namespace and verified its location.

The catalog bootstrap review tightened input accumulation as well as output serialization: per-product 256 KiB checks alone still permit too many individually valid snapshots to accumulate. Track the cumulative bootstrap budget and stop before retaining an oversized response. Two existing nullable test warnings now use explicit non-null assertions, which both document the expected response and give the compiler the same information.

Documentation verification currently checks local file links in 106 Markdown documents successfully. This does not validate anchors or prove feature behavior; runtime checks remain separate.

### Full regression execution begins

The complete test assembly compiled and the full suite began. EF's `has-pending-model-changes` check passed: the generated migration, manually completed feed-state revision metadata, and current runtime model agree.

Early failures include older order-state tests making now-unauthorized guest financial requests and a legacy webhook test expecting immediate delivery. Those fixtures must exercise the new access and worker contracts while retaining their original state/stock assertions. Another fixture bug consumed the same HTTP response stream twice to obtain checkout credentials and order fields; the fix buffers one response or maps the already-deserialized value. These are observed failures, not a passing regression claim.

### First all-story regression: 760 passed, 49 failed, zero skipped

Ran the full test project in `BootcampDurable`; 809 tests executed in 3m36s. Evidence: `artifacts/bootcamp/final/all-stories-regression.trx`. The build completed; this is not a green regression result.

The failure audit grouped all 49 failures into explicit follow-up work: old guest financial/read fixtures, repeated response-stream consumption, inline-webhook expectations, the calendar duplicate-date status, CSV/header formatting assumptions, the new authentication query in an existing timeline budget, and an oversized legacy-product fixture whose preassigned variant IDs were not explicitly added.

The passing groups include 15 warehouse coordination tests (API/domain/persistence), scoped-key authorization and migration checks, cursor traversal/protection/index/upgrade checks, durable outbox claim/rollback/late-ack/replay/backfill checks, shipping rules, account-export record/byte/snapshot bounds, and most report-export lifecycle/limits/ownership checks. Additional catalog synchronization acceptance tests were written after this compiled snapshot and still require execution.

The timeline query-budget fix explicitly accounts for one login-session lookup while preserving the original bounded timeline-query budget. The quote test now proves zero inline webhook sends plus two committed Pending deliveries after checkout. It continues to prove repeated quotes create no database writes and call no providers.

### Why a build needs a stable source snapshot

The second regression still reports unauthorized cancellation in tests whose current source already uses an admin client. Those five edits were made after starting the build, while its buffered output still showed the API step. Compiler input may already have been captured by then. Treat them as unverified until the next compilation; do not change a correct authorization rule to satisfy an older binary.

The practical rule is to freeze source before starting a build, then distinguish the running binary from files edited afterward. Console output timing is not a reliable boundary for sneaking in a final edit. The next verification will compile the fully frozen source again.

### Second all-story regression: 812 passed, 6 failed, zero skipped

The expanded suite executed 818 tests in 2m59s. Evidence: `artifacts/bootcamp/final/all-stories-regression-2.trx`. All access/export, shipping/calendar, durable webhook, and previous fixture corrections passed. Five cancellation cases still used the earlier compiled test snapshot; their current source selects an admin client and needs recompilation. The remaining failure is the new catalog bootstrap/concurrent-update test timing out while waiting for its SQL interception barrier.

The final catalog boundary review also removes conservative reserved-overhead rejection: an API advertising a byte cap should account for the actual serialized wrapper and row overhead. Keep the bounded stream as a backstop, but calculate the fitting prefix using real byte sizes rather than rejecting a valid response several hundred bytes early. Exact-limit and one-byte-over tests accompany that correction.

### Final all-story regression: 820 passed, zero failures, zero skipped

The frozen source compiled without warnings or errors, then the complete `BootcampDurable` test project passed all 820 tests in 2m57s. Evidence: `artifacts/bootcamp/final/all-stories-regression-3.trx`. All 75 stories are now complete in the [live tracker](story-tracker.md): 25 junior, 30 midlevel, and 20 mid/senior.

The catalog concurrency test now signals that the writer has started before it waits to acquire its transaction. Previously the test paused the reader, then waited for a signal that could only occur after that reader released its database lock. That was a circular wait in the test harness. Releasing the barrier in `finally` also prevents a failed assertion from leaving background work stuck. The passing test verifies the consistent bootstrap checkpoint under an overlapping write.

The exact-boundary tests accept a 5 MiB bootstrap and reject one byte over. A change page can fill its 1 MiB budget exactly; it selects metadata first and retrieves the fitting payloads in one batch. The query assertion counts three reader commands: state, metadata, and payload batch. This connects a public response-size promise to both memory use and database cost.

The EF pending-model check passed before this final run; no subsequent schema changes were made. The learning material contains 54 bootcamp Markdown documents: workshops, API references, curriculum, journal, tracker, and practice material. Follow [practice and explain-back](11-practice-and-explain-back.md) to turn the implementation into small repeated exercises. Passing tests are evidence for the checked behavior; the existing development payment and webhook transports remain development implementations.

Final documentation verification passed: local file targets checked in 106 Markdown documents (anchors are not checked). Git's whitespace check also passed; its messages only report Windows line-ending normalization.
