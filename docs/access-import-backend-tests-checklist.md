# Access Import Backend Tests Checklist

Этот checklist фиксирует, какие backend-тесты импорта Access уже есть и что блокирует закрытие roadmap-пункта как `[x]`.
Сейчас покрыта инфраструктура вокруг импорта, но тесты фактического mapping/transfer нельзя завершить без live Access reader, final field-level mapping and transfer implementation.

## Что Уже Покрыто

- [x] `ImportServiceTests` покрывает dry-run, invalid extension, duplicate content warning, run log, report export audit and run history limits.
- [x] `ImportServiceTests` покрывает apply request с обязательной причиной and backup confirmation.
- [x] `ImportServiceTests` покрывает apply cancel and rollback request states without touching production data.
- [x] `ImportServiceTests` покрывает created-records list for a run with server-side limit.
- [x] `ImportControllerTests` покрывает dry-run, report export, run list, run log, apply, apply cancel, rollback, quarantine and created-record endpoints.
- [x] `ImportFingerprintServiceTests` покрывает idempotency by external id, row hash fallback, validation and audit without duplicate audit.
- [x] `ImportQuarantineServiceTests` покрывает quarantine registration, validation, server-side limit, resolve flow and audit without raw snapshot.
- [x] Roadmap guards удерживают blocked/decision gates for Access reader, working copy, schema inventory, baseline, mapping, transfer, idempotency, quarantine and reconciliation report.

## Что Блокирует Полное Закрытие

- [ ] Final field-level Access -> PostgreSQL mapping is available from private schema inventory.
- [ ] Live Access reader or approved conversion returns representative rows for owners, garages, dictionaries, payments, payouts, accruals, meter readings and opening balances.
- [ ] Transfer service exists and can be tested outside controllers.
- [ ] Mapping tests validate required fields, nullable fields, enum/type conversions, money/date parsing and reference resolution.
- [ ] Duplicate tests cover external id duplicates, row hash duplicates and target unique-index conflicts.
- [ ] Malformed-row tests cover missing owner, duplicate garage, invalid amount, invalid date, unknown payment type, meter rollback and unsupported legacy rule.
- [ ] Repeat-run tests prove idempotent second import does not create duplicate target records, duplicate created-record rows or duplicate audit creation events.
- [ ] Transaction tests prove rollback behavior for critical batch failures and partial quarantine scenarios.
- [ ] PostgreSQL integration tests verify indexes, constraints, pagination and report queries on imported-like data.
- [ ] Privacy tests verify raw Access rows, personal data and payment details are not written to public DTOs, audit, roadmap history, release notes or test fixtures.

## Required Test Groups Before `[x]`

- [ ] Access reader/conversion contract tests with sanitized fixtures.
- [ ] Owner and garage mapping tests.
- [ ] Income/expense type and tariff mapping tests.
- [ ] Historical payment and payout mapping tests.
- [ ] Accrual and meter reading mapping tests.
- [ ] Opening balance mapping tests.
- [ ] Fingerprint and duplicate transfer tests.
- [ ] Quarantine/malformed transfer tests.
- [ ] Created-record registry tests.
- [ ] Reconciliation report tests.
- [ ] Transaction and rollback/status tracking tests.
- [ ] Controller authorization tests for transfer/reconciliation endpoints.

## Когда Roadmap-Пункт Можно Закрыть

- [ ] Guard `AccessImportBackendTestsRoadmapItemRemainsBlockedUntilMappingTransferAndPostgresCoverageExist` can be removed only when live mapping/transfer tests exist.
- [ ] Backend tests cover mapping, duplicates, malformed rows, idempotent repeat run, quarantine, created records, reconciliation, transactions, rollback/status tracking and PostgreSQL-specific behavior.
