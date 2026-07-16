# Checklist: final performance verification

This checklist is the final performance gate before accepting GarageBalance on a local PC or VPS. It combines automated guards that can run in development with manual checks that require PostgreSQL and realistic cooperative data.

## 1. Automated gates

- [ ] Run backend tests, including `BackendPerformanceGuardTests`:

```powershell
dotnet test GarageBalance.slnx --no-restore --configuration Debug
```

- [ ] Run frontend tests:

```powershell
npm run test -- --reporter=dot --maxWorkers=1 --testTimeout=180000
```

- [ ] Build frontend and enforce bundle budget:

```powershell
npm run build
npm run check:bundle
```

- [ ] Confirm current bundle budget remains under:
  - main JS gzip: `180 KiB`;
  - main CSS gzip: `40 KiB`;
  - total JS/CSS gzip: `260 KiB`.
- [ ] Run `npm run lint`, `npm exec tsc -- --noEmit`, `dotnet format GarageBalance.slnx --verify-no-changes --no-restore`.
- [ ] Generate idempotent EF SQL and keep the artifact for deployment review:

```powershell
dotnet ef migrations script `
  --project backend/GarageBalance.Api/GarageBalance.Api.csproj `
  --startup-project backend/GarageBalance.Api/GarageBalance.Api.csproj `
  --context GarageBalanceDbContext `
  --idempotent `
  --no-build `
  --output artifacts\deploy-migrations.sql
```

## 2. PostgreSQL and data prerequisites

- [ ] Local or VPS PostgreSQL is available.
- [ ] `psql` is available.
- [ ] A recent backup exists before the test.
- [ ] Test data is realistic enough for the customer cooperative:
  - owners and garages match the real order of magnitude;
  - payments and accruals cover several months;
  - meter readings cover repeated monthly input;
  - audit history contains enough rows to test filters and pagination;
  - import run logs and quarantine rows exist if Access import is being accepted.
- [ ] No real passport data, full bank details, raw Access file, `.pgdump`, `.env`, or private import folder is committed to Git.

## 3. Backend query checks

Run the heaviest screens on PostgreSQL and check that they do not materialize full tables in the application:

- [ ] dictionary search by garage number, owner name, phone, supplier name, INN and contact person;
- [ ] users list and audit history with filters;
- [ ] payments lists and paged finance history;
- [ ] Access import history, run log and quarantine;
- [ ] consolidated report;
- [ ] income report;
- [ ] expense report;
- [ ] cash and bank reports;
- [ ] fee report and fund movement report.

For PostgreSQL review:

- [ ] use `EXPLAIN (ANALYZE, BUFFERS)` for the slowest report/search queries if response is visibly slow;
- [ ] confirm GIN trigram indexes are available for contains-search fields;
- [ ] confirm period, date, month, garage, supplier and audit filters use indexed columns where practical;
- [ ] confirm visible table endpoints use `limit`, `rowCount`, `CountAsync`, `SumAsync`, pagination, or PostgreSQL aggregation before materialization.

## 4. Frontend checks

- [ ] Open the app in a browser after production build or deployed frontend.
- [ ] Check browser console for errors while opening dashboard, dictionaries, payments, reports, import, audit and "Что нового".
- [ ] Check that long tables remain usable and do not freeze the page.
- [ ] Check search inputs use debounce and do not fire requests on every render.
- [ ] Check report export buttons remain responsive and show errors without duplicate success statuses.
- [ ] Check the app on the target monitor size and a narrow laptop width.

## 5. Acceptance thresholds

Record actual numbers for the target environment:

- [ ] dashboard opens without visible delay after login;
- [ ] common dictionary search responds without visible lag;
- [ ] payments and audit lists open with bounded row counts;
- [ ] reports for several months return without full-table scans in application memory;
- [ ] import dry-run/report UI shows progress and remains responsive;
- [ ] frontend bundle remains inside the configured budget or a written exception is added to roadmap history.

## Current local limitation

The current development machine has `psql` and Docker tooling, but no configured local PostgreSQL instance or realistic customer dataset was started for this run. Local acceptance therefore remains limited to automated guards, build checks, bundle budget, SQL generation, JSON validation, encoding checks and Docker Compose configuration. Task-specific live database verification was performed on the authorized VPS as recorded below.

## Verification run: 2026-07-14

- [x] VPS PostgreSQL and `garagebalance-staging.service` were inspected before data cleanup. Database commands observed in the application journal normally completed in 0-12 ms; the slowest sampled commands completed in 52-77 ms. No warning-level service entries were present for the inspected day.
- [x] Repeated dictionary requests are deduplicated for the authenticated session and invalidated after mutations or logout. Contractor tabs load only when opened, reports no longer build inactive fee reports during initial load, and the payment screen becomes available after its critical garage request instead of waiting for every form reference.
- [x] All working tables use the shared centered progress indicator while loading. Empty states remain hidden until loading finishes, and the indicator exposes a polite accessible status and honors reduced-motion preferences.
- [x] A verified custom-format backup was created before cleanup: `/opt/garagebalance-staging/backups/garagebalance_before_manual_entry_20260714_141629.pgdump` (435078 bytes); `pg_restore --list` completed successfully.
- [x] Operational data was removed transactionally for manual re-entry. Verification after `VACUUM ANALYZE` showed zero garages, owners, staff, suppliers, payments, accruals, meter readings, funds, import rows, audit rows, form states and integration settings. Nine tariff records and the technical authentication, role, migration and release catalogs were retained.
- [x] Post-cleanup service checks passed: systemd reported `active`, local and public `/health` returned HTTP 200, and the service journal contained no warning-level entries after restart.
- [x] Automated acceptance passed: 1498 backend tests, 417 frontend tests, frontend lint, backend format verification, privacy scan, Docker Compose configuration, production build and bundle budget. Final gzip sizes: JS 178.0 KiB, CSS 17.0 KiB, total 195.0 KiB.

The local PostgreSQL limitation above still applies to this workstation, but the task-specific live database and service checks were completed against the authorized VPS environment.

## Repeated optimization and stability run: 2026-07-14

- [x] Rechecked automatic regular accrual generation for realistic growth: existing accruals and monthly meter readings are now loaded in batches instead of one query per garage. An integration-style performance test creates 200 garages and 200 readings, verifies all 200 accruals and a duplicate-safe repeat, and keeps each run within five `SELECT` commands.
- [x] Split the twelve large workspace sections into on-demand JavaScript chunks. Main navigation tiles preload their likely section on focus or pointer hover, while the shared centered table loader remains visible during the first open and a section error boundary prevents a blank screen after a chunk/network failure.
- [x] Repeated automated checks passed: 1500 backend tests and 419 frontend tests, including loader, lazy import, recovery-state and constant-query-count coverage. Frontend ESLint, TypeScript, production build, bundle budget, backend format verification, Docker Compose configuration, release JSON, strict UTF-8/no BOM, `git diff --check` and idempotent EF migration SQL generation also passed.
- [x] Production bundle measurement after splitting: main JS `74.1 KiB` gzip (previous verification `178.0 KiB`), main CSS `17.0 KiB`, total JS/CSS `212.4 KiB`; all configured budgets remain green.
- [x] VPS state was rechecked after the optimizations: `garagebalance-staging.service` is active, nginx configuration is valid, public `/health` returns HTTP 200, and no warning-level service journal entries appeared in the inspected period. PostgreSQL still contains nine tariffs and zero operational rows for garages, owners, staff, suppliers, payments, accruals, meters, funds, import and audit data. The pre-cleanup backup still exists and its `pg_restore --list` catalog is readable.

No production schema changes were introduced by this repeated pass. Local PostgreSQL remains unavailable, so live data and service verification used the authorized VPS; branch code will reach the service only through the configured deployment workflow after merge to its deployment branch.

## Third stability and query-load audit: 2026-07-14

- [x] Re-audited the requested tariffs, garages, contractors, meters, payments, reports and funds sections for unbounded production queries, unnecessary initial requests, loading states and deferred tab behavior. PostgreSQL list paths remain server-bounded; full materialization found in the reviewed repositories is restricted to explicit SQLite test fallbacks or bounded business periods.
- [x] Found and removed a remaining payment-workbench fan-out: opening, filtering or paging one visible finance table previously requested all five paged tables. The frontend now requests only the active table, obtains all tab counts from the compact summary, and loads missing-meter diagnostics only on the meter tab.
- [x] Collapsed finance summary calculation from six sequential aggregate commands to four commands: financial income/expense totals and counts share one grouped query, garage accrual total/count share one grouped query, and supplier-accrual and meter counts remain separate bounded aggregate queries.
- [x] Added regression coverage for the four-command summary, exact income/expense/supplier section counts, active-table-only loading, and deferred missing-meter diagnostics.
- [x] Final repeated verification passed after unifying the table skeleton loader: 1504 backend tests and 420 frontend tests, ESLint, TypeScript, production build and bundle budget (`74.1 KiB` main JS, `16.9 KiB` CSS, `212.3 KiB` total gzip). Static guards confirm that large tables keep the shared pagination, localized date controls, context menus, semantic creation actions and skeleton loading states without bare loading paragraphs. VPS service/nginx/public health, warning journal, backup catalog and the retained-nine-tariffs/zero-operational-rows database state were checked again.

This pass changes neither the database schema nor the cleanup policy. The optimized branch is not deployed by a feature-branch push because the staging workflow is intentionally triggered only from `master`; the currently deployed service was verified independently and remains healthy.

## Fourth stability and automation audit: 2026-07-14

