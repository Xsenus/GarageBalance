# Access Import Idempotency Checklist

Этот checklist фиксирует условия, при которых идемпотентность фактического переноса Access можно закрыть как `[x]`.
Сейчас backend-реестр fingerprint уже готов, но end-to-end гарантия "повторный импорт не создает дубли" остается заблокированной до live transfer flow.

## Что Уже Готово

- [x] Таблица `access_import_row_fingerprints` создана через EF migration.
- [x] `AccessImportRowFingerprint.FingerprintKey` строится из `SourceSystem + EntityType + ExternalId`, а при отсутствии external id - из `SourceSystem + EntityType + RowHash`.
- [x] `IImportFingerprintService.RegisterAsync` возвращает `Created=false` для повторной строки и не создает второй fingerprint.
- [x] `IImportFingerprintService.ExistsAsync` проверяет наличие fingerprint по тому же deterministic key.
- [x] Fingerprint хранит `AccessImportRunId`, `TargetEntityType`, `TargetEntityId`, `CreatedAtUtc` и `CreatedByUserId`.
- [x] Регистрация нового fingerprint пишет audit-событие `import.row_fingerprint_registered`.
- [x] Backend tests покрывают создание fingerprint, повтор по external id, повтор по row hash без external id, разные row hash и validation.
- [x] ERD и roadmap описывают назначение fingerprint registry.

## Что Блокирует Полное Закрытие

- [ ] Live Access reader или утвержденная конвертация возвращает реальные строки с source table/key metadata.
- [ ] Есть field-level mapping, по которому можно стабильно построить canonical row hash.
- [ ] Фактический transfer flow вызывает `IImportFingerprintService` перед созданием каждой target-записи.
- [ ] Для каждой переносимой сущности определен `EntityType`: owners, garages, income types, expense types, tariffs, historical payments, payouts, meter readings and opening balances.
- [ ] Для строк с Access primary key используется stable external id; для строк без ключа используется canonical SHA-256 row hash.
- [ ] Повторный запуск того же файла не создает новые target records и не пишет повторный audit создания.
- [ ] Повторный запуск другого run связывает найденный fingerprint с уже созданным target record через `access_import_created_records`.
- [ ] Если external id совпал, но normalized row hash изменился, применяется documented conflict policy: skip, quarantine, update or decision-required.
- [ ] PostgreSQL unique constraint violations в target tables не являются нормальным механизмом идемпотентности; они должны быть fallback safety, а не основной flow.
- [ ] Ошибочные или неоднозначные duplicate/conflict cases уходят в `access_import_quarantine_items` с безопасным snapshot/hash.
- [ ] Transaction strategy документирует порядок: fingerprint registration, target create/update, created-record registry and rollback behavior.

## Required Tests

- [ ] Backend mapping tests validate deterministic external id and row hash generation for each imported entity type.
- [ ] Backend transfer tests prove that a second run of the same Access data skips already imported rows.
- [ ] Backend transfer tests prove that duplicates without external id are detected by canonical row hash.
- [ ] Backend transfer tests prove that changed data with the same external id follows the conflict policy.
- [ ] Backend transaction tests prove rollback does not leave orphan target records or misleading created-record rows.
- [ ] PostgreSQL/integration tests cover unique indexes together with fingerprint pre-checks.
- [ ] Quarantine tests cover duplicate/conflict rows with safe reason codes.
- [ ] Reconciliation report includes imported, skipped-as-duplicate, quarantined and decision-required counts.

## Acceptance Evidence

- [ ] First transfer run on private Access working copy creates expected records.
- [ ] Second transfer run on the same working copy creates zero duplicates.
- [ ] Reconciliation compares first and second run counts.
- [ ] Audit history contains creation events only for the first actual create.
- [ ] Customer-facing report explains skipped duplicates without exposing raw personal, payment or Access rows.

## Когда Roadmap-Пункт Можно Закрыть

- [ ] Guard `AccessImportIdempotencyRoadmapItemRemainsBlockedUntilTransferUsesFingerprintRegistry` can be removed only after live transfer uses fingerprint registry before creating target records.
- [ ] Actual transfer flow, backend tests, PostgreSQL checks and reconciliation evidence confirm that repeat import does not create duplicates.
