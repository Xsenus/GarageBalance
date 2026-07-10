# 1C Fresh Sync Design

Документ фиксирует целевую модель синхронизации 1C Fresh до подключения реального тестового контура. Он не содержит доступов, токенов, персональных данных, банковских реквизитов или реальных документов кооператива.

## Scope

- Защищенные доступы уже хранятся через `IntegrationSecretSetting` и `IIntegrationSecretSettingsService`.
- Ручной запуск уже проходит через `POST /api/integrations/one-c-fresh/sync-runs`, повтор через `/retry`.
- Реальный адаптер 1C Fresh подключается заменой `IOneCFreshSyncAdapter`; application-сервис не должен знать детали HTTP/API 1C.
- Направление обмена, список документов и тестовый контур остаются `[decision]` пунктами roadmap.

## Domain Model

Будущая физическая модель журнала обмена должна быть отдельной от audit, но связанной с ним:

- `IntegrationSyncRun`
  - `Id`
  - `Provider`: `OneCFresh`
  - `Direction`: `export`, `import`, `bidirectional`
  - `Mode`: `preview`, `apply`, `retry`
  - `Status`
  - `RequestedByUserId`
  - `RequestedAtUtc`
  - `StartedAtUtc`
  - `CompletedAtUtc`
  - `CorrelationId`
  - `ExternalRunId`
  - `Comment`
  - `SummaryJson`
  - `AuditEventId`
- `IntegrationSyncItem`
  - `Id`
  - `RunId`
  - `ObjectType`: `counterparty`, `payment`, `accrual`, `invoice`, `receipt`, `act`, `report`
  - `LocalEntityType`
  - `LocalEntityId`
  - `ExternalEntityId`
  - `Operation`: `create`, `update`, `skip`, `delete`, `match`
  - `Status`
  - `ConflictKind`
  - `ValidationCode`
  - `Message`
  - `PayloadHash`
- `IntegrationSyncConflict`
  - `Id`
  - `RunId`
  - `ItemId`
  - `ConflictKind`: `mapping_missing`, `version_mismatch`, `duplicate_external`, `duplicate_local`, `validation_failed`, `permission_denied`
  - `Resolution`: `pending`, `use_local`, `use_external`, `skip`, `manual_mapping`, `cancel_run`
  - `ResolvedByUserId`
  - `ResolvedAtUtc`
  - `ResolutionComment`

## Statuses

Run statuses:

- `draft_preview`: preview requested, not sent to 1C.
- `preview_ready`: preview completed and waiting for user confirmation.
- `queued`: apply/retry accepted and waiting for adapter/background worker.
- `running`: adapter started exchange.
- `succeeded`: all required items completed.
- `succeeded_with_warnings`: exchange completed with skipped or warning items.
- `failed`: adapter or validation failure stopped the run.
- `conflict`: user resolution is required before apply/retry.
- `cancelled`: user cancelled before completion.

Item statuses:

- `planned`: item is included in preview.
- `validated`: item passed validation.
- `sent`: item was sent to 1C.
- `received`: item was received from 1C.
- `applied`: local or external change was applied.
- `skipped`: item was intentionally skipped.
- `warning`: item completed with non-blocking issue.
- `failed`: item failed.
- `conflict`: item requires resolution.

Current adapter statuses `pending_adapter`, `started` and adapter-specific failures must be mapped into these run statuses when the physical journal is added.

## Preview Mode

Preview is mandatory before applying changes to 1C Fresh or importing changes from it.

Preview response must include:

- run id;
- direction and mode;
- period/filter summary;
- counts by object type and operation;
- warnings;
- conflicts;
- items with local object labels safe for display;
- payload hashes instead of raw exported documents where possible.

Preview must not:

- store plaintext tokens;
- expose raw protected settings;
- export real documents before user confirmation;
- write final finance/audit changes except preview audit event.

## Error, Retry And Conflict Rules

- Adapter failures are stored as safe error codes and messages without credentials or raw payloads.
- Retry creates a new `IntegrationSyncRun` linked to the previous run instead of mutating history.
- Repeated retry keeps the original preview snapshot hash unless the user explicitly creates a new preview.
- Conflicts block apply until resolved or skipped.
- Resolution requires a user comment for destructive or money-affecting decisions.
- Every apply, retry, conflict resolution and cancellation writes an audit event with `Section=integrations`.

## Permissions

- Viewing integration status requires `settings.manage` or an explicit future `integrations.read`.
- Saving protected settings requires `settings.manage`.
- Starting preview/apply/retry requires `import.run` until a separate integration permission is introduced.
- Conflict resolution requires the same permission as apply plus audit visibility for related finance objects.

## Journal UI

The settings page should show a compact latest status. A future journal view should show:

- run list with status, direction, mode, requester, date, counts and external id;
- item list with object type, operation, status and safe message;
- conflict list with resolution controls;
- retry button only for failed/conflict/cancelled runs where retry is safe;
- export of journal without secrets or raw protected payloads.

## Acceptance Criteria

- No plaintext token or connection secret is stored in run, item, conflict, audit, logs or export.
- Preview can be reviewed before apply.
- Apply and retry are auditable.
- Conflicts cannot be silently overwritten.
- Statuses are stable enough for backend tests, React tests and future PostgreSQL migrations.
- Real 1C Fresh adapter can be introduced behind `IOneCFreshSyncAdapter` without rewriting controller contracts.
