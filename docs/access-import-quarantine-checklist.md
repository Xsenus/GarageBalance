# Access Import Quarantine Checklist

Этот checklist фиксирует условия, при которых `quarantine/error bucket` для фактического переноса Access можно закрыть как `[x]`.
Сейчас backend, API и UI для карантина готовы, но автоматическое попадание проблемных строк в карантин остается заблокированным до live transfer flow.

## Что Уже Готово

- [x] Таблица `access_import_quarantine_items` создана через EF migration.
- [x] `AccessImportQuarantineItem` хранит source system, entity type, external id, row hash, reason code/message, severity, status and resolution fields.
- [x] `IImportQuarantineService.GetOpenItemsAsync` возвращает открытые строки с фильтром по run id и server-side limit.
- [x] `IImportQuarantineService.RegisterAsync` валидирует source/entity/hash/reason/severity and JSON snapshot.
- [x] `IImportQuarantineService.ResolveAsync` закрывает строку карантина и требует безопасный resolution comment.
- [x] DTO не возвращает raw `RowSnapshotJson`.
- [x] Audit events `import.quarantine_registered` and `import.quarantine_resolved` пишутся без raw snapshot.
- [x] `ImportController` отдает endpoints просмотра и закрытия карантина.
- [x] Frontend import panel показывает открытый карантин, лимитирует список и позволяет закрывать строки.
- [x] Backend/frontend tests покрывают регистрацию, валидацию, сортировку, лимит, просмотр, закрытие и not-found cases.

## Что Блокирует Полное Закрытие

- [ ] Live Access reader или утвержденная конвертация возвращает реальные malformed/conflict rows.
- [ ] Field-level mapping определяет required fields, nullable rules, reference resolution, enum mapping and money/date parsing rules.
- [ ] Фактический transfer flow вызывает `IImportQuarantineService.RegisterAsync` для каждой строки, которую нельзя перенести автоматически.
- [ ] Для каждой entity type определены reason codes: missing owner, duplicate garage, invalid amount, invalid date, unknown payment type, meter rollback, supplier mismatch, unsupported legacy rule and decision required.
- [ ] safe snapshot policy определяет безопасный minimized JSON без passport, phone, address, full bank/payment details and raw Access row dumps.
- [ ] Severity policy разделяет `error` и `warning`: какие строки блокируют batch/run, а какие допускают перенос остальных данных.
- [ ] Idempotency policy не создает повторные quarantine items для той же source row без documented re-open/update behavior.
- [ ] Created-record registry and reconciliation report учитывают quarantined/skipped rows separately from imported rows.
- [ ] Resolve flow после ручного решения либо запускает повторный transfer, либо фиксирует manual correction/audit path.

## Required Tests

- [ ] Backend transfer tests prove malformed rows are registered in quarantine instead of creating partial target records.
- [ ] Backend transfer tests prove duplicate/conflict rows include deterministic row hash and reason code.
- [ ] Backend tests prove raw sensitive Access fields are not exposed in DTO, audit, release notes or roadmap history.
- [ ] Backend transaction tests prove critical errors rollback target changes while preserving a safe run/quarantine report according to the documented strategy.
- [ ] Frontend workflow tests cover transfer result with imported, skipped, quarantined and decision-required counts.
- [ ] Frontend workflow tests cover resolving a quarantined row and refreshing the import panel.
- [ ] PostgreSQL checks cover indexes for run, status, source/entity and row hash on realistic quarantine volume.

## Acceptance Evidence

- [ ] Dry-run or transfer on private Access working copy produces expected quarantine counts.
- [ ] Sample quarantined rows can be resolved by staff without exposing sensitive raw data.
- [ ] Reconciliation report separates imported, quarantined, skipped duplicates and decision-required rows.
- [ ] Second transfer run after resolving known issues does not duplicate quarantine rows or target records.
- [ ] Customer confirms reason codes and resolution workflow are understandable for daily import cleanup.

## Когда Roadmap-Пункт Можно Закрыть

- [ ] Guard `AccessImportQuarantineRoadmapItemRemainsBlockedUntilTransferRegistersMalformedRows` can be removed only after live transfer sends real malformed/conflict rows to quarantine.
- [ ] Backend, frontend, PostgreSQL and reconciliation evidence confirm that problematic Access rows are observable, safe and resolvable.
