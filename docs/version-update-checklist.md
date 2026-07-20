# Безопасное обновление версии

Документ фиксирует порядок обновления GarageBalance на локальном ПК или VPS. Цель: перед изменением рабочей базы всегда иметь свежий backup, заранее понимать SQL миграций, проверить health endpoint после запуска и иметь понятный rollback.

## 1. Перед началом

- [ ] Убедиться, что рабочее дерево релиза чистое: `git status --short`.
- [ ] Зафиксировать текущий commit: `git log -1 --oneline`.
- [ ] Проверить, что пользователь разрешил deploy/update именно этого релиза.
- [ ] Не выполнять `git push` без отдельного разрешения пользователя.
- [ ] Проверить, что реальные `.env`, `.accdb`, `.pgdump`, `.sql.gz`, `artifacts/` и приватные импорты не попали в Git.

## 2. Обязательный backup

Перед backup проверить доступность локальной PostgreSQL:

```powershell
.\infrastructure\scripts\check-local-postgres.ps1 `
  -Database garagebalance_local `
  -HostName 127.0.0.1 `
  -Port 5432 `
  -Username garagebalance_local `
  -RequirePsql
```

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
- [ ] Записать имя backup-файла в журнал технических работ.
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
npm run test
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

Для автоматической выкладки в `master` эти проверки выполняет `.github/workflows/deploy-staging.yml`. Если GitHub Actions падает на любом шаге, релиз не отправляется на VPS.

Для готового пользовательского Docker ZIP эти же обязательные проверки выполняет `.github/workflows/publish-docker-release.yml`. Workflow не публикует images и установочный архив, пока backend/frontend quality gate не пройдет полностью.

## 5. Миграционный SQL

Перед применением миграций сформировать idempotent SQL:

```powershell
.\infrastructure\scripts\generate-migration-script.ps1 `
  -OutputPath artifacts\deploy-migrations.sql
```

- [ ] SQL-скрипт сохранен как артефакт релиза.
- [ ] В выводе есть `migrationScriptPath=...` и `migrationScriptBytes=...`.
- [ ] Если схема не менялась, это явно записано в историю работ.
- [ ] Если схема менялась, миграции применяются только после backup и restore-check.

## 6. Применение обновления

Публикация готового Docker-релиза владельцем проекта:

```powershell
git tag v0.759.0
git push origin v0.759.0
```

Tag `vX.Y.Z` запускает `Publish Docker release`: GitHub Actions проверяет код, собирает versioned API/frontend images, сохраняет их внутри `GarageBalance-Docker-X.Y.Z.zip`, вычисляет SHA-256 и прикладывает оба файла к GitHub Release. Tag создается только для уже проверенного commit и только после отдельного разрешения на push.

Обновление готовой пользовательской установки: распаковать новый ZIP поверх существующей папки и запустить `update.cmd`. Команда сама создает проверенный backup до импорта новой версии; `.env`, база, ключи, backups и логи сохраняются.

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
- [ ] Для автоматического deploy проверить, что GitHub Actions загрузил `api.tar.gz`, `frontend.tar.gz` и `deploy-migrations.sql` в `/home/garagebalance-deploy/uploads/<release-id>`.
- [ ] Запускать применение релиза через `sudo /usr/local/bin/garagebalance-deploy-apply <release-id>`, чтобы backup, миграции, замена каталогов, health-check и rollback выполнялись одним проверяемым сценарием.
- [ ] Обновлять symlink или рабочую папку только после backup.
- [ ] Выполнить `systemctl restart garagebalance-staging.service`, если обновление выполняется вручную без apply-скрипта.
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
- [ ] Записать причину rollback и результат проверки в журнал технических работ.

## 9. После приемки

- [ ] Записать выполненные проверки в журнал технических работ.
- [ ] Убедиться, что "Что нового" содержит запись релиза.
- [ ] Сохранить номер commit и backup-файла.
- [ ] Не удалять backup до завершения ближайшего рабочего цикла.
