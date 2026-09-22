# Техническое задание: надёжное storage и backup/restore GarageBalance

Версия: 1.0 от 22.09.2026  
Baseline: `master` / `e5309e8ae851d6a5f4ce4960e01066ded651e8fb`  
Статус: планирование; реализация не начата.

## 1. Scope и требования

| ID | Обязательное требование | Источник |
|---|---|---|
| `REQ-001` | Сохранить current local-only backup flow, filenames, API/UI и old config без обязательных новых credentials. | Владелец + `EVID-004..015` |
| `REQ-002` | Создать независимую off-site защиту критичных DB backup artifacts; не переносить все transient files. | `RISK-001`, `DEC-001` |
| `REQ-003` | Разделить connections, destinations, roles, pools, policies и tiers; поддержать N destinations без зависимости business layer от vendor. | Текущий запрос |
| `REQ-004` | Ввести logical object, committed generation, physical replicas, actual locators, per-copy state и durable jobs. | `RISK-003`, `RISK-020` |
| `REQ-005` | Реализовать durable async replication, partial success, UNKNOWN reconciliation, idempotency, restart/resume и честный protection ACK. | Текущий запрос; `RISK-012`, `RISK-014` |
| `REQ-006` | Реализовать version-aware read/restore-source failover только между пригодными verified copies; сохранить proxy и optional direct GET. | Текущий запрос; `RISK-013` |
| `REQ-007` | Реализовать tombstone, retention-aware per-replica deletion, reconciliation, repair, catch-up и safe failback без resurrection/stale overwrite. | Текущий запрос; `RISK-008`, `RISK-015` |
| `REQ-008` | Сделать backup pipeline и restore доказуемыми: checksum/manifest, freshness, independent copies, scheduled isolated restore and app smoke. | `RISK-002..004` |
| `REQ-009` | Включить Data Protection keys/config в отдельный encrypted recovery process; каталог/ключевой путь доступны при потере production DB. | `RISK-005`, `RISK-020` |
| `REQ-010` | Обеспечить least privilege, secret hygiene, private encryption defaults, endpoint validation, tenant/policy isolation and audit. | `RISK-009`, `RISK-016` |
| `REQ-011` | Добавить status/API/UI/metrics/alerts для protection, lag, stale backup, failure, recovery and restore drill. | `RISK-010` |
| `REQ-012` | Не ломать local filesystem, Access import, diagnostics, 1C Fresh, receipt printing, reports и release catalog; подтвердить non-regression. | Владелец; `EVID-018..027`, `RISK-017` |
| `REQ-013` | Удалять orphan Access staging по безопасному TTL/quarantine, не трогая active runs. | `RISK-006` |
| `REQ-014` | Дать operator CLI для inventory/plan/dry-run/copy/verify/diff/delta/resume/repair/status/report/cutover/rollback checks. | Текущий запрос; `RISK-018` |
| `REQ-015` | Выполнять rollout/backfill/rollback без потери old и B-only data; failover включать только после coverage proof. | `RISK-018` |
| `REQ-016` | Ограничить memory/disk/network/retry/concurrency, не допускать starvation и retry storm. | `RISK-019` |

## 2. Non-goals, deferred и not required

- Не переносить PostgreSQL business tables в object storage.
- Не создавать permanent storage для generated reports, diagnostic ZIP или Access source.
- Не добавлять Google Drive/Yandex Disk/Mail/Dropbox/OneDrive/WebDAV/SFTP adapters: таких contracts нет, бизнес-требование отсутствует.
- Не делать browser direct upload DB backup; dump создаётся сервером.
- Не внедрять Kafka/RabbitMQ/distributed consensus: PostgreSQL job table + hosted worker достаточны на текущем масштабе.
- Не внедрять PITR/WAL chain в первой версии. Это отдельное улучшение после подтверждения RPO/масштаба.
- Cold/archive lifecycle и Object Lock — `DEFERRED` до выбора provider, официальной проверки capabilities/cost/RTO и owner approval.
- CDN/public objects — `NOT REQUIRED`.
- Автоматический destructive production restore запрещён.

## 3. Архитектурные решения

