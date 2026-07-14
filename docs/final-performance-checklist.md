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