- [x] Rechecked tariffs, garages, contractors, meters, payments, reports, funds and administration for bounded PostgreSQL queries, shared pagination, deferred hidden sections, debounced search and consistent skeleton loading. The 137 focused architecture, migration and performance guards and the 55 focused UI pattern tests passed.
- [x] Optimized the idempotent regular-accrual path: when every active garage already has the monthly accrual, two aggregate checks now stop the run before materializing garages, owners or per-garage skip messages. A partially completed month still creates rows for garages added after the first run.
- [x] Added a five-minute technical retry after a transient automation exception while preserving the normal six-hour interval after a successful run. This avoids delaying monthly accruals for six hours when PostgreSQL is briefly unavailable during startup.
- [x] Repeated the complete verification twice with identical results: 1506 backend tests and 420 frontend tests passed, together with ESLint, TypeScript, production build, backend formatting, Docker Compose validation, release JSON, strict UTF-8/no BOM, idempotent EF migration SQL and the bundle budget (`74.1 KiB` main JS, `16.9 KiB` CSS, `212.3 KiB` total gzip).
- [x] Rechecked the authorized VPS: `garagebalance-staging.service` is active with zero restarts, nginx is valid, public health and frontend return HTTP 200, the warning journal is empty, the verified backup remains readable, and PostgreSQL retains nine tariffs with zero operational rows in garages, owners, staff, suppliers, payments, accruals, meters, funds, import and audit tables.

This audit introduces no schema or cleanup-policy changes. Feature-branch publication does not deploy staging because the deployment workflow remains intentionally restricted to `master`.

## Fifth concurrency and loading-state audit: 2026-07-14

- [x] Rechecked concurrent financial and dictionary writes. PostgreSQL unique-constraint races are now returned as a safe HTTP 409 conflict instead of an internal HTTP 500 error; unrelated database failures remain HTTP 500 and do not expose database details.
- [x] Rechecked overlapping payment table requests. A delayed response from an older tab, page or filter can no longer replace the latest result or stop its loader; reference-data loading and active-table loading now have independent state.
- [x] The active financial table keeps the shared accessible table skeleton while its latest request is pending. Previous rows and empty-state text are hidden until loading completes, and conflicting pagination actions remain disabled.
- [x] Added regression tests for PostgreSQL unique-write conflicts, non-unique database failures, reversed finance response order and loader lifetime. Complete verification passed twice with identical results: 1508 backend tests and 421 frontend tests.
- [x] ESLint, production TypeScript build, bundle budget (`74.1 KiB` main JS, `16.9 KiB` CSS, `212.5 KiB` total gzip), backend formatting, Docker Compose validation with process-local placeholder secrets, release JSON, whitespace checks and 114893-byte idempotent EF migration SQL generation passed.
- [x] Rechecked the authorized VPS without changing it: `garagebalance-staging.service` is active with zero restarts, nginx is valid, direct and public health and the frontend return HTTP 200, the warning journal is empty, and the verified 435078-byte backup remains readable. PostgreSQL retains nine tariffs and zero operational rows in garages, owners, staff, staff departments, suppliers, supplier groups and contacts, payments, accruals, meters, funds, import and audit tables.

No database schema or cleanup-policy changes were required. This feature branch remains intentionally undeployed until it reaches the deployment branch through the configured workflow.

## Sixth background-load and monthly-latency audit: 2026-07-14

- [x] Separated the paged finance workbench result from the four compact recent-item previews. A delayed preview response can no longer replace a filtered page while leaving unrelated pagination and totals on screen; switching a tab invalidates the previous request before the next effect runs.
- [x] Reduced each delayed payment preview from 50 rows to the eight rows that are actually rendered. Exact totals still come from the compact server summary, reducing the maximum preview payload from 200 to 32 records without losing counters.
- [x] Kept compact previews current immediately after create, edit, cancel, restore and generation flows while the main table continues to render only its protected paged result. The first complete frontend run exposed eight preview regressions; all were corrected and rerun before acceptance.
- [x] Reduced the successful regular-accrual automation interval from six hours to 15 minutes. A garage or regular service added during the current month is now picked up within the next quarter-hour; the five-minute technical-failure retry and duplicate-safe generation remain unchanged.
- [x] Two clean complete verification runs passed after the corrections with identical results: 1508 backend tests and 421 frontend tests. ESLint, production TypeScript build, bundle budget (`74.1 KiB` main JS, `16.9 KiB` CSS, `212.6 KiB` total gzip), backend formatting, Docker Compose, release/configuration JSON, whitespace checks and 114893-byte idempotent EF migration SQL generation also passed.
- [x] Rechecked the authorized VPS without changing or deploying it: service active with zero restarts, nginx valid, direct/public health and frontend HTTP 200, zero warning-level journal lines, readable 435078-byte backup, nine tariffs and zero rows in all checked operational, staff, supplier, finance, fund, import and audit tables.

No schema or cleanup-policy changes were introduced. The feature branch remains undeployed because the staging workflow is restricted to `master`.

## Seventh deferred-table and partial-failure audit: 2026-07-14

- [x] Rechecked every delayed payment preview against the shared loading-state rules. The four recent-item tables now show accessible table skeletons from the moment the overview is enabled until all background requests settle; empty states are suppressed throughout that interval.
- [x] Added a safe visible error state for preview failures without blocking the paged finance workbench or its pagination. A failed preview no longer appears as a genuinely empty list.
- [x] Replaced all-or-nothing preview loading with independent settled results. If one endpoint fails, the other successfully loaded operation, accrual, supplier-accrual and meter previews remain visible.
- [x] Added workflow coverage for delayed preview completion, loader lifetime, hidden empty states, continued main-table availability and partial preview failure. Two complete clean runs passed with identical results: 1508 backend tests and 422 frontend tests.
- [x] ESLint, production TypeScript build, bundle budget (`74.1 KiB` main JS, `16.9 KiB` CSS, `212.9 KiB` total gzip), backend formatting, Docker Compose, release/configuration JSON, whitespace checks and 114893-byte idempotent migration SQL generation passed.
- [x] The authorized VPS was checked read-only again: service active with zero restarts, nginx valid, direct/public health and frontend HTTP 200, zero warning-level journal lines, readable 435078-byte backup, nine tariffs and zero checked operational, staff, supplier, finance, fund, import and audit rows.

No schema, data-cleanup policy or deployment configuration changed. The feature branch remains undeployed until it reaches `master` through the configured workflow.

## Eighth independent-preview completion audit: 2026-07-14

- [x] Rechecked every original performance, loading, data-cleanup and monthly-accrual requirement. Backend list paths remain bounded and protected by the performance guards; table loads use shared skeleton states; automatic regular accrual generation remains duplicate-safe and checks the current month every 15 minutes with a five-minute retry after technical failure.
- [x] Removed the last avoidable wait between the four compact payment previews. Operations, garage accruals, supplier accruals and meter readings now complete independently, so a slow or stalled endpoint keeps only its own table loading and no longer delays already available neighboring tables.
- [x] Extended workflow coverage to hold the operations preview pending while proving that a ready accrual preview is rendered and its own loader is removed. Partial preview failures continue to preserve the main paged payment table and every successful compact preview.
- [x] Two clean complete verification runs passed with identical results: 1508 backend tests and 422 frontend tests. The focused performance, security and regular-accrual subset passed 48 tests; the new delayed-preview regression passed together with the existing partial-failure scenario.
- [x] ESLint, production TypeScript build, bundle budget (`74.1 KiB` main JS, `16.9 KiB` CSS, `212.9 KiB` total gzip), backend formatting, Docker Compose, strict release/configuration JSON, whitespace checks and 114893-byte idempotent migration SQL generation passed.
- [x] Rechecked the authorized VPS read-only: the service is active with zero restarts, nginx is valid, proxied/direct health and the frontend return HTTP 200, the warning journal is empty, and the verified 435078-byte backup remains readable. PostgreSQL retains nine tariffs and zero rows in every checked garage, owner, staff, supplier, payment, accrual, meter, fund, import, form-state, integration-setting and audit table.

This audit introduces no schema, cleanup-policy or deployment-configuration changes. The feature branch remains undeployed because the staging workflow intentionally deploys only from `master`.

## Ninth searchable-workload and pagination audit: 2026-07-14

- [x] Rechecked the original tariff, garage, contractor, meter, payment, report, fund, database-cleanup and monthly-accrual requirements. Existing server pagination, bounded queries, deferred sections, shared skeleton states and duplicate-safe 15-minute regular-accrual automation remain intact.
- [x] Removed per-keystroke report requests. Garage, payout, income and fee searches now apply once after 350 ms of idle input and reset their related server/client pagination only when the user actually changes a filter.
- [x] Applied the same coordinated debounce to the large audit journal. Search, user, garage, counterparty and document text fields now produce one combined request instead of a request for every character, while select and date filters remain immediate.
- [x] The first full frontend run exposed a timing race in the new debounce initialization: an initial no-op timer could reset a page selected immediately after opening a report. The run was rejected, the initial timer was removed, and the pre-existing quick-pagination scenario was added to the focused regression group before complete acceptance restarted.
- [x] Two subsequent clean complete verification runs passed with identical results: 1508 backend tests and 424 frontend tests. Focused workflows prove final-only report filters, one combined audit query, offset reset after a real edit, and no initial pagination reset.
- [x] ESLint, production TypeScript build, bundle budget (`74.1 KiB` main JS, `16.9 KiB` CSS, `213.2 KiB` total gzip), backend formatting, Docker Compose, strict release/configuration JSON, whitespace checks and 114893-byte idempotent migration SQL generation passed.
- [x] Rechecked the authorized VPS read-only: the service is active with zero restarts, nginx is valid, direct/proxied/external health and the frontend return HTTP 200, the two-hour warning journal is empty, and the verified 435078-byte backup remains readable. PostgreSQL retains nine tariffs and zero rows in every checked garage, owner, staff, supplier, payment, accrual, meter, fund, import, form-state, integration-setting and audit table.

This audit introduces no schema, cleanup-policy or deployment-configuration changes. The feature branch remains undeployed because the staging workflow intentionally deploys only from `master`.

## Tenth test-coverage and publication-gate audit: 2026-07-15