| ID | Решение | Причина / альтернатива / условие пересмотра |
|---|---|---|
| `DEC-001` | Scope — DB backup и encrypted recovery artifacts; transient flows остаются отдельными. | Минимальная защита critical data без overengineering. Пересмотреть при появлении persistent user files. |
| `DEC-002` | `LOCAL_VERIFIED_ASYNC_OFFSITE`: local hot/staging + async remote copies. | Не связывает создание dump с latency всех vendors; protection lag виден. Sync quorum отклонён сейчас. |
| `DEC-003` | PostgreSQL catalog + durable job rows + DB lease/`SKIP LOCKED`; no new broker. | Использует current stack, обеспечивает restart/multi-instance. Пересмотреть при доказанном масштабе/HA DB. |
| `DEC-004` | Immutable physical object keys, logical object/generation/tombstone, deterministic operation ID. | Исключает stale overwrite/resurrection; mutable overwrite rejected. |
| `DEC-005` | Stable authorized logical download endpoint; proxy baseline, optional signed GET capability. | Preserves current behavior; direct URL cannot fail over in-flight. |
| `DEC-006` | Workers initially hosted in API but workloads/leases/resource budgets isolated. | Current deployment has hosted services; separate worker only if measured contention. |
| `DEC-007` | Migration/reconciliation operator CLI in new project, not public HTTP. | Safer permissions, checkpoints and reports. |
| `DEC-008` | Key/config recovery bundle encrypted independently and routed through separate pool/policy. | Dump alone cannot decrypt protected integration values; plaintext bundle forbidden. |
| `DEC-009` | Proposed protection: required 2 independent copies total including ≥1 off-site; desired 3 total (local + two off-site). | Provides host-loss protection while allowing async creation. Owner may raise/lower only explicitly; UI distinguishes local creation and protection. |
| `DEC-010` | No global provider health switch. Eligibility is operation/data-class/capability + actual replica generation. | A healthy endpoint may lack object; 404 is object-scoped. |

## 4. Target pools, policies and tiers

| Policy | Destinations | Write ACK | Read | Protection | Retention/tier |
|---|---|---|---|---|---|
| `DatabaseBackupPolicy` | `local-hot`, `offsite-a`, `offsite-b` | Valid local dump => `CreatedLocal`; `Protected` only when `DEC-009` met | current verified local, then verified remote by priority | durable debt until desired copies | local/hot first; remote hot; cold deferred |
| `RecoverySecretsPolicy` | encrypted bundle on two independent protected destinations | success only after required encrypted copies | restore-tool/operator only | encryption key in independent secret source | hot; archive gated |
| `ImportStagingPolicy` | current local queue only | DB run + staged file | owning worker only | no replicas | TTL/quarantine cleanup |
| `DiagnosticsPolicy` | current local logs | best effort | admin ZIP | no DR promise | 14 days |

## 5. Exact code changes

### Existing files to modify

- `backend/GarageBalance.Api/Application/DatabaseBackups/DatabaseBackupContracts.cs`
  - retain existing DTO fields for compatibility;
  - remove absolute `Directory` from normal client DTO or replace with safe location label;
  - add `ProtectionState`, required/available/desired copies, checksum, last verified, replica summaries;
  - keep existing service methods or add additive interfaces rather than breaking routes.
- `backend/GarageBalance.Api/Infrastructure/DatabaseBackups/PostgresDatabaseBackupService.cs`
  - preserve temp/non-empty/TOC/rename sequence;
  - stream SHA-256 after finalization, write sidecar manifest atomically;
  - register logical object/replicas/jobs transactionally;
  - make retention protection-aware and stop deleting unprotected source;
  - use cross-instance DB lease for creation/retention;
  - recover file-without-metadata and metadata-without-file through reconciliation.
- `backend/GarageBalance.Api/Application/DatabaseBackups/DatabaseBackupAutomation.cs`
  - add catch-up outside missed window under explicit maximum delay;
  - separate dump creation from remote delivery;
  - expose stale/protection state without stopping unrelated API.
- `backend/GarageBalance.Api/Infrastructure/Data/DatabaseStartupHostedService.cs`
  - require locally verified pre-migration dump as today;
  - optionally require configured protection level before high-risk migration/deploy, operator-configurable and time-bounded;
  - never delete source on upload failure.
- `backend/GarageBalance.Api/Infrastructure/Data/GarageBalanceDbContext.cs`
  - add DbSets/mappings/indexes/concurrency for storage catalog/jobs.
- `backend/GarageBalance.Api/Program.cs`
  - bind/validate storage options, registry, policies and endpoint allowlist;
  - register providers/router/repository/workers after migrations;
  - default missing `Storage` section to legacy local `SINGLE` mode.
