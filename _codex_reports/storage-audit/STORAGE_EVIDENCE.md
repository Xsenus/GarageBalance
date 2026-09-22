# Индекс доказательств по storage, backup и restore

Дата: 22.09.2026  
Baseline: ветка `master`, commit `e5309e8ae851d6a5f4ce4960e01066ded651e8fb`  
Режим: `ANALYZE_AND_PLAN_ONLY`

## 1. Правила чтения индекса

- `CODE` — подтверждено исполняемым кодом и wiring.
- `CONFIG` — подтверждено конфигурацией/deployment-файлом.
- `TEST` — подтверждено существующим автоматическим тестом или фактически выполненной командой.
- `RUNTIME` — безопасно наблюдалось на локальном хосте; это не доказательство production.
- `DOC` — заявлено документацией и требует сверки с runtime.
- `ABSENCE SEARCH` — результат полного поиска по доступному репозиторию; не доказывает отсутствие внешней managed-инфраструктуры.

Пути и строки относятся к baseline commit. Секреты и содержимое пользовательских данных не читались и не выводились.

## 2. Evidence map

| ID | Claim | Источник / символ | Тип | Уверенность и ограничения | Связи |
|---|---|---|---|---|---|
| `EVID-001` | Репозиторий состоит из ASP.NET Core API, React frontend, двух seed-утилит, deployment/scripts и тестов; отдельного storage-worker нет. | `GarageBalance.slnx`; `backend/GarageBalance.Api/Program.cs:318-327` | CODE | Высокая; vendor/generated каталоги исключены из построчного чтения. | `REQ-001`, `DEC-001` |
| `EVID-002` | В production dependencies и исходниках нет S3/S3-compatible, Drive, Dropbox, OneDrive, WebDAV, SFTP, rclone или Azure Blob adapter/client/configuration. | `backend/GarageBalance.Api/GarageBalance.Api.csproj:10-20`; полный поиск по manifests, code, config, Docker, CI и docs | ABSENCE SEARCH | Высокая в границах репозитория; внешняя инфраструктура `NOT VERIFIED`. | `RISK-001`, `REQ-002`, `DEC-001` |
| `EVID-003` | PostgreSQL — source of truth для бизнес-данных; файловой модели пользовательских вложений/медиа нет. | `backend/GarageBalance.Api/Infrastructure/Data/GarageBalanceDbContext.cs`; controllers/services; migrations | CODE | Высокая; production DB содержимое не исследовалось. | `DEC-001`, `REQ-001` |
| `EVID-004` | Backup options поддерживают один локальный каталог, интервал, окно, count-retention и пути `pg_dump`/`pg_restore`. | `backend/GarageBalance.Api/Application/DatabaseBackups/DatabaseBackupContracts.cs:5-35`; `backend/GarageBalance.Api/appsettings.json:50-61` | CODE/CONFIG | Высокая. | `RISK-002`, `REQ-003` |
| `EVID-005` | Сервис backup зарегистрирован в DI, а worker работает внутри API. | `backend/GarageBalance.Api/Program.cs:298-324` | CODE | Высокая; отдельного scheduler/worker deployment нет. | `RISK-002`, `DEC-006` |
| `EVID-006` | Backup создаётся во временный файл, проверяется на ненулевой размер и через `pg_restore --list`, затем атомарно переименовывается. | `backend/GarageBalance.Api/Infrastructure/DatabaseBackups/PostgresDatabaseBackupService.cs:55-169`; `CreateAsync()` | CODE/TEST | Высокая; это не полный restore. | `REQ-001`, `RISK-004` |
| `EVID-007` | Пароль PostgreSQL передаётся дочернему процессу через `PGPASSWORD`, а не аргумент командной строки. | `PostgresDatabaseBackupService.cs:351-374`; `BuildPasswordEnvironment()` | CODE | Высокая; защита окружения процесса зависит от ОС. | `REQ-010` |
| `EVID-008` | Блокировка создания backup — статический `SemaphoreSlim`, действующий только внутри одного процесса. | `PostgresDatabaseBackupService.cs:22,96-101,183-190` | CODE | Высокая; несколько API instances не координируются. | `RISK-007`, `TASK-010` |
| `EVID-009` | После rename retention выполняется до сохранения audit; удаление файла выполняется до audit commit. | `PostgresDatabaseBackupService.cs:138-166,263-318,415-421` | CODE | Высокая; общей ACID-транзакции с FS нет. | `RISK-008`, `REQ-007` |
| `EVID-010` | Каталог backup строится из имён файлов; DB metadata, checksum, generation, replica status и durable retry отсутствуют. | `PostgresDatabaseBackupService.cs:376-413`; `DatabaseBackupFileDto` | CODE | Высокая. | `RISK-003`, `REQ-004`, `REQ-005` |
| `EVID-011` | Скачивание — backend proxy stream с 64 KiB buffer и HTTP Range; имя валидируется регулярным выражением. | `PostgresDatabaseBackupService.cs:194-260,396-413`; `SettingsController.cs:356-374` | CODE/TEST | Высокая; remote/direct access отсутствует. | `REQ-006`, `DEC-005` |
| `EVID-012` | Backup API create/list/download/delete защищён общей permission `users.manage`. | `backend/GarageBalance.Api/Controllers/SettingsController.cs:319-398` | CODE/TEST | Высокая; least-privilege backup permissions отсутствуют. | `RISK-009`, `REQ-010` |
| `EVID-013` | UI показывает локальный путь, список, create/download/delete, но не protection/replica state. | `frontend/src/services/settingsApi.ts:102-122,245-265`; `frontend/src/features/settings/PasswordPanel.tsx:1345-1459` | CODE/TEST | Высокая. | `REQ-011`, `TASK-016` |
| `EVID-014` | Автоматический worker проверяет срок ежечасно, но создаёт backup только в окне 02:00–05:00 Europe/Moscow. | `backend/GarageBalance.Api/Application/DatabaseBackups/DatabaseBackupAutomation.cs:13-100` | CODE/TEST | Высокая; пропуск всего окна не догоняется вне окна. | `RISK-002`, `TASK-004` |
| `EVID-015` | Перед startup-миграциями при включённой опции создаётся обязательный `PreUpdate` backup. | `backend/GarageBalance.Api/Infrastructure/Data/DatabaseStartupHostedService.cs:22-55` | CODE/TEST | Высокая; копия остаётся на том же storage. | `REQ-001`, `RISK-001` |
| `EVID-016` | Docker размещает DB, backups, key ring, import queue и logs в отдельных volumes/bind mounts, но обычно на одном host. | `docker-compose.yml:1-69`; `.env.example:15-32` | CONFIG/TEST | Высокая для supplied compose; физическая независимость production `NOT VERIFIED`. | `RISK-001`, `RISK-005` |
| `EVID-017` | Data Protection key ring может сохраняться во внешнем локальном каталоге и нужен для расшифрования интеграционных секретов. | `backend/GarageBalance.Api/Program.cs:349-359`; `Infrastructure/Security/DataProtectionSensitiveDataProtector.cs` | CODE/CONFIG | Высокая; автоматического encrypted backup key ring нет. | `RISK-005`, `REQ-009` |
| `EVID-018` | Raw Access dry-run потоково записывается как `<runId>.pending`, затем worker читает и удаляет его. | `backend/GarageBalance.Api/Application/Import/ImportDryRunQueue.cs:93-175,204-212,215-350` | CODE/TEST | Высокая; orphan inventory/TTL нет. | `RISK-006`, `REQ-013` |
| `EVID-019` | Diagnostic errors сохраняются локально в JSONL, санитизируются, ротируются по 10 MiB и удаляются по возрасту. | `backend/GarageBalance.Api/Infrastructure/Diagnostics/RollingJsonDiagnosticLogger.cs:11-80,140-208,228-247`; `appsettings.json:25-31` | CODE/TEST | Высокая; logs не source of truth. | `DEC-001` |
| `EVID-020` | XLSX/PDF/CSV/JSON/ZIP exports генерируются на запрос и возвращаются из памяти; постоянного file storage для них нет. | `Controllers/ReportsController.cs`; `Controllers/AuditController.cs`; `Controllers/DiagnosticsController.cs`; соответствующие services | CODE/TEST | Высокая; максимальный production объём `NOT VERIFIED`. | `DEC-001`, `RISK-011` |
| `EVID-021` | `AppReleases/releases.json` поставляется с кодом и синхронизируется в PostgreSQL. | `backend/GarageBalance.Api/Application/Releases/AppReleaseCatalogSynchronizer.cs`; `AppReleaseService.cs:664` | CODE | Высокая. | `DEC-001` |
| `EVID-022` | Public health/readiness проверяет БД, но не freshness/protection backup и не storage destinations. | `backend/GarageBalance.Api/Controllers/HealthController.cs`; registration в `Program.cs` | CODE/TEST | Высокая. | `RISK-010`, `REQ-011` |
| `EVID-023` | Локальные PowerShell scripts умеют создать custom dump и восстановить его в защищённую check DB; destructive target требует explicit switch. | `infrastructure/scripts/backup-postgres.ps1:1-53`; `restore-postgres.ps1:1-70` | CODE/TEST | Высокая; backup script не выполняет `pg_restore --list`, checksum или remote copy. | `REQ-008` |
| `EVID-024` | VPS apply создаёт dump и реально восстанавливает его во временную DB до миграции. | `infrastructure/scripts/vps-apply-release.sh:328-356` | CODE/TEST | Высокая для script contract; фактический production запуск `NOT VERIFIED`. | `REQ-008`, `RISK-004` |
| `EVID-025` | Windows scheduled task можно зарегистрировать отдельно, но это внешняя операторская операция. | `infrastructure/scripts/register-local-backup-task.ps1:1-43` | CODE | Высокая; наличие задачи на production `NOT VERIFIED`. | `RISK-002` |
| `EVID-026` | Git ignore/privacy controls исключают dumps, Access-файлы, secrets и private import data. | `.gitignore`; `backend/GarageBalance.Api.Tests/Deployment/SensitiveFileGitIgnoreTests.cs`; `ApiSecretExposureTests.cs` | CODE/TEST | Высокая. | `REQ-010` |
| `EVID-027` | 1C Fresh и receipt printing — HTTP business adapters, не storage providers; они не предоставляют file replication/failover. | `Infrastructure/Integrations/OneCFreshHttpSyncAdapter.cs`; `ReceiptPrintingHttpAdapter.cs`; `appsettings.json:76-90` | CODE | Высокая. | `REQ-012`, `DEC-001` |
| `EVID-028` | История Git показывает эволюцию локальных backup/restore, но не внедрение cloud/object storage. | commits `fcb00175`, `0eb2ef4f`, `8fe18f09`, `849c9fd4`, `4fcb3da6` | CODE HISTORY | Высокая в доступной истории. | `REQ-001` |
| `EVID-029` | Focused storage-adjacent test batch прошёл 40/40, 0 skipped. | `dotnet test GarageBalance.slnx --configuration Release --no-restore --filter "FullyQualifiedName~DatabaseBackups|FullyQualifiedName~BackupScriptTests|FullyQualifiedName~RollingJsonDiagnosticLoggerTests|FullyQualifiedName~ImportDryRunQueueTests|FullyQualifiedName~SensitiveFileGitIgnoreTests|FullyQualifiedName~ApiSecretExposureTests"` | TEST | Выполнено 22.09.2026 на baseline; не является full suite или provider test. | `REQ-001`, `TEST-001` |
| `EVID-030` | На локальном хосте обнаружены 2 automatic `.pgdump` (08.09 и 16.09), оба прошли `pg_restore 17 --list`; последний был около 6 суток, scheduled task отсутствовал. | безопасная локальная проверка `%LOCALAPPDATA%\GarageBalance\backups`; `Get-ScheduledTask`; `pg_restore --list` | RUNTIME | Только текущий PC, не production. Содержимое dump не читалось и restore не выполнялся. | `RISK-002`, `RISK-004` |
| `EVID-031` | Docker runtime на машине аудита недоступен; локальный PostgreSQL 17 работал. | локальная диагностика 22.09.2026 | RUNTIME | Не ограничивает code audit; container integration не выполнялась. | `TEST-001` |
| `EVID-032` | Документация требует внешнее копирование и защищённое хранение keys/config, но automation и доказательство выполнения отсутствуют. | `docs/postgres-backup-restore.md`; `docs/docker-install-update-guide.md:143-170`; `docs/docker-windows-lan-guide.md:177-213` | DOC/CODE GAP | Высокая для gap; реальная ручная off-site копия `NOT VERIFIED`. | `RISK-001`, `RISK-005` |
| `EVID-033` | Existing DB schema не содержит logical storage object/replica/job catalog. | `GarageBalanceDbContext.cs`; migrations и model snapshot; поиск `StorageObject`, `Replica`, `ProviderId` | ABSENCE SEARCH | Высокая на baseline. | `REQ-004`, `DEC-003` |
| `EVID-034` | Production/frontend manifests не содержат storage SDK; dependency lock не показывает AWS/MinIO/Azure/Drive packages. | `GarageBalance.Api.csproj`; `packages.lock.json`; `frontend/package.json`, `package-lock.json` | CONFIG | Высокая. | `EVID-002` |