- [x] Added mandatory repository rules requiring automated coverage for every changed method, function, endpoint, component, hook, query, filter, sort, pagination path, permission branch, validation, save/edit operation, error state and performance-sensitive path. A failed test, coverage gate, build, lint, formatting, privacy or migration check now explicitly blocks commit, merge, push and deployment.
- [x] Added enforced coverage gates to the staging workflow before release packaging: backend line coverage must remain at least 85% and branch coverage at least 70%; frontend statements, branches, functions and lines must remain at least 78%, 69%, 74% and 79% respectively. Generated EF migrations and the composition root are excluded from backend business-code coverage.
- [x] Covered every method of the authentication, audit, funds and user-management frontend API clients, including exact URLs, bounded pagination, filters, encoding, request payloads, authorization headers, exports and safe server/fallback errors. Added direct component coverage for dictionary empty, compact, expanded, active, open and archive-confirmation states.
- [x] Added an exhaustive authorization-handler matrix for all 40 combinations of four built-in roles and ten permissions. Existing controller architecture tests continue to require authorization metadata and the correct permission policy on every HTTP action.
- [x] Removed coverage-mode timing instability without extending global timeouts: long fee-campaign and financial-cancellation workflows were split into independent user scenarios, and the coordinated audit-filter debounce test now changes the five fields atomically before proving that exactly one final bounded request is sent.
- [x] Complete local gates passed: 1553/1553 backend tests with 89.80% line and 71.88% branch coverage; 483/483 frontend tests with 80.78% statements, 71.74% branches, 76.79% functions and 81.25% lines. ESLint, TypeScript production build, backend formatting, privacy scan of 656 files, Docker Compose configuration, release JSON, UTF-8/no BOM, whitespace and 118327-byte idempotent migration SQL generation passed.
- [x] Production bundle remains within budget: main JavaScript 74.5 KiB gzip, main CSS 18.4 KiB gzip and total JavaScript/CSS 220.1 KiB gzip. No production dependency or runtime bundle code was added by the coverage tooling.
- [x] The authorized VPS was audited read-only before publication: the service was active with zero restarts, nginx configuration was valid, the two-hour warning journal was empty, all sampled protected endpoints returned HTTP 401 without a token, PostgreSQL reported 155 valid indexes and 49 applied migrations, and representative indexed page queries completed in 0.044-0.188 ms. Twelve external `/health` checks returned HTTP 200 with 609.3 ms median and 680.5 ms p95 including a new TLS connection for each request.
- [x] GitHub Actions run `29393356172` passed every coverage, formatting, privacy, lint, build, bundle, migration, packaging and deployment step for commit `093b0f4`. Post-deployment verification confirmed the service active with zero restarts, valid nginx configuration, zero warning/error journal entries, HTTP 200 from local virtual-host and public health/frontend checks, and HTTP 401 from protected users, garages and reports endpoints without a token.

No production behavior, schema or data-cleanup policy changed in this audit, so an end-user «Что нового» entry is not required. Local PostgreSQL credentials remain unavailable on this workstation; database-specific verification used PostgreSQL integration tests and the authorized VPS read-only checks.

## Eleventh deferred-reference and consolidated-report audit: 2026-07-15

- [x] Removed unconditional loading of owners, garages and supplier groups from the dictionaries workspace. The active form now requests only its required reference list; unrelated dictionary sections perform no reference request, and mutation refreshes that need two lists run them concurrently.
- [x] Removed unconditional loading of garages, suppliers, income types and expense types from the reports workspace. The consolidated, cash, bank and fund tabs open without filter-dictionary traffic; the remaining lists are loaded once when their dependent report tab is first opened, with visible failure and retry coverage.
- [x] Replaced the consolidated report's two extra 500-row payment requests and client-side grouping with complete PostgreSQL-side income/expense breakdowns returned by the consolidated endpoint. A regression with 600 income and 600 expense operations proves that totals and breakdowns are no longer truncated.
- [x] Combined income and expense monthly aggregation into one grouped database command. PostgreSQL garage search is applied before materialization, while SQLite retains an explicitly scoped test fallback; garage aggregates and monthly rows use dictionary lookups instead of repeated linear scans.
- [x] The first complete frontend coverage run correctly rejected two stale expectations from the former eager-loading behavior. Tests were updated to distinguish page limits from reference limits and to require a report-filter error only after opening the dependent tab; the focused rerun passed 2/2 before the full gate was restarted.
- [x] Complete verification passed: backend 1609/1609 with 89.21% line and 71.35% branch coverage; frontend 516/516 with 81.01% statements, 72.04% branches, 76.82% functions and 81.57% lines. ESLint, production build, bundle budget (75.7 KiB main JS, 19.1 KiB CSS, 225.0/260.0 KiB total), backend formatting, privacy scan of 692 files, Docker Compose validation, 128700-byte idempotent migration SQL, release JSON and strict UTF-8/no BOM checks passed.
- [x] Static asset caching was rechecked and already returns long-lived immutable cache headers, so no risky deployment change was introduced. Local PostgreSQL listeners are available on ports 5432 and 5433, but current project credentials are unavailable; production data was not modified and database-specific behavior remains protected by EF query tests, migration generation and provider performance guards.

No schema, business-data, cleanup-policy or deployment-configuration change is included. End-user release note `0.670.0` describes the faster lazy loading and complete consolidated-report calculations. Publication remains intentionally pending because this task did not authorize push or deployment.

## Twelfth contractor and user-workbench latency audit: 2026-07-15

- [x] Removed the remaining all-or-nothing wait in the contractors workspace. Garage and supplier pages now stop their table loader as soon as the bounded server page arrives; owners, contacts, service settings, income types and tariffs continue independently and enrich the editor rows when ready.
- [x] Protected both response orders: reference data may finish before or after the paged list without losing owner details or supplier contacts. A delayed-reference workflow test proves that both working tables are already usable while their editor dictionaries remain pending.
- [x] Reused the role catalog across user search, paging and mutation refreshes. Failed role loads are evicted and retried, while a successful cached catalog cannot overwrite a newly saved role-permission matrix.
- [x] The first complete frontend run rejected an actual stale-role regression after a permission update. The refresh path was corrected to reload only the user page after mutations; the role-edit scenario and the new cache/retry regressions passed before the complete gate was restarted.
- [x] Complete verification passed: 1609/1609 backend tests with 89.21% line and 71.35% branch coverage; 519/519 frontend tests with 81.12% statements, 71.99% branches, 76.93% functions and 81.68% lines. ESLint, production build, backend formatting, privacy scan of 692 files, Docker Compose validation, release JSON, strict UTF-8/no BOM, whitespace checks and 128700-byte idempotent migration SQL generation passed.
- [x] Production bundle remains within budget: main JavaScript 75.6 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.2/260.0 KiB gzip.
- [x] The authorized VPS was checked read-only: `garagebalance-staging.service` is active with zero restarts, nginx is valid, the two-hour warning journal is empty, and five public health checks returned HTTP 200 in 0.276-0.329 seconds. PostgreSQL reports 156 indexes and active index scans on the reviewed garage, supplier and finance tables; no speculative schema migration was introduced without a measured slow SQL plan.

No database schema, production data, cleanup policy or deployment configuration changed in this pass. End-user release note `0.671.0` describes the faster contractor and user workbenches. Push and deployment remain intentionally pending because this task did not authorize publication.

## Thirteenth payment-table and report-query latency audit: 2026-07-15

- [x] Removed the all-or-nothing wait between the active payment page, finance summary and missing-meter diagnostics. The bounded page now ends its own table loader immediately; auxiliary requests settle independently and cannot hide a usable table when one of them fails.
- [x] Preserved stale-response protection for overlapping finance requests. Regression workflows prove that a ready payment page is visible before a delayed summary, remains visible after a summary failure, and still accepts only the latest page and summary responses.
- [x] Combined each income/expense report category's total and row count into one database aggregate. For accrual rows the measured command contract is now two `SELECT` statements per report query (combined total/count plus the bounded visible page) instead of three, while result totals, counts, filters and paging remain unchanged.
- [x] Updated the performance architecture guards to require server-side `group.Sum(...)` and `group.Count()` in the same aggregate. The first complete backend run correctly rejected the obsolete source-code expectation for separate `CountAsync` calls; focused guard tests passed 2/2 before the complete gate restarted.
- [x] Complete verification passed: backend 1611/1611 with 89.24% line and 71.45% branch coverage; frontend 521/521 with 81.14% statements, 71.98% branches, 76.92% functions and 81.71% lines. Focused report and finance regressions passed 20/20.
- [x] ESLint, production build, backend formatting, privacy scan of 692 files, Docker Compose validation, release JSON, whitespace checks and 128700-byte idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.6 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.4/260.0 KiB gzip.
- [x] The authorized VPS was checked read-only during diagnosis: the service is active with zero restarts, nginx configuration is valid, sampled Entity Framework commands complete in 0-5 ms, PostgreSQL reports 156 indexes, and public health/frontend checks return HTTP 200. The final external checks completed in 0.905 and 0.652 seconds respectively; production data and configuration were not modified.

No schema, business calculation, production data, cleanup policy or deployment configuration changed in this pass. End-user release note `0.672.0` describes the responsive payment table and reduced report round trips. Push and deployment remain intentionally pending because this task did not authorize publication.

## Fourteenth independent-tariff-table and fee-report audit: 2026-07-15