- `backend/GarageBalance.Api/Controllers/SettingsController.cs`
  - keep existing routes and proxy download semantics;
  - add protection/replica state, manual retry/verify, optional direct link;
  - use new backup-specific permissions;
  - map degraded/pending/not-found/corrupt/all-failed distinctly.
- `backend/GarageBalance.Api/Domain/Security/SystemPermissions.cs` and permission seed migration
  - add `backups.read`, `backups.create`, `backups.download`, `backups.delete`, `backups.repair`;
  - map administrator role compatibly; do not silently grant ordinary user managers more access.
- `backend/GarageBalance.Api/Application/Import/ImportDryRunQueue.cs`
  - periodic/startup sweeper with DB status cross-check, TTL/quarantine, audit/metrics;
  - never remove queued/processing or recently ambiguous files.
- `frontend/src/services/settingsApi.ts`
  - additive types for protection/replicas/retry/direct-link; preserve proxy fallback.
- `frontend/src/features/settings/PasswordPanel.tsx`
  - show `Локально создана`, `Защищена N/M`, `Ожидает копирования`, `Деградация`, `Ошибка`, last verify;
  - do not expose server paths, buckets, credentials or signed URLs after use;
  - retry logical endpoint after direct download failure; permission-aware actions.
- `.env.example`, `distribution/docker/.env.example`, `docker-compose.yml`, `distribution/docker/docker-compose.yml`
  - safe non-secret policy/options; secret references/environment only;
  - no default fake credentials/provider enablement.
- `infrastructure/scripts/vps-apply-release.sh`
  - respect protection gate before cleanup/high-risk cutover;
  - invoke/report isolated verifier, never auto-restore production.
- `infrastructure/scripts/backup-postgres.ps1`, `restore-postgres.ps1`, `register-local-backup-task.ps1`
  - align checksum/manifest/freshness/verification behavior without breaking current parameters.
- `docs/postgres-backup-restore.md`, `docs/docker-install-update-guide.md`, `docs/docker-windows-lan-guide.md`, `docs/vps-deployment-checklist.md`, `docs/security-data-protection.md`
  - update actual runbooks after implementation, no secrets.
- `backend/GarageBalance.Api/AppReleases/releases.json`
  - user-facing release note only when feature is implemented, not during planning.

### NEW / PROPOSED paths and symbols

- `backend/GarageBalance.Api/Domain/Storage/StorageObject.cs`
- `backend/GarageBalance.Api/Domain/Storage/StorageObjectReplica.cs`
- `backend/GarageBalance.Api/Domain/Storage/StorageTransferJob.cs`
- `backend/GarageBalance.Api/Domain/Storage/StorageMaintenanceRun.cs`
- `backend/GarageBalance.Api/Application/Storage/IStorageProvider.cs`
- `backend/GarageBalance.Api/Application/Storage/IStorageCatalog.cs`
- `backend/GarageBalance.Api/Application/Storage/StorageCapabilities.cs`
- `backend/GarageBalance.Api/Application/Storage/StoragePolicies.cs`
- `backend/GarageBalance.Api/Application/Storage/StorageRouter.cs`
- `backend/GarageBalance.Api/Application/Storage/StorageReplicationWorker.cs`
- `backend/GarageBalance.Api/Application/Storage/StorageReconciliationService.cs`
- `backend/GarageBalance.Api/Infrastructure/Storage/LocalFileStorageProvider.cs`
- `backend/GarageBalance.Api/Infrastructure/Storage/S3CompatibleStorageProvider.cs`
- `backend/GarageBalance.Api/Infrastructure/Data/EfStorageCatalog.cs`
- `backend/GarageBalance.Api/Infrastructure/Data/Migrations/<timestamp>_AddStorageCatalog.cs`
- `backend/GarageBalance.StorageTool/` and solution entry.

Names are proposed and must follow actual namespaces/conventions when implemented; they do not exist now.

## 6. DB/schema and backfill

### `storage_objects`

- `id uuid PK`
- `logical_key text UNIQUE NOT NULL`
- `data_class text NOT NULL`
- `policy_id text NOT NULL`, `policy_revision int NOT NULL`
- `operation_id uuid UNIQUE NOT NULL`
- `committed_generation bigint NOT NULL`
- `file_name`, `content_type`, `size_bytes bigint`, `sha256 char(64)`
- backup metadata: kind, PostgreSQL major, application version, restore point UTC
- `state`, `created_at_utc`, `deleted_at_utc`, `retain_until_utc`, creator/deletion reason
- check constraints for size/hash/state and index `(data_class,state,created_at_utc)`.

