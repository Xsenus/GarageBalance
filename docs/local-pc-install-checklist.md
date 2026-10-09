# Установка на локальный ПК без домена

Документ фиксирует установку GarageBalance на один локальный компьютер заказчика без домена, публичного VPS и TLS. Пользователь открывает веб-интерфейс в браузере по `http://127.0.0.1:5173`, API работает только локально по `http://127.0.0.1:5080`, база PostgreSQL хранится на этом же ПК.

Актуализировано 09.10.2026. Для Docker используйте [подробное руководство](docker-install-update-guide.md). Архив исходников требует сборки; готовый Docker ZIP уже содержит образы. Ни один исходный архив не должен содержать рабочую БД клиента или реальные секреты.

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
C:\GarageBalance\Config\DataProtectionKeys
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
HttpsRedirection__Enabled=false
ConnectionStrings__DefaultConnection=Host=127.0.0.1;Port=5432;Database=garagebalance_local;Username=garagebalance_local;Password=REPLACE_WITH_SECRET
Jwt__Issuer=GarageBalance
Jwt__Audience=GarageBalance
Jwt__SigningKey=REPLACE_WITH_AT_LEAST_32_UTF8_BYTES_SECRET
DataProtection__KeysPath=C:\GarageBalance\Config\DataProtectionKeys
Cors__AllowedOrigins__0=http://127.0.0.1:5173
Database__ApplyMigrationsOnStartup=true
Database__RequirePreMigrationBackup=true
DatabaseBackup__Directory=C:\GarageBalance\Backups
DatabaseBackup__PgDumpPath=C:\Program Files\PostgreSQL\17\bin\pg_dump.exe
DatabaseBackup__PgRestorePath=C:\Program Files\PostgreSQL\17\bin\pg_restore.exe
DiagnosticLogging__Directory=C:\GarageBalance\Logs
ImportProcessing__WorkDirectory=C:\GarageBalance\Imports\Queue
StagingDatabaseReset__Enabled=false
```

- [ ] Использовать уникальный пароль PostgreSQL.
- [ ] Использовать JWT secret не короче 32 UTF-8 байт.
- [ ] Не использовать примерный `change-this-development-key-before-real-data-32` для реальных данных.
- [ ] Ограничить доступ к `C:\GarageBalance\Config`.
- [ ] Убедиться, что `C:\GarageBalance\Config\DataProtectionKeys` сохраняется между обновлениями и доступен только учетной записи API/администратору.

Файл garagebalance.local.env не загружается ASP.NET Core автоматически. Перед запуском его нужно прочитать в окружение процесса. Пример для PowerShell (ключи и значения разделены первым =, без кавычек вокруг значения):

~~~powershell
$localConfigFile = 'C:/GarageBalance/Config/garagebalance.local.env'
foreach ($configLine in Get-Content -LiteralPath $localConfigFile -Encoding UTF8) {
    if ([string]::IsNullOrWhiteSpace($configLine) -or $configLine.TrimStart().StartsWith('#')) { continue }
    $configParts = $configLine.Split('=', 2)
    if ($configParts.Count -ne 2 -or $configParts[0] -notmatch '^[A-Za-z][A-Za-z0-9_]*$') {
        throw 'Некорректная строка конфигурации.'
    }
    [Environment]::SetEnvironmentVariable($configParts[0], $configParts[1], 'Process')
}
~~~

Создайте каталог Imports/Queue заранее и ограничьте права конфигурации/ключей учётной записью запуска и администратором. Значения не выводятся в консоль. Для запуска по расписанию этот же загрузчик должен выполняться в том же процессе PowerShell до API; среда другого терминала автоматически не переносится.

## 4. Вариант A: локальный запуск через Docker Compose

Этот вариант проще для демонстрации и локальной установки, если Docker Desktop уже установлен.

- [ ] Скопировать `.env.example` в `.env`.
- [ ] Заменить `POSTGRES_PASSWORD` и `JWT_SIGNING_KEY` на реальные значения.
- [ ] Оставить `DATA_PROTECTION_KEYS_PATH=/var/lib/garagebalance/keys` и проверить постоянный volume `data-protection-keys`.
- [ ] Оставить локальные bind-адреса без публикации наружу: `POSTGRES_BIND_ADDRESS=127.0.0.1`, `API_BIND_ADDRESS=127.0.0.1`, `FRONTEND_BIND_ADDRESS=127.0.0.1`.
- [ ] Оставить локальные порты: `POSTGRES_PORT=5432`, `API_PORT=5080`, `FRONTEND_PORT=5173`, `FRONTEND_ORIGIN=http://127.0.0.1:5173`.
- [ ] Проверить путь для backup mount: `BACKUP_HOST_PATH=./backups` или отдельная локальная папка вне Git.
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
- [ ] Для сборки установить .NET SDK 10 и Node.js 24 с npm. Для запуска framework-dependent сборки нужен ASP.NET Core Runtime 10; self-contained сборка ниже содержит runtime.
- [ ] Проверить службу PostgreSQL и её фактический порт; далее пример использует 5432. Несколько установленных кластеров могут иметь разные порты.
- [ ] Создать пользователя и базу:

```powershell
& 'C:/Program Files/PostgreSQL/17/bin/createuser.exe' -h 127.0.0.1 -p 5432 -U postgres -W --pwprompt --no-superuser --no-createdb --no-createrole garagebalance_local
& 'C:/Program Files/PostgreSQL/17/bin/createdb.exe' -h 127.0.0.1 -p 5432 -U postgres -W --owner=garagebalance_local --encoding=UTF8 --template=template0 garagebalance_local
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

- [ ] Пароль postgres нужен только для создания роли/БД; новый пароль garagebalance_local задаётся отдельно при --pwprompt. При существующей БД не пересоздавать её.
- [ ] Собрать опубликованный API из корня исходников:

```powershell
dotnet restore ./backend/GarageBalance.Api/GarageBalance.Api.csproj --locked-mode
dotnet publish ./backend/GarageBalance.Api/GarageBalance.Api.csproj -c Release -r win-x64 --self-contained true -o C:/GarageBalance/App/api
```

- [ ] Собрать frontend с локальным API:

```powershell
Set-Location ./frontend
npm ci
$env:VITE_API_BASE_URL=""
npm run build
New-Item -ItemType Directory -Force C:/GarageBalance/App/frontend
Copy-Item ./dist/* C:/GarageBalance/App/frontend -Recurse
Set-Location ..
```

- [ ] Загрузить конфигурацию из раздела 3 в окружение PowerShell, затем запустить API из опубликованной папки. Это гарантирует правильный content root для appsettings и файла «Что нового»:

```powershell
Set-Location C:/GarageBalance/App/api
./GarageBalance.Api.exe
```

- [ ] Проверить API: `curl -fsS http://127.0.0.1:5080/health`.
- [ ] API автоматически применяет EF migrations при Database__ApplyMigrationsOnStartup=true: отдельный EF CLI для установки не нужен. В случае имеющихся таблиц новые миграции предваряет проверенный backup.
- [ ] Раздать собранный frontend локальным nginx на http://127.0.0.1:5173. Файл index.html через file:// не открывать.
- [ ] Не открывать порты `5080`, `5173`, `5432` во внешнюю сеть без отдельного решения по безопасности.

Для Windows nginx используйте официальный дистрибутив и сохраните конфигурацию в его conf/nginx.conf. Внутри http-блока:

~~~nginx
server {
    listen 127.0.0.1:5173;
    server_name localhost;
    root C:/GarageBalance/App/frontend;
    index index.html;
    client_max_body_size 51m;
    location /api/ {
        proxy_pass http://127.0.0.1:5080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 300s;
    }
    location ~ ^/health(/(live|ready))?$ {
        proxy_pass http://127.0.0.1:5080;
    }
    location / {
        add_header Cache-Control "no-store" always;
        try_files $uri $uri/ /index.html;
    }
}
~~~

Проверьте nginx -t из папки nginx и запустите nginx.exe. Затем curl.exe -fsS http://127.0.0.1:5173/health/ready и браузер. Для остановки nginx используйте nginx.exe -s quit из той же папки, API в интерактивном терминале — Ctrl+C. Для автозапуска настройте Windows Task Scheduler от выделенной учётной записи с правильной рабочей папкой и загрузчиком окружения. Не используйте Vite dev/preview как постоянный производственный сервер; localhost-вариант без Docker требует обслуживания этих отдельных процессов.

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
- [ ] Записать причину rollback и результат проверки в журнал технических работ.

## 9. Что нельзя делать

- [ ] Не открывать порты PostgreSQL/API/frontend в интернет.
- [ ] Не использовать слабый JWT secret или пароль базы.
- [ ] Не запускать импорт Access без свежего backup.
- [ ] Не хранить единственный backup на том же диске без копии.
- [ ] Не выполнять push в Git без отдельного разрешения пользователя.

## 10. Условия финального закрытия локальной установки

- [ ] На выбранном ПК подтвержден способ запуска: Docker Compose или без Docker.
- [ ] Если выбран Docker Compose, выполнены `docker compose config`, `docker compose up --build -d`, `docker compose ps` и health-check API.
- [ ] Если выбран запуск без Docker, `check-local-postgres.ps1 -RequirePsql` завершился `localPostgresPreflight=OK`.
- [ ] Миграции применены к чистой локальной PostgreSQL и повторно применены без ошибок.
- [ ] Создан backup, выполнен restore-check в `garagebalance_restore_check`.
- [ ] Выполнена smoke-проверка входа, справочников, платежей, отчетов, импорта dry-run и "Что нового".
- [ ] В журнале технических работ записаны выбранный способ запуска, backup-файл, результат health-check и все блокеры.