- [x] Split the tariffs workspace's all-or-nothing four-request load into three independent states: tariffs/services, irregular payments and fee campaigns. Each table now stops its own skeleton as soon as its data arrives, and an auxiliary failure cannot hide already usable rows in the other tables.
- [x] Kept persisted form state safe while the loads settle independently. Tariff and irregular-payment drafts are not written back until both editable catalogs have completed, preventing a slow irregular-payment response from being replaced by a premature empty snapshot.
- [x] Removed two redundant full grouped scans from the fee report. Accrual and collection totals are derived from the already aggregated fee rows; garage identity is reused from accrual groups and fetched separately only for payment-only garages. The measured query contract is two `SELECT` commands for normal rows and three when a payment-only garage requires identity lookup, instead of five commands for every report.
- [x] Preserved financial correctness for income operations without a garage. They remain included in the collected summary total but do not create a fictitious garage-detail row; regression coverage also verifies payment-only garage identity, totals, debt inputs and command counts.
- [x] The first complete frontend run rejected the obsolete single-loader expectation. The test was strengthened to delay and complete all three sources independently. A later full run exposed one unrelated transient user-toast lookup; its unchanged focused scenario passed 1/1, and the next complete run passed without weakening timeouts.
- [x] Complete verification passed: backend 1612/1612 with 89.26% line and 71.47% branch coverage; frontend 522/522 with 81.16% statements, 71.93% branches, 76.94% functions and 81.73% lines. Focused tariff and fee-report regressions passed 10/10.
- [x] ESLint, production build, backend formatting, privacy scan of 692 files, Docker Compose validation, release JSON, whitespace checks and 128700-byte idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.6 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.5/260.0 KiB gzip.
- [x] Production was checked without mutation: five public health requests and the frontend returned HTTP 200; sampled response times were 0.693-1.021 seconds for health and 0.726 seconds for the frontend. PostgreSQL-specific query behavior is protected by the command-count integration test and the existing provider performance guards; no speculative index or schema migration was added.

No schema, business rule, production data, cleanup policy or deployment configuration changed in this pass. End-user release note `0.673.0` describes the independent tariff tables and faster fee report. Push and deployment remain intentionally pending because this task did not authorize publication.

## Fifteenth lazy fee-reference and consolidated-breakdown audit: 2026-07-16

- [x] Removed the remaining unconditional garage-catalog request from the tariffs workspace. Up to 100 garage records are now requested only when an administrator creates or edits a fee campaign; the tariff, irregular-payment and campaign tables no longer compete with that auxiliary request during initial loading.
- [x] Added in-flight request reuse and successful-result caching for the fee form. Repeated clicks cannot start duplicate requests, reopening the form reuses the loaded garage list, and a failed request is released for a visible user-triggered retry.
- [x] Combined the consolidated report's income and expense breakdowns into one database command with `UNION ALL`. The measured monthly query contract is five `SELECT` commands instead of six, while regression tests preserve both breakdown totals and the existing monthly, accrual, meter and starting-balance calculations.
- [x] Complete verification passed: backend 1613/1613 with 89.26% line and 71.47% branch coverage; frontend 523/523 with 81.19% statements, 71.94% branches, 76.98% functions and 81.76% lines. Focused lazy-load/retry and consolidated-report regressions passed 6/6.
- [x] ESLint, production build, backend formatting, privacy scan of 692 files, Docker Compose validation, standalone non-Docker publish/build, whitespace checks and 128700-byte idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.6 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.6/260.0 KiB gzip.
- [x] Production was checked read-only without deployment: five public health requests and the frontend returned HTTP 200. After the first TLS connection, health responses completed in 0.142-0.237 seconds; the frontend completed in 0.145 seconds. Local PostgreSQL listens on port 5432, but the project test credentials are unavailable, so no local production-like database mutation was attempted.

No schema, business rule, production data, cleanup policy or deployment configuration changed in this pass. End-user release note `0.674.0` describes the deferred fee-form reference loading and reduced consolidated-report round trips. Push and deployment remain intentionally pending because this task did not authorize publication.

## Sixteenth report-aggregate round-trip audit: 2026-07-16

- [x] Combined garage-report accrual and income totals into one server aggregate. The measured query contract is now three `SELECT` commands for totals, grouped row count and the bounded page instead of four separate commands; existing grouped/expanded modes, starting balances, search and totals remain unchanged.
- [x] Combined income, accrual and meter-reading groups for searched consolidated garage rows into one `UNION ALL` command. The measured search contract is now two `SELECT` commands (matching garage identities plus all three aggregate categories) instead of four.
- [x] Combined count and amount totals in the PostgreSQL cash-payment and bank-deposit report branches. Each report now needs two commands for aggregate metadata and its bounded visible page instead of three.
- [x] Replaced three separate fund-change count/deposit/withdrawal aggregates with one grouping by operation kind. The normal report path now needs one aggregate command, one bounded page command and only when necessary one user-name lookup instead of three aggregate commands plus the page and lookup.
- [x] Focused report, correctness and performance-guard verification passed 60/60 after two new command-count regressions. The first full backend run was correctly blocked by one obsolete guard that expected three former dictionaries; the guard was strengthened to require the combined result and the complete rerun passed.
- [x] Complete verification passed: backend 1616/1616 with 89.13% line and 71.47% branch coverage; frontend 523/523 with 81.19% statements, 71.94% branches, 76.98% functions and 81.76% lines. ESLint, production build, backend formatting, privacy scan of 692 files, Docker Compose validation, standalone publish/build, whitespace checks and 128700-byte idempotent migration SQL generation passed.
- [x] The production bundle remains within budget: main JavaScript 75.6 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.6/260.0 KiB gzip. Production was checked read-only without deployment: five public health requests and the frontend returned HTTP 200; after the first TLS connection responses completed in 0.141-0.235 seconds and the frontend in 0.140 seconds.

No schema, business rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL is reachable but project test credentials remain unavailable, so PostgreSQL-specific behavior is protected by the production-query guards, SQLite integration tests and idempotent migration generation. End-user release note `0.675.0` describes the faster garage, cash, bank and fund reports. Push and deployment remain intentionally pending because this task did not authorize publication.

## Seventeenth payment-summary request audit: 2026-07-16

- [x] Combined the meter-reading and supplier-accrual section counts used by the payment summary into one PostgreSQL `UNION ALL` command. The measured no-search summary contract is now three `SELECT` commands instead of four, while period, search, empty-result and Cyrillic case-insensitive SQLite fallback behavior remain covered.
- [x] Reused an already fulfilled or in-flight payment summary when the user changes the active finance table or page without changing filters. This removes duplicate summary traffic from routine navigation; every create, edit, regular generation, cancellation and restoration path explicitly requests a fresh summary after the mutation.
- [x] Added regression coverage for the three-command database contract, independent supplier/meter search and period filtering, summary reuse during tab changes, and mandatory refresh after financial mutations. Existing stale-response protection and the rule that the active table becomes usable before auxiliary summary data remain intact.
- [x] The first complete backend gate correctly rejected two obsolete architecture expectations and exposed the existing SQLite Cyrillic-search requirement. The production query guard was updated to require the combined query, and the SQLite-only fallback was restored without weakening PostgreSQL aggregation; focused verification then passed 6/6 before the complete rerun.
- [x] Complete verification passed: backend 1617/1617 with 89.02% line and 71.23% branch coverage; frontend 523/523 with 81.20% statements, 71.95% branches, 76.99% functions and 81.78% lines. ESLint, production build, backend formatting, privacy scan of 694 files, Docker Compose validation, whitespace checks and 128700-byte idempotent migration SQL generation passed.
- [x] The production bundle remains within budget: main JavaScript 75.6 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.7/260.0 KiB gzip. Production was checked read-only without deployment: five public health requests and the frontend returned HTTP 200; health completed in 0.162-1.151 seconds and the frontend in 0.157 seconds.

No schema, financial formula, business rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable; PostgreSQL behavior is protected by query-shape guards, EF integration tests and idempotent migration generation. End-user release note `0.676.0` describes the faster payment-table navigation and reduced summary round trips. Push and deployment remain intentionally pending because this task did not authorize publication.

## Eighteenth consolidated-monthly-query audit: 2026-07-16

- [x] Rechecked the remaining measured report query contracts and selected the highest confirmed sequential path: the consolidated monthly report still issued five `SELECT` commands for one period.
- [x] Combined monthly income/expense, accrual and meter-reading aggregates into one server-side `UNION ALL` command. Complete income/expense breakdowns and active-garage starting balances remain separate, reducing the full monthly query contract from five commands to three.
- [x] Kept every growing-table operation in PostgreSQL: period filters, grouping, money sums and row counts are applied before materialization. No unbounded client-side financial scan was introduced.
- [x] Strengthened the command-count regression with income, expense, accrual and meter data in the same month, and extended the architecture guard to require all three monthly aggregate branches in the combined query. Focused consolidated-report verification passed 8/8.
- [x] Complete verification passed: backend 1617/1617 with 89.03% line and 71.23% branch coverage; frontend 523/523 with 81.20% statements, 71.95% branches, 76.99% functions and 81.78% lines. ESLint, production build, backend formatting, privacy scan of 694 files, Docker Compose validation, whitespace checks and 128700-byte idempotent migration SQL generation passed.
- [x] The production bundle remains within budget: main JavaScript 75.6 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.7/260.0 KiB gzip.
- [x] Production was checked read-only without deployment: five public health requests and the frontend returned HTTP 200. After the first TLS connection, health completed in 0.152-0.156 seconds and the frontend in 0.155 seconds.

No schema, financial formula, business rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable; PostgreSQL behavior is protected by query-shape guards, EF integration tests and idempotent migration generation. End-user release note `0.677.0` describes the faster consolidated monthly report. Push and deployment remain intentionally pending because this task did not authorize publication.

## Nineteenth finance-total round-trip audit: 2026-07-16

- [x] Measured the remaining payment-summary path after section-count optimization. Operation totals and accrual totals still executed as two sequential aggregates before the combined section-count query.
- [x] Added a dedicated application query that combines income, expense and accrual totals and counts into one PostgreSQL `UNION ALL` command. Together with the existing combined section counts, the full summary now requires two `SELECT` commands instead of three.
- [x] Preserved the exact filter split: date, operation kind, garage, supplier and staff filters constrain operations, while month and search rules continue to govern accrual totals. A regression proves that selecting only income operations does not incorrectly hide accrual totals.
- [x] Preserved case-insensitive Cyrillic search in the explicitly scoped SQLite test fallback. PostgreSQL filtering, grouping, conditional sums and counts remain server-side before materialization.
- [x] Removed the obsolete repository summary methods and DTOs so there is one production calculation path. Architecture guards require the combined totals query and prevent reintroducing the retired repository method.
- [x] The first complete backend gate correctly rejected one obsolete source guard that still required the removed repository method. The guard was changed to prohibit that method, its focused rerun passed 1/1, and the complete backend gate then passed.
- [x] Complete verification passed: backend 1618/1618 with 88.92% line and 70.93% branch coverage; frontend 523/523 with 81.20% statements, 71.95% branches, 76.99% functions and 81.78% lines. ESLint, production build, backend formatting, privacy scan of 696 files, Docker Compose validation, whitespace checks and 128700-byte idempotent migration SQL generation passed.
- [x] The production bundle remains within budget: main JavaScript 75.6 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.7/260.0 KiB gzip.
- [x] Production was checked read-only without deployment: five public health requests and the frontend returned HTTP 200. After the first TLS connection, health completed in 0.159-0.245 seconds and the frontend in 0.160 seconds.