### `storage_object_replicas`

- `id uuid PK`, `storage_object_id FK`
- `destination_id text`, `native_locator text`, optional `provider_version_id`
- `generation bigint`, `state`, size, SHA-256, provider checksum/ETag stored only as provider metadata
- attempts, next/last attempt, last verified, sanitized error category/code/message
- optimistic concurrency token/version
- unique `(storage_object_id,destination_id,generation)` and retry-scan indexes.

### `storage_transfer_jobs`

- id, object/replica FK, kind (`replicate`, `verify`, `delete`, `repair`), state, lease owner/expiry, attempt counters, due time, policy revision, bounded error.
- unique idempotency key per object/generation/destination/kind.
- workers claim with PostgreSQL locking/lease; expired leases are resumable.

### `storage_maintenance_runs`

- kind (`inventory`, `migration`, `reconciliation`, `restore_verification`), state, cursor/checkpoint, counters, started/finished timestamps, bounded summary/report locator.

Migration is additive. Existing backup files are not renamed. Backfill registers each valid managed filename only after size/TOC/SHA; invalid files go to report/quarantine, not deletion. Schema rollback is roll-forward: old binaries ignore new tables; destructive down migration is not used during rollback window.

## 7. Configuration and defaults

Proposed hierarchy (no secret values in source):

```text
Storage__Mode=Single | AsyncMirror
Storage__Policies__DatabaseBackups__RequiredIndependentCopies=2
Storage__Policies__DatabaseBackups__MinimumOffsiteCopies=1
Storage__Policies__DatabaseBackups__DesiredCopies=3
Storage__Replication__PollSeconds=15
Storage__Replication__MaxParallelPerDestination=2
Storage__Replication__OperationDeadlineSeconds=<PROPOSED, measured before rollout>
Storage__Providers__0__Id=local-hot
Storage__Providers__0__Type=LocalFileSystem
Storage__Providers__0__RootPath=/backups
Storage__Providers__1__Id=offsite-a
Storage__Providers__1__Type=S3Compatible
Storage__Providers__1__Endpoint=<operator config>
Storage__Providers__1__Bucket=<operator config>
Storage__Providers__1__CredentialSource=EnvironmentOrWorkloadIdentity
```

If `Storage` is absent, synthesize `Single/local-hot` from existing `DatabaseBackup.Directory`; no startup requirement for cloud credentials. Validate unique IDs, explicit roles/pools, private access, HTTPS (except isolated local test), endpoint allowlist, bucket/prefix, independent failure-domain labels, limits and capability-policy compatibility. Never return credential fields through options/status API.

## 8. State machines and workers

Object states: `Creating`, `CreatedLocal`, `ProtectionPending`, `Protected`, `ProtectionDegraded`, `Deleting`, `Deleted`, `Failed`.

Replica states: `Pending`, `Uploading`, `Unknown`, `VerificationPending`, `Available`, `Missing`, `Corrupted`, `Failed`, `Deleting`, `Deleted`, `Disabled`.

Job states: `Ready`, `Leased`, `RetryScheduled`, `Completed`, `Blocked`, `DeadLetter`.

Invariants:

- Only a complete verified local dump becomes committed generation.
- `Protected` means required independent-copy policy is actually met.
- `DesiredCopies` debt can remain visible without falsifying required protection.
- UNKNOWN upload is reconciled by deterministic locator/stat/metadata before retry.
- A durable job is not a copy; source bytes must remain readable until target verification.
- Retry cannot overwrite a later generation or bypass tombstone/policy revision.
- Disabled/draining destination receives no new job.
- Cross-instance claim is exclusive; expired lease can be safely resumed.

## 9. Read/write/backup routing and errors

### Creation and backup delivery

`pg_dump`/verification/source failure => creation failed; no destination fallback can make it valid. Valid local dump => `CreatedLocal`. Remote A timeout/5xx/429/quota/auth failure is adapter-classified; bounded retry/fallback may try permitted B within overall deadline. Per-copy states progress independently. If required protection is unmet, response/status says `ProtectionPending` or `ProtectionDegraded`; never ordinary fully protected success.

### Read/restore selection

Authorize first, identify logical object + committed generation/tombstone, load actual replicas, filter by policy/capability/state/generation/checksum/tier, then select priority. 403 business authorization never falls back. 404 is object-scoped. Stale/corrupt replica is excluded. If no current replica exists, return explicit not-found/unavailable/corrupt status.

