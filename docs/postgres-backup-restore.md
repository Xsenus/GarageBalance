# PostgreSQL backup and restore

Эта инструкция описывает ручной и регулярный backup PostgreSQL для локальной установки GarageBalance и проверочное восстановление перед импортом Access, обновлением версии или миграциями.

## Скрипты

- `infrastructure/scripts/check-local-postgres.ps1` проверяет доступность локальной PostgreSQL по TCP, наличие `psql` и, если клиент установлен, выполняет безопасный `SELECT 1`.
- `infrastructure/scripts/backup-postgres.ps1` создает `.pgdump` через `pg_dump --format=custom`.
- `infrastructure/scripts/restore-postgres.ps1` восстанавливает backup через `pg_restore` в проверочную базу `garagebalance_restore_check`.
- `infrastructure/scripts/register-local-backup-task.ps1` регистрирует ежедневную задачу Windows Task Scheduler `GarageBalance Local PostgreSQL Backup`.

Скрипты не хранят пароль базы. Для автоматического запуска используйте безопасно настроенный PostgreSQL password file (`pgpass`) или переменную окружения на машине установки, а не файл в Git.

## Preflight локальной PostgreSQL

Перед миграциями, backup, restore-check и импортом сначала убедиться, что локальная база действительно доступна:

```powershell
.\infrastructure\scripts\check-local-postgres.ps1 `
  -Database garagebalance_local `
  -HostName 127.0.0.1 `
  -Port 5432 `
  -Username garagebalance_local `
  -RequirePsql
```

Ожидаемый успешный вывод содержит `postgresTcp=True`, `psql=True`, `psqlConnection=True` и `localPostgresPreflight=OK`. Скрипт выводит только факт наличия connection string через `connectionStringProvided=True/False`, но не печатает пароль или полный connection string.

## Ручной backup

```powershell
.\infrastructure\scripts\backup-postgres.ps1 `
  -Database garagebalance_local `
  -HostName 127.0.0.1 `
  -Port 5432 `
  -Username garagebalance_local `
  -BackupDirectory C:\GarageBalance\Backups
```

Проверка после запуска:

- [ ] В выводе есть `backupPath=...`.
- [ ] Файл лежит в `C:\GarageBalance\Backups`.
- [ ] Размер файла больше 0 байт.
- [ ] Имя backup-файла записано в историю работ перед импортом, обновлением или миграциями.

## Проверочное восстановление

По умолчанию восстановление делается не в рабочую базу, а в `garagebalance_restore_check`.

```powershell
.\infrastructure\scripts\restore-postgres.ps1 `
  -BackupFile C:\GarageBalance\Backups\garagebalance_local-20260624_091500.pgdump `
  -TargetDatabase garagebalance_restore_check `
  -DropAndCreate
```

Скрипт блокирует восстановление в `garagebalance`, `garagebalance_local` и `garagebalance_staging`, если явно не передать `-AllowProductionTarget`. Это сделано специально, чтобы проверка restore не стерла рабочие данные.

## Регулярный backup на локальном ПК

```powershell
.\infrastructure\scripts\register-local-backup-task.ps1 `
  -Database garagebalance_local `
  -HostName 127.0.0.1 `
  -Port 5432 `
  -Username garagebalance_local `
  -BackupDirectory C:\GarageBalance\Backups `
  -At 23:00
```

После регистрации:

- [ ] Открыть Windows Task Scheduler.
- [ ] Проверить задачу `GarageBalance Local PostgreSQL Backup`.
- [ ] Запустить задачу вручную один раз.
- [ ] Проверить появление нового `.pgdump` в `C:\GarageBalance\Backups`.
- [ ] Раз в месяц выполнять restore-check в отдельную базу.

## Перед импортом и обновлением

- [ ] Выполнить `check-local-postgres.ps1`.
- [ ] Выполнить `backup-postgres.ps1`.
- [ ] Проверить, что backup не пустой.
- [ ] Выполнить `restore-postgres.ps1` в `garagebalance_restore_check`, если меняется схема или импортируются реальные данные.
- [ ] Только после этого запускать импорт Access, `dotnet-ef database update` или выкладку новой версии.
- [ ] Не удалять предыдущий backup до приемки результата.