No schema, financial formula, business rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable; PostgreSQL behavior is protected by query-shape guards, EF integration tests and idempotent migration generation. End-user release note `0.678.0` describes the faster financial summary. Push and deployment remain intentionally pending because this task did not authorize publication.

## Twentieth deferred payment-form reference audit: 2026-07-16

- [x] Removed six unconditional dictionary requests from the normal payment-screen opening path when the extended financial overview is disabled: supplier groups, suppliers, staff members, income types, expense types and tariffs are now requested only when a form that needs them is opened.
- [x] Preserved the extended overview mode. When that user setting is enabled, the same reference bundle is prepared automatically so its existing inline finance forms retain their initialized selections and calculations.
- [x] Added in-flight reuse, successful-result caching and failure eviction. Concurrent form actions share one request, reopening a form does not reload successful dictionaries, and a failed request leaves the form closed with a visible error and can be retried by the user.
- [x] Protected authentication/client changes and unmounts with a generation guard so an obsolete response cannot overwrite the current session's form dictionaries.
- [x] Added a workflow regression covering the zero-request initial state, all six deferred calls, visible failure, user retry, successful dialog opening and cache reuse. Existing income, expense and payment-modal workflows passed separately before the complete suite.
- [x] Complete verification passed: backend 1618/1618 with 88.92% line and 70.93% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy scan of 696 files, Docker Compose validation, standalone backend publish, strict release JSON/UTF-8 validation, whitespace checks and 128700-byte idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.
- [x] Production was checked read-only without deployment: five public health requests and the frontend returned HTTP 200. After the first TLS connection, health completed in 0.161-0.171 seconds and the frontend in 0.168 seconds.
- [x] Published to `master` as commit `f0a272b` through GitHub Actions run `29442474021`. The workflow passed every verification and deployment step, created the pre-migration PostgreSQL backup, validated nginx, applied the idempotent migration script and reported `deployStatus=ok` for release `f0a272bfe989660a3374b025d6f3b4eca85f1a78-52`.
- [x] Post-deployment checks returned HTTP 200 for five `/health` requests, the frontend document and its deployed `index-BTDaYAgk.js` asset. Warm health responses completed in 0.158-0.249 seconds and the frontend in 0.155 seconds.

No schema, financial formula, business rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. End-user release note `0.679.0` describes the faster payment-screen opening and safe retry behavior. The optimized build is published and verified on staging.

## Twenty-first garage-income-worksheet round-trip audit: 2026-07-16

- [x] Rechecked the remaining selected-garage loading path and confirmed that the financial worksheet still executed six sequential `SELECT` commands: one garage lookup followed by separate opening-accrual, opening-income, accrual-bucket, income-bucket and meter-reading queries.
- [x] Added a dedicated application query that combines the five worksheet data branches into one server-side `UNION ALL` command. Together with the garage lookup, the measured contract is now two `SELECT` commands instead of six.
- [x] Preserved the financial model and visible result: opening debt, per-service accrual and income totals, month range, canceled-record exclusion, and latest water/electricity reading selection remain unchanged. Growing tables are filtered and aggregated before materialization.
- [x] Removed the three obsolete repository methods and data contracts that previously served the separate service, accrual and meter-reading branches. Architecture and performance guards now require the combined query and prevent the retired calls from returning.
- [x] Added a command-count regression populated with previous-period debt, current accrual, payment and meter data, plus direct cancellation propagation coverage. Focused worksheet and architecture verification passed 13/13.
- [x] The first complete backend run correctly rejected three obsolete architecture expectations. They were replaced with guards for the new query and prohibition of the retired methods; the coverage rerun passed 1621/1621 with 86.30% line and 70.04% branch coverage, and the final complete suite with the cancellation regression passed 1622/1622.
- [x] Complete frontend verification passed 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines. ESLint, production build, backend formatting, privacy scan of 698 files, Docker Compose validation, standalone backend publish, whitespace checks and 128700-byte idempotent migration SQL generation passed.
- [x] The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so PostgreSQL behavior is protected by the combined-query source guard, EF integration tests and idempotent migration generation. End-user release note `0.680.0` describes the faster financial card for the selected garage. Push and deployment remain intentionally pending because this task did not authorize publication.

## Twenty-second garage-balance-history round-trip audit: 2026-07-16

- [x] Rechecked the remaining garage financial-report path and confirmed that balance history still executed five sequential `SELECT` commands: garage identity, opening accruals, opening income, monthly accruals and monthly income.
- [x] Added a dedicated application query that combines both opening totals and both monthly bucket sets into one server-side `UNION ALL` command. Together with the garage lookup, the measured report contract is now two `SELECT` commands instead of five.
- [x] Preserved the exact running-debt model: starting balance, transactions before the selected period, monthly accruals, monthly income, canceled-record exclusion and closing debt remain unchanged. Empty months are still emitted with zero values.
- [x] Added regression coverage for previous-period debt, two populated months, an empty month, missing garage, reversed and oversized periods, cancellation propagation and the two-command contract. Invalid periods are confirmed to fail before any database access.
- [x] Removed the obsolete monthly-income repository method and DTO. Architecture and performance guards require server grouping and one materialization in the combined query and prohibit the retired repository path.
- [x] The first two complete backend attempts correctly rejected obsolete architecture and grouping guards. After moving those requirements to the new query, focused verification passed 12/12 and the complete coverage run passed 1628/1628 with 86.34% line and 70.06% branch coverage.
- [x] Complete frontend verification passed 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines. ESLint, production build, backend formatting, privacy scan of 700 files, Docker Compose validation, standalone backend publish, whitespace checks and 128700-byte idempotent migration SQL generation passed.
- [x] The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL listens on port 5432, but project test credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by combined-query source guards, EF integration tests and idempotent migration generation. End-user release note `0.681.0` describes the faster garage financial report. Push and deployment remain intentionally pending because this task did not authorize publication.

## Twenty-third available-balance and expense-worksheet audit: 2026-07-16

- [x] Rechecked the supplier-payment worksheet and confirmed nine sequential `SELECT` commands: supplier accruals, two operation lists, staff, two bank-balance aggregates and three cash-balance aggregates. The active bank-deposit total was read twice during one worksheet request.
- [x] Added a dedicated application query that combines total income, cash expenses, bank expenses and active bank deposits into one server-side `UNION ALL` command. The expense worksheet now needs five `SELECT` commands instead of nine.
- [x] Reused the same combined balance calculation in save/edit validation paths. A cash check now needs one balance query instead of three and a bank check one instead of two, while the existing maximum-available-amount rules remain unchanged.
- [x] Preserved classification by operation kind and configured cash expense codes/names, cancellation exclusion, non-negative cash/bank results and money rounding. Mixed cash/bank payments still reconcile exactly to collected income minus expenses.
- [x] Removed the obsolete fund-deposit and financial-operation balance repository methods and DTO. Architecture and performance guards require database filtering, conditional sums, `deposit` filtering and one materialization in the combined query.
- [x] Added regressions for the five-command worksheet contract, mixed cash/bank correctness, an empty database in one command and cancellation propagation. Focused worksheet/balance verification passed 8/8; the corrected guard subset passed 5/5.
- [x] The first complete backend attempt correctly rejected one obsolete fund-deposit guard. After moving it to the combined query, the complete coverage run passed 1631/1631 with 86.36% line and 70.06% branch coverage.
- [x] Complete frontend verification passed 524/524 with 81.27% statements, 71.93% branches, 77.07% functions and 81.83% lines. ESLint, production build, backend formatting, privacy scan of 702 files, Docker Compose validation, standalone backend publish, whitespace checks and 128700-byte idempotent migration SQL generation passed.
- [x] The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by combined-query source guards, EF integration tests and idempotent migration generation. End-user release note `0.682.0` describes the faster supplier-payment worksheet and balance checks. Push and deployment remain intentionally pending because this task did not authorize publication.

## Twenty-fourth expense-worksheet data audit: 2026-07-16

- [x] Rechecked the remaining supplier-payment worksheet path after the available-balance optimization and confirmed five `SELECT` commands: supplier accruals, separate expense and income operation lists, active staff and available balances.
- [x] Added a dedicated application query that combines supplier accruals, supplier expenses, active staff, staff expenses and income buckets into one server-side `UNION ALL` command. Together with the available-balance query, the measured worksheet contract is now two `SELECT` commands instead of five and two instead of the original nine.
- [x] Moved supplier, staff and income grouping before materialization and projected only the fields required by the worksheet. No growing financial table is loaded as complete tracked entities for this screen.
- [x] Removed the three obsolete repository paths that returned complete supplier-accrual, financial-operation and staff entity sets. Architecture guards require the new application port and prevent the retired calls from returning.
- [x] Added regressions for the two-command contract, supplier rows with both sides, accrual-only and expense-only rows, staff payments, income types with and without codes, empty data in one command and cancellation propagation. Existing mixed cash/bank reconciliation remains green.
- [x] The first two complete coverage checks passed all 1634 tests but correctly stopped at 69.98% and 69.99% branch coverage. Missing business variants were added instead of weakening the gate; the final run passed 1634/1634 with 86.44% line and 70.01% branch coverage.
- [x] Complete frontend verification passed 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines. ESLint, production build, backend formatting, privacy scan of 704 files, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed.
- [x] The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by combined-query source guards, EF integration tests and idempotent migration generation. End-user release note `0.683.0` describes the faster supplier-payment worksheet. Push and deployment remain intentionally pending because this task did not authorize publication.