### Error categories

Adapters map native errors to: `TransientNetwork`, `RateLimited`, `CredentialsExpired`, `ProviderForbidden`, `ObjectMissing`, `QuotaOrReadOnly`, `Conflict`, `ValidationOrUnsupported`, `ChecksumOrStale`, `ArchivePending`, `TlsSecurity`, `Cancelled`, `UnknownOutcome`. Each category has bounded retry/fallback and audit/metric behavior; no generic retry of every exception.

### Recovery/failback

Returning A is `Recovering`. Limited half-open probes and cooldown prove operation-specific health; reconciliation copies current generation/tombstones from authoritative verified source with resource caps. Read priority returns per verified object or after an explicit coverage gate. Mass corruption/security incident quarantines auto-repair and requires operator decision.

## 10. API, CLI and frontend contracts

- Existing `/api/settings/backups` routes remain.
- Status adds safe fields: `protectionState`, `requiredCopies`, `availableCopies`, `desiredCopies`, `checksum`, `lastVerifiedAtUtc`, replica summaries without bucket path/secrets.
- Add explicit endpoints under settings/backups for `retry`, `verify`, and optional `download-link`; all use backup-specific permissions and audit.
- Proxy download remains stable fallback and supports Range/cancellation/current generation.
- Direct link is short-lived and only returned after authorization/capability check; frontend retries logical endpoint on failure.
- CLI runs with operator identity and supports all `REQ-014` commands; destructive cleanup is separate, dry-run-first and explicit.

## 11. Migration requirements

1. Baseline inventory and verified restore point.
2. Additive schema and code in feature-off/`Single` mode.
3. Register current local files without rename/delete.
4. Start registering new backups and durable remote debt.
5. Configure/test A; bulk copy with checkpoint, verify.
6. Configure/test independent B; copy/verify.
7. Delta inventory including additions/tombstones.
8. Coverage report proves retained objects meet policy.
9. Enable read fallback, then write/delivery fallback, then recovery/failback in separate gates.
10. Keep local/source and compatible locator lookup through observation/rollback window.

## 12. Backup and restore requirements

- Proposed DB schedule ≤4h in working period plus pre-risk backups; owner must approve.
- Local quick copy + required independent off-site copy + desired second off-site copy.
- Fresh copies remain hot; tiering deferred until measured RTO and official capability evidence.
- Manifest contains logical ID, generation, filename, size, SHA, backup kind, PostgreSQL/app version, timestamps and encryption metadata reference; never secrets.
- Weekly proposed disposable restore: fetch selected copy, SHA, TOC, `pg_restore --exit-on-error`, migration/table/control checks, isolated read-only API readiness/login/report smoke.
- Key/config bundle uses authenticated encryption; decryption key/credential stored independently; restore drill validates an existing protected test secret.
- Production restore always operator-gated.

## 13. Security

- Separate runtime/local writer, remote backup writer (write/list/stat, minimal delete), restore reader, retention cleanup roles.
- Private destination, TLS validation, encryption at rest; client-side encrypted secret bundle.
- No credentials in Git/DB/frontend/API/log/audit/manifest/diagnostic package.
- Configurable endpoints require allowlist/scheme validation to prevent SSRF.
- Object key normalizer forbids traversal/control characters and embeds no PII.
- Backup permission split and audit for create/download/delete/retry/restore verification.
- Fallback preserves data class, owner/tenant, region, encryption and capability policy; no arbitrary personal drive.

## 14. Observability

Structured logs include operation/backup ID, data class, policy revision, destination ID, generation, category and next attempt; no signed URLs/secrets/high-cardinality PII. Metrics and alerts cover copy counts, protection lag, stale backup, all destinations failed, queue/staging capacity, quota/auth, recovery, restore-test age. Public health exposes only safe aggregate state. Liveness is not failed by optional destination; workload readiness/protection status is honest.

## 15. Test requirements

Required suites:

- unit: options/capabilities/policy graph, key normalization, states, retry budgets, error mapping, generation/tombstone, ack;
- local provider contract and existing backup characterization;
- PostgreSQL integration: migrations/indexes, atomic catalog/jobs, leases, restart, policy revision, partial success;
- S3-compatible integration in isolated test resources: private access, streaming, stat/delete, multipart/resume if used, signed TTL, timeouts/429/5xx/auth/quota, UNKNOWN;
- controller and frontend: all states/permissions/direct-proxy retry/Range/error/empty/loading;
- migration: inventory/dry-run/copy/verify/delta/resume/report, concurrent new backup/delete, rollback lookup;
- restore: real dump -> replica -> fetch -> SHA -> disposable DB -> key decrypt -> API smoke;
- security/privacy and old config non-regression;
- performance: bounded RAM/disk/concurrency, long outage backlog, no starvation.

