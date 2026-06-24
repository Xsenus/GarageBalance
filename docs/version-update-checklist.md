# Checklist: version update

Документ фиксирует порядок обновления GarageBalance на локальном ПК или VPS. Цель: перед изменением рабочей базы всегда иметь свежий backup, заранее понимать SQL миграций, проверить health endpoint после запуска и иметь понятный rollback.

## 1. Перед началом

- [ ] Убедиться, что рабочее дерево релиза чистое: `git status --short`.
- [ ] Зафиксировать текущий commit: `git log -1 --oneline`.
- [ ] Проверить, что пользователь разрешил deploy/update именно этого релиза.
- [ ] Не выполнять `git push` без отдельного разрешения пользователя.
- [ ] Проверить, что реальные `.env`, `.accdb`, `.pgdump`, `.sql.gz` и приватные импорты не попали в Git.

## 2. Обязательный backup

Для локальной установки:

```powershell
.\infrastructure\scripts\backup-postgres.ps1 `
  -Database garagebalance_local `
  -HostName 127.0.0.1 `
  -Port 5432 `
  -Username garagebalance_local `
  -BackupDirectory C:\GarageBalance\Backups
```

Для VPS:

```bash
pg_dump --format=custom --file=/opt/garagebalance-staging/backups/garagebalance_$(date +%Y%m%d_%H%M%S).pgdump garagebalance_staging
```

- [ ] Проверить, что backup-файл создан.
- [ ] Проверить, что backup-файл больше 0 байт.
- [ ] Записать имя backup-файла в roadmap history или журнал работ.
- [ ] Не удалять предыдущий backup до приемки обновления.

## 3. Restore-check перед рисковыми изменениями

Если обновление меняет миграции, импорт или реальные данные, сначала восстановить backup в проверочную базу:

```powershell
.\infrastructure\scripts\restore-postgres.ps1 `
  -BackupFile C:\GarageBalance\Backups\garagebalance_local-20260624_091500.pgdump `
  -TargetDatabase garagebalance_restore_check `
  -DropAndCreate
```

- [ ] Restore идет в `garagebalance_restore_check`, а не в рабочую базу.
- [ ] `restore-postgres.ps1` не запускался с `-AllowProductionTarget`, если нет отдельного письменного решения.
- [ ] После restore-check выполнена проверка подключения к test-базе.

## 4. Проверки до выкладки

```powershell
dotnet test
npm run test -- --runInBand
npm run build
npm run lint
dotnet format --verify-no-changes
```

- [ ] Все backend-тесты прошли.
- [ ] Все React-тесты прошли.
- [ ] Production-сборка frontend создана.
- [ ] Lint и форматирование прошли.
- [ ] `git diff --check` не показывает whitespace-ошибок.
- [ ] `backend/GarageBalance.Api/AppReleases/releases.json` проходит JSON-валидацию.
- [ ] Измененные файлы проходят строгую UTF-8 no-BOM проверку.

## 5. Миграционный SQL

Перед применением миграций сформировать idempotent SQL:

```powershell
dotnet tool run dotnet-ef migrations script --idempotent `
  --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj `
  --startup-project .\backend\GarageBalance.Api\GarageBalance.Api.csproj `
  --output .\artifacts\deploy-migrations.sql
```

- [ ] SQL-скрипт сохранен как артефакт релиза.
- [ ] Если схема не менялась, это явно записано в историю работ.
- [ ] Если схема менялась, миграции применяются только после backup и restore-check.

## 6. Применение обновления

Локальный Docker-вариант:

```powershell
docker compose up --build -d
```

Локальный вариант без Docker:

```powershell
dotnet tool run dotnet-ef database update `
  --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj `
  --startup-project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
```

VPS-вариант:

- [ ] Разложить backend/frontend в новый release-каталог.
- [ ] Обновить symlink или рабочую папку только после backup.
- [ ] Выполнить `systemctl restart garagebalance-staging.service`.
- [ ] Выполнить `nginx -t`.
- [ ] Выполнить `systemctl reload nginx`, если nginx-конфиг менялся.

## 7. Smoke-проверка после обновления

Локальный ПК:

```powershell
curl -fsS http://127.0.0.1:5080/health
```

VPS/domain:

```bash
curl -fsS https://sgk.blagodaty.ru/health
```

- [ ] Открыть frontend в браузере: `http://127.0.0.1:5173` или `https://sgk.blagodaty.ru`.
- [ ] Войти под администратором.
- [ ] Проверить пользователей, справочники, платежи, отчеты, импорт, audit и "Что нового".
- [ ] Проверить `GET /api/app-releases`.
- [ ] Проверить, что новая версия видна в "Что нового".
- [ ] Проверить, что HTML-оболочка не залипла в кэше: `Cache-Control: no-store`.
- [ ] Проверить, что без входа рабочие разделы недоступны.

## 8. Rollback

- [ ] Остановить приложение: `docker compose down` или `systemctl stop garagebalance-staging.service`.
- [ ] Вернуть предыдущую папку релиза или symlink.
- [ ] Если БД была изменена, восстановить backup сначала в test-базу.
- [ ] Только после проверки test-базы принимать решение о восстановлении рабочей базы.
- [ ] Запустить приложение.
- [ ] Проверить `curl -fsS http://127.0.0.1:5080/health` или `curl -fsS https://sgk.blagodaty.ru/health`.
- [ ] Записать причину rollback и результат проверки в roadmap history.

## 9. После приемки

- [ ] Обновить roadmap history фактическими проверками.
- [ ] Убедиться, что "Что нового" содержит запись релиза.
- [ ] Сохранить номер commit и backup-файла.
- [ ] Не удалять backup до завершения ближайшего рабочего цикла.