## Twenty-fifth garage-report summary audit: 2026-07-16

- [x] Rechecked the remaining measured report paths and confirmed that the garage report still executed separate commands for financial totals, grouped-row count and the bounded visible page.
- [x] Combined accrual total, income total and grouped-row count in one database aggregate. A populated report now needs two `SELECT` commands instead of three while the visible page remains bounded with `Skip` and `Take`.
- [x] Added an empty-summary short circuit. A period without starting balances, accruals or payments returns after one aggregate command and does not execute an unnecessary page query.
- [x] Preserved garage/owner search, expanded and grouped service modes, starting balances, canceled-record exclusion, period filters, sorting, pagination and financial totals. All aggregation remains in the database before materialization.
- [x] Added regressions for the two-command populated contract, one-command empty contract and cancellation propagation. Existing report correctness, grouped counting, search, pagination and invalid-period tests passed in the focused 10/10 run.
- [x] Complete verification passed: backend 1636/1636 with 86.44% line and 70.00% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy scan of 704 files, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by database-query source guards, EF integration tests and idempotent migration generation. End-user release note `0.684.0` describes the faster garage report. Push and deployment remain intentionally pending because this task did not authorize publication.

## Twenty-sixth consolidated-monthly starting-balance audit: 2026-07-16

- [x] Rechecked the remaining report command-count regressions and confirmed that the consolidated monthly query still executed three commands: combined monthly aggregates, operation breakdowns and a separate active-garage starting-balance sum.
- [x] Added active nonzero garage starting balances to the existing monthly `UNION ALL` pipeline. A populated consolidated monthly report now needs two `SELECT` commands instead of three.
- [x] Skipped the operation-breakdown query when the monthly aggregate proves that the selected period has neither income nor expense operations. A completely empty period now returns from the monthly query after one command.
- [x] Preserved monthly income, expense, accrual and meter counts, complete type breakdowns, active-garage filtering, canceled-record exclusion and starting-balance inclusion in the first report month. All growing-table grouping remains in the database.
- [x] Added regressions for the two-command populated contract with a nonzero starting balance, one-command empty contract and cancellation propagation. Existing consolidated totals, 1200-operation completeness, garage limit/search and normalized-period scenarios passed in the focused 10/10 run.
- [x] Complete verification passed: backend 1638/1638 with 86.45% line and 70.02% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy scan of 704 files, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by combined-query source guards, EF integration tests and idempotent migration generation. End-user release note `0.685.0` describes the faster consolidated report. Push and deployment remain intentionally pending because this task did not authorize publication.

## Twenty-seventh fee-report unified-data audit: 2026-07-16

- [x] Rechecked the final report command-count regression above two commands and confirmed variable behavior in the fee report: two commands for garages with accruals and three when a payment-only garage required a separate identity lookup.
- [x] Combined garage accrual groups and payment groups in one database `UNION ALL` command. Garage and owner identity fields are projected in both branches, so a payment-only garage no longer triggers a follow-up query.
- [x] Reduced the fee-data query to one `SELECT` command for normal, payment-only and empty results. Payments without a garage remain included in collected totals but are excluded from garage rows exactly as before.
- [x] Preserved canceled-record exclusion, income-type filtering, accrual and collected totals, last-payment dates, garage identities, debtor rows and complete report summaries. Both growing transaction sources are grouped before the single materialization.
- [x] Added regressions for the one-command contract with same-garage, payment-only and no-garage payments, one-command empty data and cancellation propagation. Existing campaign, variation, totals, debtors and audit scenarios passed in the focused 7/7 run.
- [x] Complete verification passed: backend 1640/1640 with 86.45% line and 70.02% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy scan of 704 files, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by unified-query source guards, EF integration tests and idempotent migration generation. End-user release note `0.686.0` describes the faster fee report. Push and deployment remain intentionally pending because this task did not authorize publication.

## Twenty-eighth financial-operation history N+1 audit: 2026-07-16

- [x] Rechecked the main finance tables after report round-trip optimization and found a high-impact N+1 path in operation history. Every garage income or supplier expense row separately queried accrual totals, previous payments and monthly allocation buckets; a 25-row page could issue up to 102 `SELECT` commands.
- [x] Added a dedicated application query that loads previous-payment totals for all visible operations in one command and garage/supplier monthly accrual buckets in one combined `UNION ALL` command. Page count and bounded items remain the first two commands, producing a constant four-command contract.
- [x] Rebuilt visible debt-before, debt-after and payment-allocation DTO values from the batch data with the existing money rounding, starting-balance, negative-balance and previous-payment rules. Single-operation create/update validation paths remain unchanged.
- [x] Kept the batch bounded to visible operation IDs, involved garage/supplier IDs and the latest visible accounting month. Canceled operations and accruals remain excluded, and grouping occurs before materialization.
- [x] Added a six-operation regression proving the constant four-command page contract and exact first/last garage and supplier debts; the former implementation would require up to 26 commands for the same page. Empty input performs no database access and cancellation is propagated.
- [x] Complete verification passed: backend 1645/1645 with 86.53% line and 70.08% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy scan of 706 files, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by bounded batch-query guards, EF integration tests and idempotent migration generation. End-user release note `0.687.0` describes the faster financial-operation history. Push and deployment remain intentionally pending because this task did not authorize publication.

## Twenty-ninth bulk-accrual generation N+1 audit: 2026-07-16

- [x] Rechecked the remaining sequential database calls in finance workflows and found per-record duplicate queries in fee-campaign accrual generation and supplier-group salary generation.
- [x] Replaced garage-by-garage duplicate checks with one bounded query returning the existing garage identifiers for the selected income type, month and fee-campaign source.
- [x] Added the equivalent supplier identifier query scoped by expense type, month, source and document number, preserving the exact duplicate key used before the optimization.
- [x] Preserved active/archived filtering, selected campaign participants, money rounding, comments, audit events, duplicate prevention and the existing empty-result behavior.
- [x] Added 200-record regressions proving constant query counts and exact totals for both first and repeated runs: at most three `SELECT` commands for fee campaigns and four for supplier-group salaries.
- [x] Complete verification passed: backend 1647/1647 with 86.54% line and 70.08% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy scan of 706 files, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by constant-query regressions, repository source guards, EF integration tests and idempotent migration generation. End-user release note `0.688.0` describes faster bulk accrual generation. Push and deployment remain intentionally pending because this task did not authorize publication.

## Thirtieth missing-meter-reading query audit: 2026-07-16

- [x] Rechecked the remaining sequential database reads used by visible finance screens and confirmed that missing-meter control queried every requested meter kind separately and then loaded the same candidate garages again.
- [x] Replaced the per-kind loop with one bounded database projection containing garage identity, owner fields and water/electricity presence flags. The final missing rows are assembled from that bounded result without additional database access.
- [x] Reduced the normal two-kind screen from three `SELECT` commands to one. Query count remains one with 200 garages and a 100-row output limit.
- [x] Preserved active-garage and canceled-reading rules, month normalization, owner/garage search, meter-kind order, final row limit, owner-name formatting and the behavior when a complete earlier garage must be skipped.
- [x] Added regressions for the one-command 200-garage contract and cancellation propagation; existing both-kind, single-kind, search, archived, canceled, unknown-kind and limit tests remain green.
- [x] Complete verification passed: backend 1649/1649 with 86.55% line and 70.05% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy scan of 706 files, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by the single-command regression, bounded-query source guards, EF integration tests and idempotent migration generation. End-user release note `0.689.0` describes faster missing-meter control. Push and deployment remain intentionally pending because this task did not authorize publication.

## Thirty-first financial-operation display unification audit: 2026-07-16

- [x] Rechecked the constant-query financial-operation page and confirmed that previous-payment calculations and monthly garage/supplier accrual buckets still required two sequential display-data commands after the bounded page queries.
- [x] Combined visible-operation calculations, garage accrual buckets and supplier accrual buckets into one server-side `UNION ALL` pipeline with a row discriminator. Page count and bounded page items remain separate, reducing the complete page from four `SELECT` commands to three.
- [x] Kept the query scoped to visible operation identifiers and involved counterparties. Accrual grouping remains in the database and canceled operations/accruals remain excluded according to the existing rules.
- [x] Preserved previous-payment totals, garage and supplier starting balances, debt before/after, month allocation ordering, mixed income/expense pages and exact money values.
- [x] Updated the six-operation regression to require the three-command contract and exact first/last garage and supplier debts. Empty input still performs no database access and cancellation propagation remains covered.
- [x] Complete verification passed: backend 1649/1649 with 86.55% line and 70.05% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy scan of 706 files, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by the three-command page regression, unified-query source guards, EF integration tests and idempotent migration generation. End-user release note `0.690.0` describes the faster financial-operation details. Push and deployment remain intentionally pending because this task did not authorize publication.

## Thirty-second finance-summary aggregate audit: 2026-07-16

- [x] Rechecked the summary requested beside every finance working table and confirmed two sequential aggregate commands: operations/garage accruals and meter/supplier-accrual section counts.
- [x] Extended the finance totals application query to combine operations, garage accruals, meter readings and supplier accruals in one server-side `UNION ALL` pipeline. The obsolete second query abstraction and implementation were removed from dependency injection and tests.
- [x] Reduced the complete summary from two `SELECT` commands to one while keeping all growing-table aggregation in the database before materialization.
- [x] Preserved operation-kind semantics, date-to-month normalization, search across the fields of each section, canceled-record exclusion, income/expense/accrual totals, balance, debt and all five section counts.
- [x] Updated the aggregate regression to require one command and exact counts. Search-specific supplier/meter counts, operation-kind isolation, empty values and cancellation remain covered by the complete suite.
- [x] Complete verification passed: backend 1649/1649 with 86.56% line and 70.04% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy scan of 706 files, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by the one-command summary regression, aggregate-query source guards, EF integration tests and idempotent migration generation. End-user release note `0.691.0` describes the faster finance summary. Push and deployment remain intentionally pending because this task did not authorize publication.