Mocks do not prove real provider behavior. Real-provider and fault tests require owner-approved isolated accounts/resources.

## 16. Deployment compatibility

Deploy in expand/activate/contract style. Add schema first; old behavior remains default. Provider config and workers can be enabled independently. No provider cleanup or source deletion in first rollout. Single-local, local+one remote, local+two remotes and feature-off configurations must all start and pass baseline. Docker/systemd/local-PC installation remain supported.

## 17. Rollback and roll-forward

- Set mode `Single`, stop assigning remote jobs, preserve catalog/objects.
- Previous binary continues with unchanged local directory/filename/API routes.
- Do not down-migrate/drop catalog during rollback window.
- B-only objects remain accessible through compatibility lookup or are backfilled local before disabling router.
- Unknown/partial jobs remain recorded; safe resume later.
- Provider removal is draining -> verify alternate copies -> operator approval -> disable; delete is separate.
- Irreversible lifecycle/Object Lock/cleanup requires separate approval and cannot be hidden in feature rollback.

## 18. Acceptance criteria

| ID | Criterion |
|---|---|
| `AC-001` | Old config without `Storage` runs unchanged; existing local create/list/download/delete/pre-update tests pass. |
| `AC-002` | Every committed backup has logical ID, generation, size, SHA and manifest; file/catalog orphan cases reconcile. |
| `AC-003` | Multiple destinations/pools validate capabilities and policy; invalid/empty policy fails clearly. |
| `AC-004` | Partial success and UNKNOWN survive restart; only missing/unknown copy is retried/reconciled. |
| `AC-005` | Required-copy policy is never silently downgraded; local-created, pending, degraded, protected and failed are distinct. |
| `AC-006` | A down before remote write results in permitted B copy and actual locator, or honest pending/error within budget. |
| `AC-007` | A down before read returns exact current bytes from verified B without config change, or explicit error when no copy exists. |
| `AC-008` | Stale/corrupt/missing replica is never returned as current and does not globally disable unrelated healthy operations. |
| `AC-009` | Recovery repairs A from authoritative generation; B-only data/tombstones survive failback; flapping is bounded. |
| `AC-010` | Proxy/direct behavior is authorized, version-aware, Range/cancellation-safe; no secret reaches frontend. |
| `AC-011` | Valid backup falls back from remote A to B; source failure does not become destination success; chain/protection status is correct. |
| `AC-012` | Full isolated restore of DB + protected test secret + app smoke succeeds from independent copy and records RPO/RTO. |
| `AC-013` | Inventory/backfill/delta/resume proves all retained historical backups meet rollout coverage before failover enablement. |
| `AC-014` | Delete/retention cannot remove last required/current copy, resurrect tombstone or outrun restore dependencies. |
| `AC-015` | Existing Access, diagnostics, 1C, receipt, reports and release flows pass non-regression; no scopes/contracts change. |
| `AC-016` | Secrets/privacy tests pass; private encryption/least privilege/endpoint validation are verified. |
| `AC-017` | Metrics/alerts detect stale backup, debt, all-failed, quota/staging and stale restore drill without failing unrelated liveness. |
| `AC-018` | Load/fault tests prove bounded RAM/disk/retry/concurrency and measured failover/protection-lag budgets. |
| `AC-019` | Rollback to `Single` preserves access to all post-cutover backups; no destructive cleanup is required. |
| `AC-020` | Operator docs, provider add/drain, restore and incident runbooks match tested commands and contain no secrets. |

## 19. Definition of Done and Roadmap links

Done requires all mandatory `REQ-*` linked to implemented `TASK-*`, green risk-appropriate and publication suites, real isolated provider contract tests, historical coverage report, one independent-copy restore drill, documented actual RPO/RTO, security review, owner approval for production policies and successful observation window. A code-complete feature without real-provider/restore evidence is not operationally ready.

Execution order, task cards, commands, rollback gates and traceability are in `STORAGE_IMPLEMENTATION_ROADMAP.md`. Current status for every new task is `READY`, `TODO` or `BLOCKED`; none is `DONE`.
