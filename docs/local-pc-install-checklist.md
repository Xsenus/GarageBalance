# Checklist: local PC without domain

Документ фиксирует установку GarageBalance на один локальный компьютер заказчика без домена, публичного VPS и TLS. Пользователь открывает веб-интерфейс в браузере по `http://127.0.0.1:5173`, API работает только локально по `http://127.0.0.1:5080`, база PostgreSQL хранится на этом же ПК.

## 1. Когда выбирать этот сценарий

- [ ] Система нужна только на одном компьютере или в рамках ручной демонстрации.
- [ ] Доступ из интернета не требуется.
- [ ] Домен и TLS не настраиваются.
- [ ] Ответственный за ПК понимает, где хранятся backup и как остановить приложение.
- [ ] До переноса реальных данных проверен вход, справочники, платежи, отчеты, импорт и "Что нового".

## 2. Папки на Windows

Рекомендуемая структура:

```powershell
C:\GarageBalance\App
C:\GarageBalance\Config
C:\GarageBalance\Backups
C:\GarageBalance\Logs
C:\GarageBalance\Imports
```

- [ ] Создать папки до установки.
- [ ] Хранить `.accdb`/`.mdb` только в `C:\GarageBalance\Imports` или другой приватной папке.
- [ ] Хранить backup PostgreSQL только в `C:\GarageBalance\Backups`.
- [ ] Не добавлять реальные `.env`, дампы, backup и Access-файлы в Git.

## 3. Настройки и секреты

Secrets-файл для локального ПК: `C:\GarageBalance\Config\garagebalance.local.env`.

```powershell
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5080
ConnectionStrings__DefaultConnection=Host=127.0.0.1;Port=5432;Database=garagebalance_local;Username=garagebalance_local;Password=REPLACE_WITH_SECRET
Jwt__Issuer=GarageBalance
Jwt__Audience=GarageBalance
JWT_SIGNING_KEY=REPLACE_WITH_AT_LEAST_32_UTF8_BYTES_SECRET
Cors__AllowedOrigins__0=http://127.0.0.1:5173
```

- [ ] Использовать уникальный пароль PostgreSQL.
- [ ] Использовать JWT secret не короче 32 UTF-8 байт.
- [ ] Не использовать примерный `change-this-development-key-before-real-data-32` для реальных данных.
- [ ] Ограничить доступ к `C:\GarageBalance\Config`.

## 4. Вариант A: локальный запуск через Docker Compose

Этот вариант проще для демонстрации и локальной установки, если Docker Desktop уже установлен.

- [ ] Скопировать `.env.example` в `.env`.
- [ ] Заменить `POSTGRES_PASSWORD` и `JWT_SIGNING_KEY` на реальные значения.
- [ ] Оставить локальные порты без публикации наружу: `POSTGRES_PORT=5432`, `API_PORT=5080`, `FRONTEND_PORT=5173`.
- [ ] Запустить:

```powershell
docker compose up --build -d
```

- [ ] Проверить контейнеры: `docker compose ps`.
- [ ] Проверить API: `curl -fsS http://127.0.0.1:5080/health`.
- [ ] Открыть интерфейс: `http://127.0.0.1:5173`.
- [ ] Создать первого администратора, если база пустая.
- [ ] Проверить, что после перезапуска ПК данные остались в volume `postgres-data`.

## 5. Вариант B: локальный запуск без Docker

Этот вариант нужен, если Docker Desktop нельзя использовать. До финальной упаковки он остается администраторским сценарием и требует установленного PostgreSQL.

- [ ] Установить PostgreSQL 17.
- [ ] Установить .NET 10 ASP.NET Core Runtime или SDK.
- [ ] Установить Node.js только если frontend собирается на этом ПК.
- [ ] Создать пользователя и базу:

```powershell
createuser --pwprompt garagebalance_local
createdb --owner=garagebalance_local garagebalance_local
```