## Thirty-third garage-balance history identity audit: 2026-07-16

- [x] Rechecked the financial history of a selected garage and confirmed that it first loaded garage identity and starting balance, then performed a second combined query for previous totals and monthly buckets.
- [x] Added active garage identity, owner name parts and starting balance to the existing server-side `UNION ALL` pipeline. The complete history now needs one `SELECT` command instead of two.
- [x] Preserved the missing/archived garage response, previous accrual and payment totals, canceled-record exclusion, monthly ordering, opening and closing debt, money rounding and the maximum-period validation.
- [x] Added regressions for the one-command populated, empty-financial-data and missing-garage contracts. The performance source guard requires a single materialization of all five query branches.
- [x] Complete verification passed: backend 1649/1649 with 86.59% line and 70.04% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy tests, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by the one-command integration regressions, combined-query source guard and idempotent migration generation. End-user release note `0.692.0` describes the faster garage balance history. Push and deployment remain intentionally pending because this task did not authorize publication.

## Thirty-fourth garage-income worksheet identity audit: 2026-07-16

- [x] Rechecked the garage income worksheet and confirmed that it loaded the active garage identity and starting balance separately before requesting its combined financial and meter data.
- [x] Added active garage identity, owner name parts and starting balance to the existing server-side `UNION ALL` pipeline. The complete worksheet now needs one `SELECT` command instead of two.
- [x] Preserved previous-period debt, nonnegative debt rules, monthly accruals and incomes, latest meter-reading selection, canceled-record exclusion, row ordering and exact money rounding.
- [x] Added one-command regressions for populated and empty periods, missing and archived garages, plus zero-command validation for reversed and oversized periods. Cancellation propagation remains covered.
- [x] Kept ordinary garage point-lookups on the shared repository while explicitly guarding the dedicated balance-history and income-worksheet aggregate application ports.
- [x] Complete verification passed: backend 1654/1654 with 86.67% line and 70.10% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy tests, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by one-command integration regressions, aggregate-query and architecture source guards, and idempotent migration generation. End-user release note `0.693.0` describes the faster garage income worksheet. Push and deployment remain intentionally pending because this task did not authorize publication.

## Thirty-fifth expense-worksheet available-balance audit: 2026-07-16

- [x] Rechecked the expense worksheet and confirmed that supplier/staff rows were loaded by one aggregate command while available cash and bank amounts required a second command.
- [x] Added all-time income, cash-expense, bank-expense and non-canceled bank-deposit aggregates to the existing server-side `UNION ALL` worksheet pipeline. The complete form now needs one `SELECT` command instead of two.
- [x] Preserved expense-type code/name classification, nonnegative available amounts, supplier and staff accruals, collected-income matching, canceled-record exclusion, money rounding, row ordering and every displayed total.
- [x] Added a one-command empty-form regression and extended the populated one-command regression to assert exact accrual, expense, collected, cash and bank totals. Query cancellation and the standalone available-balance endpoint remain covered.
- [x] Complete verification passed: backend 1655/1655 with 86.72% line and 70.10% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy tests, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by the one-command integration regressions, unified-query source guards and idempotent migration generation. End-user release note `0.694.0` describes the faster expense worksheet. Push and deployment remain intentionally pending because this task did not authorize publication.

## Thirty-sixth consolidated-report single-command audit: 2026-07-16

- [x] Rechecked the consolidated monthly report and confirmed that it loaded monthly income/expense/accrual/meter totals and starting balances first, then executed a second command for complete income/expense type breakdowns.
- [x] Unified all six database aggregate branches in one server-side `UNION ALL` pipeline with explicit row categories. Populated and empty reports now need one `SELECT` command.
- [x] Preserved period filtering, canceled-record exclusion, active-garage starting balances, monthly counts, complete type breakdowns, fallback type labels and exact financial totals.
- [x] Added deterministic in-memory ordering after the bounded aggregate result so monthly rows and type breakdowns remain stable for screen rendering and exports.
- [x] Updated the command-count regression from two commands to one and strengthened the source guard to require all five unions and one materialization. Focused report tests passed 12/12 after a transient MSBuild worker exit was cleared; the complete normal build/test runs then passed without recurrence.
- [x] Complete verification passed: backend 1655/1655 with 86.74% line and 70.10% branch coverage; frontend 524/524 with 81.27% statements, 71.93% branches, 77.07% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy tests, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by the one-command report regression, unified-query source guard and idempotent migration generation. End-user release note `0.695.0` describes the faster consolidated report. Push and deployment remain intentionally pending because this task did not authorize publication.

## Thirty-seventh income-report payment-debt batch audit: 2026-07-16

- [x] Rechecked the income-report payment page and confirmed that starting garage balances, monthly accrual buckets and related earlier payments still required three sequential database commands after the bounded count and page queries.
- [x] Combined those three debt inputs into one server-side `UNION ALL` pipeline with explicit row categories and one materialization. A populated payment page now needs three `SELECT` commands instead of five.
- [x] Kept the combined query bounded to garages visible on the current page and to the latest required month and operation date. Growing financial tables are filtered and aggregated in the database before materialization.
- [x] Preserved pagination, search and sort behavior, starting balances, monthly accrual allocation, canceled-record exclusion, chronological ordering by operation date, creation time and identifier, exact money values and report totals.
- [x] Strengthened the two-payment regression to require the three-command contract and exact debts of 1,300.00 and 1,000.00. Added a 25-payment regression proving the query count remains three and the first/last debts remain exactly 4,900.00 and 2,500.00.
- [x] Complete verification passed: backend 1656/1656 with 86.76% line and 70.10% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy tests, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by constant-query regressions, the unified-query source guard, EF integration tests and idempotent migration generation. End-user release note `0.696.0` describes the faster income-report debt calculation. Push and deployment remain intentionally pending because this task did not authorize publication.

## Thirty-eighth garage and supplier balance aggregate audit: 2026-07-16

- [x] Rechecked the main contractor tables and confirmed that a garage page loaded accrual and income totals in two sequential commands, while a supplier page loaded starting balances, accruals and payments in three sequential commands after its bounded page and contact queries.
- [x] Combined garage accrual/income totals into one server-side `UNION ALL` query and supplier starting-balance/accrual/payment totals into another. Both queries remain scoped to identifiers visible on the requested page.
- [x] Reduced a complete garage page from four `SELECT` commands to three and a complete supplier page from six commands to four. Query counts remain constant with 200 database records and a 25-row page.
- [x] Preserved search, database sorting, offset/limit pagination, archived-record rules, canceled financial-record exclusion, primary-contact selection and the exact `starting balance + accruals - payments` formula.
- [x] Added exact-value regressions proving a 100.00 starting balance, 50.00 accrual and 20.00 payment remain a 130.00 balance/debt for every visible row. Empty identifier collections return empty aggregates without database access.
- [x] Complete verification passed: backend 1659/1659 with 86.80% line and 70.23% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy tests, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by constant-query page regressions, unified-aggregate source guards, EF integration tests and idempotent migration generation. End-user release note `0.697.0` describes faster balance loading in the garage and supplier tables. Push and deployment remain intentionally pending because this task did not authorize publication.

## Thirty-ninth expense-report aggregate audit: 2026-07-16

- [x] Rechecked the complete supplier-expense report and confirmed that starting obligations, supplier accruals and payments each executed a separate totals command in addition to their bounded visible-row queries.
- [x] Combined all selected totals branches in one server-side `UNION ALL` pipeline. A complete report now needs four `SELECT` commands instead of six, while single-section reports retain their existing two-command path.
- [x] Kept every aggregate and source filter in the database. The initially attempted named projection was rejected by the EF translation regression before completion and replaced with a supported anonymous server projection; no client-side full-table calculation was introduced.
- [x] Preserved supplier and expense-type filters, search, offset/limit pagination, starting obligations, canceled-record exclusion, row ordering, exact totals and the existing screen/export contract.
- [x] Added a 401-row regression proving a constant four-command report with exactly 10,100.00 of obligations/accruals, 4,000.00 of payments, a 401 total row count and a bounded 25-row page.
- [x] Complete verification passed: backend 1660/1660 with 86.82% line and 70.23% branch coverage; frontend 524/524 with 81.27% statements, 71.93% branches, 77.07% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, privacy tests, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by the four-command report regression, unified-aggregate source guard, EF integration tests and idempotent migration generation. End-user release note `0.698.0` describes the faster expense-report totals. Push and deployment remain intentionally pending because this task did not authorize publication.

## Fortieth income-report aggregate audit: 2026-07-16

- [x] Rechecked the complete garage-income report and confirmed that starting debt, accruals and payments each executed a separate totals command in addition to their bounded visible-row queries and the already unified payment-debt calculation.
- [x] Combined all selected totals branches in one server-side `UNION ALL` pipeline. A complete report now needs five `SELECT` commands instead of seven, while accrual-only and payment-only modes retain their existing two- and three-command paths.
- [x] Kept every aggregate, search and source filter in the database. The payment-debt pipeline remains separate and bounded to garages and dates needed by the visible payment candidates.
- [x] Preserved garage, owner and income-type filters, offset/limit pagination, starting debt, canceled-record exclusion, chronological debt calculation, row ordering, exact totals and the existing screen/export contract.
- [x] Added a 401-row regression proving a constant five-command report with exactly 10,100.00 of debt/accruals, 4,000.00 of income, a 401 total row count and a bounded 25-row page.
- [x] Complete verification passed: backend 1661/1661 with 86.86% line and 70.24% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] A transient unchanged user-management workflow test exposed that the 3.2-second success toast was asserted only after waiting for the refreshed table. The assertion order now checks the toast immediately and the refreshed status second; the focused scenario and complete coverage suite pass without increasing or weakening any timeout.
- [x] ESLint, production build, backend formatting, privacy tests, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by the five-command report regression, unified-total and payment-debt source guards, EF integration tests and idempotent migration generation. End-user release note `0.699.0` describes the faster income-report totals. Push and deployment remain intentionally pending because this task did not authorize publication.

