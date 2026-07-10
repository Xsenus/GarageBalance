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

On the current development machine the final performance verification cannot be accepted as complete because the environment has no local PostgreSQL, no `psql`, no Docker, and no realistic customer data: `postgresTcp=False`, `psql=False`, `docker=False`. Until these are available, agents may run automated guards, build checks, bundle budget, SQL generation, JSON validation and encoding checks, but must keep the live final performance gate blocked.