- [ ] Проверить локальную PostgreSQL перед миграциями:

```powershell
.\infrastructure\scripts\check-local-postgres.ps1 `
  -Database garagebalance_local `
  -HostName 127.0.0.1 `
  -Port 5432 `
  -Username garagebalance_local `
  -RequirePsql
```

- [ ] Восстановить инструменты проекта: `dotnet tool restore`.
- [ ] Применить миграции:

```powershell
dotnet tool run dotnet-ef database update `
  --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj `
  --startup-project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
```

- [ ] Собрать frontend с локальным API:

```powershell
$env:VITE_API_BASE_URL="http://127.0.0.1:5080"
npm run build
```

- [ ] Запустить API только на localhost:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Production"
$env:ASPNETCORE_URLS="http://127.0.0.1:5080"
$env:ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=garagebalance_local;Username=garagebalance_local;Password=REPLACE_WITH_SECRET"
$env:Jwt__Issuer="GarageBalance"
$env:Jwt__Audience="GarageBalance"
$env:Jwt__SigningKey="REPLACE_WITH_AT_LEAST_32_UTF8_BYTES_SECRET"
dotnet .\backend\GarageBalance.Api\bin\Release\net10.0\GarageBalance.Api.dll
```

- [ ] Проверить API: `curl -fsS http://127.0.0.1:5080/health`.
- [ ] Раздать собранный frontend локальным статическим сервером на `http://127.0.0.1:5173`.
- [ ] Не открывать порты `5080`, `5173`, `5432` во внешнюю сеть без отдельного решения по безопасности.

## 6. Backup перед импортом и обновлением

- [ ] Перед импортом Access создать backup PostgreSQL:

```powershell
.\infrastructure\scripts\backup-postgres.ps1 `
  -Database garagebalance_local `
  -HostName 127.0.0.1 `
  -Port 5432 `
  -Username garagebalance_local `
  -BackupDirectory C:\GarageBalance\Backups
```

- [ ] Проверить, что backup-файл появился в `C:\GarageBalance\Backups`.
- [ ] Проверить восстановление в отдельную базу `garagebalance_restore_check` через `.\infrastructure\scripts\restore-postgres.ps1`.
- [ ] Для ежедневного backup зарегистрировать задачу `GarageBalance Local PostgreSQL Backup` через `.\infrastructure\scripts\register-local-backup-task.ps1`.
- [ ] Записать имя backup-файла в историю работ.
- [ ] Не удалять предыдущий backup до приемки новой версии или импорта.

## 7. Smoke-проверка

- [ ] Открыть `http://127.0.0.1:5173`.
- [ ] Создать первого администратора или войти существующим пользователем.
- [ ] Проверить раздел "Пользователи" и матрицу ролей.
- [ ] Создать тестового владельца, гараж, поставщика и тариф.
- [ ] Создать тестовое начисление и платеж.
- [ ] Открыть отчеты и "Что нового".
- [ ] Проверить dry-run импорта Access без изменения исходного файла.
- [ ] Проверить, что без входа рабочие разделы недоступны.

## 8. Rollback

- [ ] Остановить приложение или `docker compose down`.
- [ ] Вернуть предыдущую папку `C:\GarageBalance\App`.
- [ ] При необходимости восстановить backup PostgreSQL в отдельную test-базу.
- [ ] Только после проверки восстановленной test-базы переключать рабочую базу.
- [ ] Запустить приложение и проверить `curl -fsS http://127.0.0.1:5080/health`.
- [ ] Записать причину rollback и результат проверки в roadmap history.

## 9. Что нельзя делать

- [ ] Не открывать порты PostgreSQL/API/frontend в интернет.
- [ ] Не использовать слабый JWT secret или пароль базы.
- [ ] Не запускать импорт Access без свежего backup.
- [ ] Не хранить единственный backup на том же диске без копии.
- [ ] Не выполнять push в Git без отдельного разрешения пользователя.
