# Migration verification checklist

Этот checklist нужен перед финальной сдачей, обновлением версии, импортом Access и применением новых EF Core migrations. Цель: отдельно проверить, что миграции применяются к чистой PostgreSQL-базе и к базе после импорта, не ломают запуск API и могут безопасно повторяться.

Не используйте рабочую базу для проверки. Для live-прогона создать отдельные базы `garagebalance_migration_clean_check` и `garagebalance_migration_import_check` или аналогичные временные базы с понятными именами.

## 1. Предварительные условия

- [ ] Доступен локальный PostgreSQL или тестовый PostgreSQL на VPS.
- [ ] Доступны `psql`, `pg_dump`, `pg_restore` и `dotnet tool run dotnet-ef`.
- [ ] Выполнен `check-local-postgres.ps1 -RequirePsql`, успешный вывод содержит `localPostgresPreflight=OK`.
- [ ] Выполнен свежий `backup-postgres.ps1` рабочей базы или базы, из которой будет браться post-import состояние.
- [ ] Backup восстановлен через `restore-postgres.ps1 -TargetDatabase garagebalance_restore_check -DropAndCreate`.
- [ ] Временные базы проверки не совпадают с `garagebalance`, `garagebalance_local` и `garagebalance_staging`.
- [ ] В документацию и логи не копируются реальные персональные, финансовые и импортные данные.

## 2. Генерация idempotent SQL

```powershell
.\infrastructure\scripts\generate-migration-script.ps1 `
  -OutputPath artifacts\deploy-migrations.sql
```

- [ ] В выводе есть `migrationScriptPath=...`.
- [ ] В выводе есть `migrationScriptBytes=...`.
- [ ] `artifacts\deploy-migrations.sql` больше 0 байт.
- [ ] SQL-файл сохранен как временный проверочный артефакт релиза или удален после проверки, если он не нужен для выкладки.

## 3. Проверка на чистой базе

```powershell
$env:ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=garagebalance_migration_clean_check;Username=garagebalance_local;Password=REPLACE_WITH_SECRET"

dotnet tool run dotnet-ef database update `
  --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj `
  --startup-project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
```

- [ ] Все migrations применились к пустой базе без ошибок.
- [ ] Повторный `dotnet tool run dotnet-ef database update` завершился без изменений и ошибок.
- [ ] `dotnet tool run dotnet-ef migrations list` показывает все migrations как примененные.
- [ ] API стартует с этой базой и `/health` возвращает успешный ответ.
- [ ] Проверены базовые startup-seed правила: первый администратор/роли/permissions не ломают запуск.

## 4. Проверка на базе после импорта

Вариант для безопасной проверки:

- [ ] Взять post-import backup или согласованную обезличенную копию базы после импорта Access.
- [ ] Восстановить ее в `garagebalance_migration_import_check`, а не в рабочую базу.
- [ ] Убедиться, что import run, quarantine, audit и финансовые таблицы доступны без раскрытия чувствительных данных в логах.

```powershell
$env:ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=garagebalance_migration_import_check;Username=garagebalance_local;Password=REPLACE_WITH_SECRET"

dotnet tool run dotnet-ef database update `
  --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj `
  --startup-project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
```

- [ ] Migrations применились к post-import базе без ошибок.
- [ ] Повторный `dotnet tool run dotnet-ef database update` завершился без изменений и ошибок.
- [ ] Открываются health endpoint, вход администратора, импортный журнал, quarantine, платежи, начисления, балансы и отчеты.
- [ ] Нет ошибок PostgreSQL в логах API после startup и smoke-проверки.
- [ ] Если migration меняла финансовые, импортные или audit-таблицы, проверены соответствующие выборки и отчеты.

## 5. Фиксация результата

- [ ] В активном отчете проверки записаны: имена временных баз, путь к backup, путь к SQL-скрипту, результат clean DB, результат post-import DB, результат повторного применения и health/smoke; архивный roadmap не изменялся.
- [ ] Временные базы проверки удалены или явно оставлены с причиной и владельцем.
- [ ] Временный SQL в `artifacts\deploy-migrations.sql` удален, если он не является deliverable релиза.

## Условия финального закрытия проверки миграций

- [ ] `generate-migration-script.ps1` сформировал непустой idempotent SQL.
- [ ] Миграции применены к чистой PostgreSQL-базе и повторно применены без ошибок.
- [ ] Миграции применены к базе после импорта Access и повторно применены без ошибок.
- [ ] API успешно стартует на обеих проверочных базах, `/health` отвечает успешно.
- [ ] В логах API и PostgreSQL нет ошибок миграций, подключения, отсутствующих колонок/индексов или проблем кодировки.
- [ ] Результат зафиксирован в roadmap history без персональных, финансовых и импортных данных.