## 3. Выполненные команды и результаты

| ID | Команда / среда | Результат | Побочный эффект |
|---|---|---|---|
| `TEST-001` | Focused `dotnet test` из корня репозитория, Release, `--no-restore`, фильтр из `EVID-029` | exit 0; 40 passed; 0 failed; 0 skipped | Только обычные ignored test/build artifacts; после проверки выполнен `dotnet build-server shutdown`. |
| `TEST-002` | `pg_restore --list` для двух локальных managed dumps | exit 0 для обоих | Read-only; БД не создавалась и не изменялась. |
| `TEST-003` | Поиск storage/cloud SDK, adapters, endpoints и конфигурации через `rg` | Совпадений production storage adapters нет | Read-only. |
| `TEST-004` | `git status`, branch, commit, repository file inventory | Baseline зафиксирован; до новых отчётов был один untracked файл `docs/storage-backup-multistorage-audit-2026-09-22.md` | Read-only. |

## 4. Неподтверждённые области

- Состояние production/VPS, наличие внешних ручных копий, фактическая свежесть production backup — `NOT VERIFIED`.
- Любые provider capabilities, цены, Object Lock, versioning, region, quota и credentials — `NOT VERIFIED`, потому что provider не выбран и интеграции нет.
- Полный DR restore с PostgreSQL + Data Protection keys + config + запуском приложения — `NOT VERIFIED`.
- Реальные production DB size, object count, рост, egress и RPO/RTO — `NOT VERIFIED`.
- Managed backups вне репозитория/хоста (snapshot VPS, provider backup) — `NOT VERIFIED`; их отсутствие не утверждается.