## Forty-first fund-change actor projection audit: 2026-07-16

- [x] Rechecked the fund-change report and confirmed that its bounded operation page and author display names were loaded by two sequential commands after the totals command.
- [x] Added the optional author to the bounded page projection with a database left join. PostgreSQL now needs two `SELECT` commands instead of three for a complete report, while the SQLite fallback used by integration tests needs one instead of two.
- [x] Replaced the persistence-entity result contract with compact application query rows, so the report service receives only the fields needed by the screen and exports.
- [x] Preserved period and text filters, canceled-operation exclusion, chronological ordering, offset/limit pagination, missing-author fallback, exact deposit and withdrawal totals and the existing API/export contract.
- [x] Added a 200-operation regression proving one SQLite command, exact 1,000.00 deposit and withdrawal totals, a bounded 25-row page, correct author names and a safe empty-author state. Existing report, XLSX, PDF, audit, architecture and source-boundary scenarios remain covered.
- [x] Complete verification passed: backend 1662/1662 with 86.87% line and 70.23% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by the bounded joined-projection source guard, EF integration tests and idempotent migration generation. End-user release note `0.700.0` describes the faster fund-change report. Push and deployment remain intentionally pending because this task did not authorize publication.

## Forty-second consolidated-garage search bounding audit: 2026-07-16

- [x] Rechecked the consolidated report garage search and confirmed that PostgreSQL materialized every matching garage identity before applying the requested visible-row limit, then aggregated income, accruals and meter readings for the entire materialized set.
- [x] Moved PostgreSQL garage-number and owner-name search into the reusable projected query. Count remains database-side, the visible limit is applied before materialization, and correlated financial aggregates are evaluated only for the requested page.
- [x] Kept the case-insensitive SQLite compatibility path separate because its provider cannot reproduce the production text-search semantics. Its established two-command behavior and calculations remain unchanged.
- [x] Preserved archived-garage exclusion, starting balances, canceled-record exclusion, accounting-period filters, owner display fields, garage-number ordering, exact row count and the income/accrual/meter formulas.
- [x] Added a 200-garage regression proving a constant two-command query, exact total count, a bounded 25-row result and unchanged starting-balance values. Strengthened the production source guard to reject materialization in PostgreSQL search before the common bounded executor.
- [x] Complete verification passed: backend 1663/1663 with 86.87% line and 70.23% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by the bounded projection source guard, the 200-row EF integration regression and idempotent migration generation. End-user release note `0.701.0` describes faster garage search in the consolidated report. Push and deployment remain intentionally pending because this task did not authorize publication.

## Forty-third audit-page actor projection audit: 2026-07-16

- [x] Rechecked the paginated audit journal and confirmed that it executed a count query, a bounded event-page query and a separate user query for author names and email addresses.
- [x] Added a left join from the bounded event page to users and returned the visible actor dictionary with the repository page contract. PostgreSQL now needs two commands instead of three; the SQLite integration path needs one instead of two.
- [x] Preserved every structured filter, descending event order, offset/limit pagination, exact total count, missing-author behavior and the existing DTO/API contract.
- [x] Kept masking in the application service unchanged, so email addresses, credentials, tokens, bank-like numbers, metadata and summaries continue to pass through the established privacy rules.
- [x] Added a 200-event regression proving one SQLite command, an exact total count, a bounded 25-row page, a populated author and a safe empty-author event. Strengthened the production source guard to require the bounded left join and prohibit the former page-level actor lookup.
- [x] The first complete frontend coverage run exposed a slow-path race in the unchanged user-restore workflow: its success-message timer started before the refreshed user page completed and could expire before React displayed the message. Moved the success notification after the awaited refresh without changing its timeout; the complete edit/delete/restore workflow passed three consecutive focused runs.
- [x] The repeated complete verification passed under the same parallel load: backend 1664/1664 with 86.86% line and 70.29% branch coverage; frontend 524/524 with 81.26% statements, 71.93% branches, 77.04% functions and 81.83% lines.
- [x] ESLint, production build, backend formatting, Docker Compose validation, standalone backend publish, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, business rule, permission rule, audit content, privacy rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. PostgreSQL behavior is protected by the joined-page source guard, the 200-row EF integration regression and idempotent migration generation. End-user release note `0.702.0` describes the faster audit journal. Push and deployment remain intentionally pending because this task did not authorize publication.

## Forty-fourth contractor-page request-priority audit: 2026-07-16

- [x] Rechecked the initial contractor screen and confirmed that its bounded visible garage or supplier page competed with large editor-only reference requests for owners, groups, contacts, services, income types and tariffs.
- [x] Prioritized the visible server-paginated table. Editor references now start immediately after the active page finishes instead of sharing the initial network and server window with it.
- [x] Preserved the existing once-per-section reference cache, editor options, page error handling, search, sorting, pagination, archived-record rules and every form/save contract.
- [x] Added a deterministic deferred-response regression proving that the owner reference request does not start while the garage page is pending and starts exactly once after the visible row is rendered. The existing slow-reference regression continues to prove that tables do not wait for editor dictionaries.
- [x] Complete verification passed under parallel load: backend 1664/1664 with 86.86% line and 70.29% branch coverage; frontend 525/525 with 81.28% statements, 72.06% branches, 77.07% functions and 81.84% lines.
- [x] ESLint, production build, bundle budget, backend formatting, Docker Compose validation with disposable validation secrets, standalone backend publish, release JSON, whitespace checks and repeated idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 225.9/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. The optimization is protected by workflow-level request-order tests and the complete backend/frontend suites. End-user release note `0.703.0` describes the faster contractor tables. Push and deployment remain intentionally pending because this task did not authorize publication.

## Forty-fifth report-data request-priority audit: 2026-07-16

- [x] Rechecked the report workbook and confirmed that the first garage, payout, income and fee report request competed with large dictionaries used only for filter suggestions.
- [x] Prioritized calculated report data. Garage, supplier, expense-type and income-type suggestions now start after the active report's first success or failure response instead of sharing its initial network and server window.
- [x] Preserved lazy once-per-dictionary loading, report calculations, exact totals, server pagination, debounced filters, exports, error presentation and retry after leaving and reopening a tab.
- [x] Added a deterministic deferred-response regression proving that garage suggestions do not start while the visible garage report is pending and start exactly once after it settles. Existing lazy-load and failed-reference retry workflows remain green and prove that no duplicate retry is introduced.
- [x] Complete verification passed under parallel load: backend 1664/1664 with 86.86% line and 70.29% branch coverage; frontend 526/526 with 81.32% statements, 72.11% branches, 77.14% functions and 81.86% lines.
- [x] ESLint, production build, bundle budget, backend formatting, Docker Compose validation with disposable validation secrets, standalone backend publish, release JSON, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.7 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 226.1/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, report contract, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. The optimization is protected by workflow-level request-order and retry tests plus the complete backend/frontend suites. End-user release note `0.704.0` describes the faster initial report data. Push and deployment remain intentionally pending because this task did not authorize publication.

## Forty-sixth dictionary-page request-priority audit: 2026-07-16

- [x] Rechecked the dictionaries workspace and confirmed that its bounded visible owner, garage or supplier page competed with an editor-only reference request for up to 500 garages, owners or supplier groups.
- [x] Prioritized the active server-paginated table. The matching editor reference list now starts after the page settles, and changing dictionary subgroups resets that priority before the new requests begin.
- [x] Preserved lazy once-per-section reference loading, create/edit forms, server search, archived-record visibility, pagination, access checks and every save contract.
- [x] Added a deterministic deferred-response regression proving that garage options do not start while the owner page is pending and start only after its visible row is rendered. Existing active-section and bounded-request workflows remain green.
- [x] Complete verification passed under parallel load: backend 1664/1664 with 86.86% line and 70.29% branch coverage; frontend 527/527 with 81.33% statements, 72.11% branches, 77.14% functions and 81.87% lines.
- [x] ESLint, production build, bundle budget, backend formatting, Docker Compose validation with disposable validation secrets, standalone backend publish, release JSON, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.6 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 226.0/260.0 KiB gzip.

No schema, financial formula, business rule, permission rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. The optimization is protected by workflow-level request-order tests and the complete backend/frontend suites. End-user release note `0.705.0` describes the faster dictionary tables. Push and deployment remain intentionally pending because this task did not authorize publication.

## Forty-seventh user-page request-priority audit: 2026-07-16

- [x] Rechecked user management and confirmed that its bounded visible user page waited for the independent role dictionary before leaving the loading state.
- [x] Prioritized the user page and ended the table loading state as soon as that request settles. Roles now load afterward for create/edit and permission-management forms.
- [x] Preserved role-request caching, retry after a temporary role failure, server pagination and search, create/edit/deactivate/restore workflows and every backend permission rule.
- [x] Added a deterministic deferred-response regression proving that the role request does not start while the visible user page is pending and starts exactly once after the user row is rendered. Existing role-cache and retry scenarios remain green.
- [x] Complete verification passed under parallel load: backend 1664/1664 with 86.86% line and 70.29% branch coverage; frontend 528/528 with 81.34% statements, 72.10% branches, 77.14% functions and 81.88% lines.
- [x] ESLint, production build, bundle budget, backend formatting, Docker Compose validation with disposable validation secrets, standalone backend publish, release JSON, whitespace checks and idempotent migration SQL generation passed. The production bundle remains within budget: main JavaScript 75.6 KiB gzip, main CSS 19.1 KiB gzip and total JavaScript/CSS 226.1/260.0 KiB gzip.

No schema, role, permission, business rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. The optimization is protected by workflow-level request-order, cache and retry tests plus the complete backend/frontend suites. End-user release note `0.706.0` describes the faster user table. Push and deployment remain intentionally pending because this task did not authorize publication.
