# Audit Contract Verification

Этот документ фиксирует evidence для закрытия единого audit-контракта текущих mutating workflows.

## Единый Writer

- [x] Все текущие mutating application-сервисы используют `IAuditEventWriter` / `AuditEventWriter`.
- [x] Production-код не создает `AuditEvent` и не вызывает `AuditEvents.Add` напрямую вне writer.
- [x] Read-only, status и token-only сервисы не создают ложные audit-события.

## Контракт События

- [x] Событие хранит actor, UTC-время, section, action kind, entity type/id и видимое имя объекта.
- [x] Update-события поддерживают structured `OldValues`/`NewValues`, field labels и no-op suppression.
- [x] Archive/delete/cancel требуют reason; обычные изменения могут хранить comment.
- [x] Metadata и summary маскируют чувствительные значения.
- [x] Связанный контекст поддерживает гараж, расчетный месяц, контрагента и документ.
- [x] Финансовые события сохраняют сумму/денежный эффект и контекст начисления или операции.

## Текущий Scope

- [x] Auth, users, roles/permissions и password workflows.
- [x] Dictionaries, tariffs, services, fees and irregular payments.
- [x] Finance, meters, accruals, payments, payouts, balances and funds.
- [x] Access dry-run, quarantine, fingerprints, report, apply/cancel/rollback requests and created-record registry context.
- [x] Protected integration settings, 1C preview/start/retry requests and receipt print/cancel/reprint actions.
- [x] Reports, form states and app release management.
- [x] Central audit API/UI supports filters, pagination, detail, CSV/XLSX export and masking under `audit.read`.

## Автоматические Гарантии

- [x] `DatabaseMigrationPolicyTests.ProductionBackendCode_CreatesAuditEventsOnlyThroughAuditEventWriter` forbids manual writes.
- [x] `AuditEventWriterTests` covers diff, metadata masking, related fields, no-op and required reason.
- [x] `AuditChangeDiffBuilderTests`, service/controller tests and `AuditEventCoverageDocumentationTests` cover current actions.
- [x] `ProjectWideRoadmapStatusTests` covers update diff, no-op, financial context and this current-scope contract.

## Future Integrations

- [x] Реальные Access transfer/rollback, 1C exchange/conflict resolution и device/fiscal printing еще не реализованы; их `[~]` audit-пункты остаются открытыми и должны расширять event coverage одновременно с adapters/workflows.

## Release Notes

- [x] Новая запись "Что нового" не нужна: production audit behavior и пользовательский интерфейс в этом evidence/test-срезе не меняются.
