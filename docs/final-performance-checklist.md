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

No schema, financial formula, business rule, production data, cleanup policy or deployment configuration changed in this pass. Local PostgreSQL project credentials remain unavailable, so no local data mutation was attempted. End-user release note `0.679.0` describes the faster payment-screen opening and safe retry behavior. Push and deployment remain intentionally pending because this task did not authorize publication.
