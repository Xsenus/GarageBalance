# Access Import Reconciliation Checklist

Этот checklist фиксирует условия, при которых сверочный отчет после фактического переноса Access можно закрыть как `[x]`.
Сейчас есть run history, created-records registry, quarantine and fingerprint infrastructure, но полноценный reconciliation report невозможен без live transfer and pre-import baseline.

## Что Уже Готово

- [x] `access_import_runs` хранит run status, counters, report JSON and timestamps.
- [x] `access_import_run_log_entries` хранит пошаговый журнал import run.
- [x] `access_import_created_records` хранит target records, created from future transfer.
- [x] `AccessImportCreatedRecordDto` and `GetAccessImportCreatedRecordsAsync` возвращают созданные записи с server-side limit.
- [x] Frontend import panel показывает вкладку созданных записей после выбранного run.
- [x] `access_import_row_fingerprints` готов для duplicate/skipped accounting.
- [x] `access_import_quarantine_items` готов для quarantined/problem rows.
- [x] Dry-run JSON report export уже идет через audited endpoint.

## Что Блокирует Полное Закрытие

- [ ] Есть приватная Access working copy and pre-import baseline with counts/checksums.
- [ ] Live transfer created real target records in owners, garages, dictionaries, payments, payouts, accruals, meter readings and balances.
- [ ] Created-record registry заполняется для каждого target record during transfer.
- [ ] Quarantine flow records malformed/conflict/decision-required rows during transfer.
- [ ] Idempotency flow records skipped duplicates during repeated transfer.
- [ ] Reconciliation report compares Access baseline counts with PostgreSQL imported counts.
- [ ] Report compares money totals, opening balances, historical payments, payouts, meter readings and final garage/supplier balances.
- [ ] Report separates imported, skipped duplicate, quarantined, failed, rollback and decision-required rows.
- [ ] Report excludes raw personal, payment, address, phone, passport, bank and raw Access row data.
- [ ] Report can be downloaded and audited after transfer without leaking raw sensitive rows.
- [ ] Customer can review report and mark acceptance findings.

## Required Report Sections

- [ ] Source metadata: sanitized Access copy hash, file size, run id, start/end times and operator.
- [ ] Baseline summary: table counts and safe aggregate checksums from Access.
- [ ] Target summary: imported counts by entity type and target table.
- [ ] Financial summary: totals by income, payout, accrual, supplier accrual and opening balance.
- [ ] Meter summary: readings count, consumption checks and suspicious meter rollback count.
- [ ] Problem summary: quarantine/error/decision-required rows grouped by reason code and severity.
- [ ] Duplicate summary: fingerprints skipped during second run.
- [ ] Created-record summary: target entity types, target ids and creation timestamps.
- [ ] Acceptance summary: unresolved blockers and manual customer decisions.

## Required Tests

- [ ] Backend reconciliation tests compare baseline counts with imported target counts.
- [ ] Backend reconciliation tests compare decimal financial totals with explicit rounding.
- [ ] Backend reconciliation tests include skipped duplicate and quarantine counts.
- [ ] Backend reconciliation tests ensure raw sensitive fields are not present in report DTO/JSON/audit.
- [ ] Backend controller tests cover report download success, missing run and permission denial.
- [ ] Frontend workflow tests show reconciliation summary, empty state, loading state, error state and download action.
- [ ] PostgreSQL checks cover report queries on realistic imported data volume.

## Acceptance Evidence

- [ ] Report generated on private Access working copy after successful transfer.
- [ ] Report reviewed against pre-import baseline.
- [ ] Report reviewed with customer without exposing raw sensitive rows in Git, roadmap or release notes.
- [ ] Unresolved quarantine/decision-required rows are listed and assigned for cleanup.
- [ ] Final acceptance entry records report id, run id, counts, totals and remaining risks.

## Когда Roadmap-Пункт Можно Закрыть

- [ ] Guard `AccessImportReconciliationReportRoadmapItemRemainsBlockedUntilLiveTransferBaselineAndReportExist` can be removed only after live transfer, baseline comparison and safe report download exist.
- [ ] Backend, frontend, PostgreSQL and acceptance evidence confirm that imported Access data reconciles with the source baseline.
