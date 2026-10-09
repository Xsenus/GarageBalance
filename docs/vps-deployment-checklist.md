# GarageBalance на VPS: установка с нуля и эксплуатация

Актуализировано 09.10.2026. Инструкция рассчитана на системного администратора. Она описывает два независимых способа: Docker Compose и отдельные службы PostgreSQL, ASP.NET Core и nginx. Выберите один способ для одной установки. Исходный архив заказчика собирается из той же версии приложения, что и репозиторий, но не содержит БД, секретов, Git и внутренних рабочих материалов.

Для установки из исходников нужен интернет к реестрам контейнеров либо NuGet/npm и пакетным репозиториям. Это не автономный установочный Docker ZIP с готовыми образами. В архиве исходников нельзя запускать `distribution/docker/start.cmd`: эта заготовка требует образов отдельного установочного релиза.

## 1. Что подготовить

Примеры ниже рассчитаны на Ubuntu 24.04 LTS x64. Для другой ОС адаптируйте установку пакетов. Начальный ориентир для небольшой организации: 2 vCPU, 4 ГБ RAM, SSD с запасом под базу, сборку и несколько полных копий. Окончательный размер выбирайте по объёму данных и измерениям.

До изменений на существующем сервере:

~~~bash
lsb_release -a
df -h
free -h
sudo ss -lntp
sudo systemctl --type=service --state=running
sudo nginx -T
~~~

Если nginx ещё не установлен, последняя команда пока неприменима. Существующие сайты и PostgreSQL других проектов не переустанавливайте. Используйте отдельные каталог, БД, пользователя и службу.

Подготовьте DNS A-запись своего домена на адрес VPS. AAAA создавайте только при настроенном IPv6. В примерах домен `garage.example.org` нужно заменить своим. Откройте на сетевом экране VPS TCP 80/443 и согласованный SSH-порт; PostgreSQL и API должны оставаться на localhost. До включения UFW разрешите свой SSH-порт, чтобы не потерять доступ.

~~~bash
sudo apt update
sudo apt install -y ca-certificates curl unzip nginx certbot python3-certbot-nginx openssl
sudo ufw allow OpenSSH
sudo ufw allow 'Nginx Full'
sudo ufw status verbose
~~~

Включение UFW и изменение SSH выполняются с учётом уже действующих правил. Не применяйте эти команды слепо к серверу с нестандартным SSH.

Распакуйте архив в постоянную папку, например `/opt/garagebalance/source`. Рабочие секреты, БД, ключи защиты и копии храните вне каталога исходников. Git для установки из ZIP не требуется.

## 2. Способ A: Docker Compose

Пошаговая настройка всех переменных, Windows и проверки восстановления описана в [Docker-руководстве](docker-install-update-guide.md). Здесь приведены особенности VPS.

### 2.1. Docker Engine

На новом Ubuntu-сервере установите Engine и Compose plugin по [официальной инструкции Docker](https://docs.docker.com/engine/install/ubuntu/). Используйте пакетный репозиторий Docker, а не произвольный скрипт с правами root. Проверка:

~~~bash
sudo docker version
sudo docker compose version
sudo systemctl enable --now docker
~~~

Доступ к Docker практически равнозначен root: не добавляйте обычные пользовательские аккаунты в группу docker без необходимости. Если в системе уже есть Docker, сначала проверьте его и существующие контейнеры.

### 2.2. Конфигурация

~~~bash
cd /opt/garagebalance/source
umask 077
cp .env.example .env
mkdir -p /opt/garagebalance/backups /opt/garagebalance/logs /opt/garagebalance/import-queue
openssl rand -hex 32
openssl rand -hex 48
nano .env
~~~

Два результата генерации используйте как разные пароль БД и JWT secret. Не записывайте их в публичный журнал. Минимально важные настройки:

~~~dotenv
POSTGRES_DB=garagebalance
POSTGRES_USER=garagebalance
POSTGRES_PASSWORD=REPLACE_WITH_RANDOM_DATABASE_PASSWORD
JWT_SIGNING_KEY=REPLACE_WITH_RANDOM_JWT_SECRET
ASPNETCORE_ENVIRONMENT=Production
POSTGRES_BIND_ADDRESS=127.0.0.1
POSTGRES_PORT=5432
API_BIND_ADDRESS=127.0.0.1
API_PORT=5080
FRONTEND_BIND_ADDRESS=127.0.0.1
FRONTEND_PORT=5173
FRONTEND_ORIGIN=https://garage.example.org
BACKUP_HOST_PATH=/opt/garagebalance/backups
LOG_HOST_PATH=/opt/garagebalance/logs
IMPORT_QUEUE_HOST_PATH=/opt/garagebalance/import-queue
DATA_PROTECTION_KEYS_PATH=/var/lib/garagebalance/keys
APPLY_MIGRATIONS_ON_STARTUP=true
REQUIRE_PRE_MIGRATION_BACKUP=true
DATABASE_BACKUP_ENABLED=true
DATABASE_BACKUP_AUTOMATIC_ENABLED=true
DATABASE_BACKUP_TIME_ZONE_ID=Asia/Novosibirsk
STAGING_DATABASE_RESET_ENABLED=false
~~~

При занятом 5432 поменяйте только внешний `POSTGRES_PORT`: API внутри сети Compose всегда подключается к `postgres:5432`. Аналогично внешний порт API не меняет его внутренний 8080. Для второй независимой установки нужно адаптировать имя Compose-проекта и фиксированные `container_name`, а не использовать существующие тома.

~~~bash
chmod 600 .env
sudo docker compose config --quiet
sudo docker compose build --pull
sudo docker compose up -d
sudo docker compose ps
curl -fsS http://127.0.0.1:5173/health/ready
~~~

Ожидается три healthy-сервиса. PostgreSQL-образ при первом запуске пустого тома создаёт роль и БД из `POSTGRES_USER/POSTGRES_DB`; backend создаёт таблицы миграциями. Ручные `createdb` и SQL создания таблиц для этого варианта не нужны. Первый администратор создаётся через начальную форму приложения; сделайте это до публичного открытия сайта, через SSH-туннель из раздела 5.

### 2.3. nginx для Docker

nginx хоста принимает HTTPS и передаёт запросы во frontend-контейнер на localhost:5173. Проксируйте весь сайт, включая `/api` и `/health`, одним блоком. Создайте `/etc/nginx/sites-available/garagebalance`:

~~~nginx
server {
    listen 80;
    server_name garage.example.org;
    client_max_body_size 51m;
    location / {
        proxy_pass http://127.0.0.1:5173;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Error-ID $request_id;
        proxy_connect_timeout 10s;
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
    }
}
~~~

~~~bash
sudo ln -s /etc/nginx/sites-available/garagebalance /etc/nginx/sites-enabled/garagebalance
sudo nginx -t
sudo systemctl reload nginx
sudo certbot --nginx -d garage.example.org --redirect
sudo nginx -t
sudo certbot renew --dry-run
curl -fsS https://garage.example.org/health/ready
~~~

Первоначальный HTTP-блок нужен, чтобы certbot смог получить сертификат; не подключайте заранее SSL-пути несуществующего сертификата. TLS завершается на nginx хоста. Добавьте HSTS в итоговый HTTPS-блок после проверки сертификата: `add_header Strict-Transport-Security "max-age=31536000" always;`. Если какой-либо промежуточный proxy меняет заголовки, проверяйте отсутствие циклов редиректа. Политики приложения и frontend уже задают CSP и защитные HTTP-заголовки.

## 3. Способ B: PostgreSQL + systemd + nginx, без Docker

### 3.1. Пакеты и учётная запись службы

Установите PostgreSQL 17 и клиентские программы через [официальный PGDG-репозиторий](https://www.postgresql.org/download/linux/ubuntu/), затем:

~~~bash
sudo apt install -y postgresql-17 postgresql-client-17 mdbtools
pg_dump --version
pg_restore --version
pg_lsclusters
~~~

Если сервер уже установлен, используйте его. Не создавайте ещё один кластер для той же установки. `pg_lsclusters` показывает фактический порт: далее используется 5432, замените его, если он другой. Клиент `pg_dump` не должен быть старше сервера. Для PostgreSQL 17 предпочтителен клиент 17.

Для сборки нужны .NET SDK 10 и Node.js 24 с npm. Установку .NET выполняйте по [инструкции Microsoft для Ubuntu](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install). Проверьте `dotnet --info`, `node --version`, `npm --version`. Ниже используется self-contained publish под linux-x64: установленный .NET runtime для запуска не обязателен, но системные зависимости .NET нужны. Для framework-dependent публикации требуется ASP.NET Core Runtime 10. ARM64 требует другой RID и отдельной проверки.

~~~bash
sudo useradd --system --home-dir /var/lib/garagebalance-staging --create-home --shell /usr/sbin/nologin garagebalance
sudo install -d -o garagebalance -g garagebalance -m 0750 /opt/garagebalance-staging
sudo install -d -o garagebalance -g garagebalance -m 0700 /opt/garagebalance-staging/backups /opt/garagebalance-staging/logs
sudo install -d -o garagebalance -g garagebalance -m 0700 /var/lib/garagebalance-staging/data-protection-keys /var/lib/garagebalance-staging/import-queue
~~~

Если пользователь уже существует, не создавайте его повторно. Имя `staging` здесь оставлено совместимым с поставляемым systemd-шаблоном; это имя каталогов, а не разрешение сбрасывать рабочую БД.

### 3.2. Создание роли и БД

Административная роль postgres используется только при подготовке. Приложение запускайте под отдельной несуперпользовательской ролью:

~~~bash
sudo -u postgres psql -X -p 5432 -d postgres
~~~

В psql:

~~~sql
CREATE ROLE garagebalance_staging LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
\password garagebalance_staging
CREATE DATABASE garagebalance_staging OWNER garagebalance_staging ENCODING 'UTF8' TEMPLATE template0;
\connect garagebalance_staging
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE, CREATE ON SCHEMA public TO garagebalance_staging;
\q
~~~

Команда `\password` запрашивает пароль без его включения в SQL-историю. Эти команды рассчитаны на новую БД и новую роль: при уже существующих объектах сначала проверьте их назначение и владельца. Не удаляйте имеющуюся БД для повторной установки.

PostgreSQL должен слушать localhost. Проверьте `SHOW listen_addresses;` и `SHOW hba_file;`. Для TCP-соединения приложения в pg_hba.conf подходит:

~~~text
host    garagebalance_staging    garagebalance_staging    127.0.0.1/32    scram-sha-256
~~~

Учитывайте порядок правил: PostgreSQL применяет первое подходящее. После изменения pg_hba выполните reload PostgreSQL, после изменения listen_addresses требуется restart в согласованное окно. Проверьте вход именно ролью приложения:

~~~bash
psql -X -W -h 127.0.0.1 -p 5432 -U garagebalance_staging -d garagebalance_staging -c 'SELECT current_database(), current_user;'
~~~

Приложению нужны права владельца своих объектов для EF migrations, но не superuser/CREATEDB. Проверочные базы создаёт администратор отдельно.

### 3.3. Сборка приложения

Собирать можно на отдельной машине с совместимыми инструментами. Из корня исходников:

~~~bash
dotnet restore backend/GarageBalance.Api/GarageBalance.Api.csproj --locked-mode
dotnet publish backend/GarageBalance.Api/GarageBalance.Api.csproj -c Release -r linux-x64 --self-contained true -o /tmp/garagebalance-release/api
dotnet restore backend/GarageBalance.StorageTool/GarageBalance.StorageTool.csproj --locked-mode
dotnet publish backend/GarageBalance.StorageTool/GarageBalance.StorageTool.csproj -c Release -r linux-x64 --self-contained true -o /tmp/garagebalance-release/storage-tool
cd frontend
npm ci
VITE_API_BASE_URL='' npm run build
cd ..
~~~

`VITE_API_BASE_URL=''` даёт относительные запросы `/api` на тот же домен. Переменные VITE применяются при сборке, а не после запуска nginx. Не включайте секреты в VITE-переменные: они доступны браузеру. Сборка выполняется из корня проекта и затем из frontend; `npm` в корне не работает.

Для проверенной версии разместите каталоги:

~~~bash
sudo cp -a /tmp/garagebalance-release/api /opt/garagebalance-staging/api
sudo cp -a /tmp/garagebalance-release/storage-tool /opt/garagebalance-staging/storage-tool
sudo cp -a frontend/dist /opt/garagebalance-staging/frontend
sudo chown -R garagebalance:garagebalance /opt/garagebalance-staging/api /opt/garagebalance-staging/storage-tool
sudo chmod 0755 /opt/garagebalance-staging/api/GarageBalance.Api /opt/garagebalance-staging/storage-tool/GarageBalance.StorageTool
sudo chmod 0755 /opt/garagebalance-staging
sudo find /opt/garagebalance-staging/frontend -type d -exec chmod 0755 '{}' +
sudo find /opt/garagebalance-staging/frontend -type f -exec chmod 0644 '{}' +
~~~

Это команды первой установки: при обновлении используйте новый release-каталог, а не копирование поверх запущенного backend. nginx должен иметь право чтения frontend и прохода по родительским каталогам; например, статика и её родитель `/opt/garagebalance-staging` могут иметь 0755, при сохранении 0700 для backups/logs и ключей.

### 3.4. Конфигурация службы

Создайте root-owned файл `/etc/garagebalance-staging.env` с правами 0600. Systemd читает его до перехода к пользователю API. Это формат EnvironmentFile, его нельзя исполнять как shell-скрипт.

~~~dotenv
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:3101
HttpsRedirection__Enabled=false
ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=garagebalance_staging;Username=garagebalance_staging;Password=REPLACE_WITH_SECRET"
Jwt__Issuer=GarageBalance
Jwt__Audience=GarageBalance
Jwt__SigningKey=REPLACE_WITH_AT_LEAST_32_UTF8_BYTES_SECRET
DataProtection__KeysPath=/var/lib/garagebalance-staging/data-protection-keys
Cors__AllowedOrigins__0=https://garage.example.org
Database__ApplyMigrationsOnStartup=true
Database__RequirePreMigrationBackup=true
DatabaseBackup__Enabled=true
DatabaseBackup__AutomaticEnabled=true
DatabaseBackup__Directory=/opt/garagebalance-staging/backups
DatabaseBackup__PgDumpPath=/usr/bin/pg_dump
DatabaseBackup__PgRestorePath=/usr/bin/pg_restore
DatabaseBackup__AutomaticWindowTimeZoneId=Asia/Novosibirsk
DiagnosticLogging__Directory=/opt/garagebalance-staging/logs
ImportProcessing__WorkDirectory=/var/lib/garagebalance-staging/import-queue
Finance__RegularAccrualAutomation__TimeZoneId=Asia/Novosibirsk
StagingDatabaseReset__Enabled=false
~~~

Все заглушки замените. Подготовьте два независимых секрета через `openssl rand -hex 32` / `openssl rand -hex 48`. Не меняйте JWT и Data Protection keys при каждом обновлении. Интеграции 1C и печати по умолчанию отключены; включаются отдельно после настройки и проверки [интеграций](integrations-guide.md).

~~~bash
sudo chown root:root /etc/garagebalance-staging.env
sudo chmod 600 /etc/garagebalance-staging.env
sudo cp infrastructure/deployment/garagebalance-staging.service /etc/systemd/system/garagebalance-staging.service
sudo chmod 0644 /etc/systemd/system/garagebalance-staging.service
sudo systemctl daemon-reload
sudo systemctl enable --now garagebalance-staging.service
sudo systemctl status garagebalance-staging.service --no-pager
curl -fsS http://127.0.0.1:3101/health/live
curl -fsS http://127.0.0.1:3101/health/ready
~~~

Backend применит EF migrations к созданной пустой БД. При имеющихся таблицах и новых миграциях сначала создаётся проверенная pre_update-копия; ошибка копирования останавливает запуск. Служба копирования требует рабочие pg_dump/pg_restore и права записи backups. Не отключайте защиту, чтобы обойти ошибку.

В штатном API не используется ручное создание таблиц. История схемы хранится в `__EFMigrationsHistory`. Если оператор выбирает ручной idempotent SQL, он делает backup и применяет SQL до запуска, а автоматические миграции отключает явно; оба метода не запускаются параллельно.

### 3.5. nginx и TLS

До первого администратора оставьте доступ локальным. После его создания по разделу 5 разместите HTTP-конфигурацию:

~~~nginx
server {
    listen 80;
    server_name garage.example.org;
    root /opt/garagebalance-staging/frontend;
    index index.html;
    client_max_body_size 51m;

    location /assets/ {
        try_files $uri =404;
        add_header Cache-Control "public, max-age=2592000, immutable" always;
    }
    location /api/ {
        proxy_pass http://127.0.0.1:3101;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Error-ID $request_id;
        proxy_connect_timeout 3s;
        proxy_read_timeout 300s;
        proxy_send_timeout 300s;
    }
    location ~ ^/health(/(live|ready))?$ {
        proxy_pass http://127.0.0.1:3101;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
    location / {
        add_header Cache-Control "no-store" always;
        try_files $uri $uri/ /index.html;
    }
}
~~~

Сохраните как `/etc/nginx/sites-available/garagebalance`, подключите symlink и выполните nginx -t/reload/certbot из раздела 2.3. После получения сертификата включите защитные заголовки и ограничения из поставляемого `infrastructure/deployment/garagebalance-staging.nginx.conf`, заменив домен, пути сертификата и каталоги. Шаблон с готовыми SSL-путями нельзя использовать до выдачи сертификата.

Полный шаблон требует определения `log_format garagebalance_timing` в http-контексте nginx, каталога `/var/log/garagebalance-nginx` и настройки logrotate. Пример формата:

~~~nginx
log_format garagebalance_timing '$time_iso8601 request_id=$request_id method=$request_method uri=$uri status=$status request_time=$request_time upstream_response_time=$upstream_response_time';
~~~

Используйте `$uri` вместо `$request_uri`, чтобы поисковые query-параметры не попадали в журнал. Не подменяйте существующие сайты сервера. Перед применением сохраните их конфигурацию.

## 4. Что сохранять между релизами

| Объект | Docker | Без Docker |
| --- | --- | --- |
| PostgreSQL | volume garagebalance_postgres-data | каталог действующего кластера PostgreSQL |
| Data Protection keys | volume garagebalance_data-protection-keys | /var/lib/garagebalance-staging/data-protection-keys |
| Секреты и конфигурация | .env | /etc/garagebalance-staging.env |
| Копии БД | BACKUP_HOST_PATH | /opt/garagebalance-staging/backups |
| Очередь импортов | IMPORT_QUEUE_HOST_PATH | /var/lib/garagebalance-staging/import-queue |
| Диагностические журналы | LOG_HOST_PATH | /opt/garagebalance-staging/logs |

Дамп БД не включает Data Protection keys и конфигурацию. Для полного переноса нужны все три: PostgreSQL-копия, ключи защиты и секреты. Храните их защищённо и вне единственного диска VPS. Ключи должны оставаться прежними для расшифровки защищённых настроек.

## 5. Первый вход и первичная настройка

У исходного Docker Compose нет общего заранее созданного логина/пароля. На базе без пользователей начальная форма позволяет создать первого администратора. После появления первого пользователя повторная первичная регистрация закрывается.

Чтобы никто посторонний не создал администратора раньше вас, выполните bootstrap до публикации сайта. В Docker-варианте откройте SSH-туннель с рабочей машины:

~~~bash
ssh -L 5173:127.0.0.1:5173 operator@YOUR_VPS_HOST
~~~

Затем откройте `http://127.0.0.1:5173` на этой машине и создайте администратора.

В варианте без Docker frontend уже собран, но до открытия домена удобнее задать одноразовые настройки службы:

~~~dotenv
InitialAdministrator__Enabled=true
InitialAdministrator__Email=admin@example.org
InitialAdministrator__DisplayName=Administrator
InitialAdministrator__Password=REPLACE_WITH_UNIQUE_LONG_PASSWORD
~~~

Добавьте их в закрытый EnvironmentFile перед первым запуском. После успешного создания пользователя удалите эти четыре строки и перезапустите API. Пароль останется в БД в виде хеша; исходный пароль в окружении больше не нужен. Для изменения пароля существующего пользователя повторный bootstrap не используется.

До реального учёта:

- проверьте права администратора и создайте отдельные учётные записи сотрудников;
- проверьте ставки, периоды, срок оплаты и фонды в разделе тарифов;
- укажите начальные остатки и дату начала учёта по согласованным данным;
- согласуйте бизнес-часовой пояс; начальные регулярные начисления выполняются автоматически, поэтому настройки тарифа проверяйте до работы на реальных данных;
- проверьте ручной backup и восстановление на отдельной БД;
- настройте независимое внешнее хранение копий;
- при изменении прав выйдите и войдите снова: текущая сессия содержит снимок прав.

## 6. Backup и проверка восстановления без Docker

Для localhost PostgreSQL административный Unix-пользователь postgres обычно подключается через peer. В примере ниже не нужен пароль приложения. Файл сначала создаётся в приватном каталоге postgres:

~~~bash
sudo install -d -o postgres -g postgres -m 0700 /var/lib/postgresql/garagebalance-backups
sudo -u postgres sh -c 'umask 077; pg_dump -p 5432 -Fc --no-owner --no-acl -d garagebalance_staging -f /var/lib/postgresql/garagebalance-backups/before_update.pgdump'
sudo -u postgres pg_restore --list /var/lib/postgresql/garagebalance-backups/before_update.pgdump > /dev/null
~~~

Не перезаписывайте последнюю пригодную копию: `before_update.pgdump` — пример имени; для каждой операции используйте уникальные дату/время. Для передачи приложению скопируйте файл в backups с владельцем garagebalance и 0600. Закрытый файл не должен быть в web-root.

Полная проверка:

~~~bash
sudo -u postgres createdb -p 5432 -O garagebalance_staging garagebalance_restore_check
sudo -u postgres pg_restore -p 5432 --exit-on-error --no-owner --no-acl --role=garagebalance_staging -d garagebalance_restore_check /var/lib/postgresql/garagebalance-backups/before_update.pgdump
sudo -u postgres psql -X -p 5432 -d garagebalance_restore_check -c 'SELECT count(*) FROM "__EFMigrationsHistory";'
sudo -u postgres psql -X -p 5432 -d garagebalance_restore_check -c "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';"
sudo -u postgres dropdb -p 5432 garagebalance_restore_check
~~~

Перед созданием проверьте, что `garagebalance_restore_check` свободна и не используется кем-то ещё; не удаляйте одноимённую чужую БД. Если restore упал, после изучения ошибки удалите только свою проверочную БД. Не запускайте приложение с фоновыми заданиями и внешними интеграциями на клиентской копии.

Простая проверка TOC `pg_restore --list` не заменяет реальное восстановление. Повторяйте restore-check минимум ежемесячно и после изменения процедуры копирования. Подробности облачной репликации и защиты ключей: [backup/restore](postgres-backup-restore.md), [хранилища](storage-operations.md), [аварийное восстановление](disaster-recovery.md).

## 7. Обновление и возврат предыдущей версии

Rollback выполняется по процедуре ниже с проверкой совместимости схемы.

1. Согласуйте окно работ и остановите запись новых финансовых данных.
2. Сохраните текущие версии backend/frontend, БД, EnvironmentFile и ключей.
3. Создайте свежий backup и выполните проверочное восстановление.
4. Соберите новую версию в отдельный release-каталог.
5. Остановите API. Замените приложение согласованным релизом, сохранив предыдущие каталоги.
6. Примените миграции единственным выбранным способом. При автоматическом режиме запускается новая служба с прежней конфигурацией и ключами.
7. Проверьте readiness, вход, справочники, платежи, отчёты, аудит и «Что нового».
8. После приёмки удаляйте только временные результаты сборки, сохраняя резервные копии по политике.

Возврат старых бинарников допустим лишь при совместимой схеме. При несовместимой миграции восстановите проверенную предрелизную БД в отдельную новую базу, проверьте её, выдайте права роли приложения и измените connection string при остановленном API. После запуска старой версии проверьте последние финансовые записи. Такое восстановление возвращает состояние на момент backup: более поздние операции требуют отдельного согласованного переноса.

Не выполняйте `dotnet ef database update 0`, `DROP DATABASE` рабочей базы или `docker compose down -v` как способ обычного обновления.

## 8. Действующая установка и автоматизация владельца репозитория

В поставляемых шаблонах действующего VPS используются:

- приложение /opt/garagebalance-staging, служба garagebalance-staging.service;
- API 127.0.0.1:3101, домен sgk.blagodaty.ru;
- EnvironmentFile /etc/garagebalance-staging.env;
- БД/роль garagebalance_staging.

Для новой организации заменяйте эти значения согласованно. Скрипт `infrastructure/scripts/vps-apply-release.sh` предназначен для этой конкретной топологии: не запускайте его без адаптации на другом сервере.

В репозитории push в master запускает GitHub Actions Deploy staging: полный backend/frontend quality gate, упаковка API/frontend/StorageTool, idempotent SQL, предрелизный backup с restore-check, обновление и readiness. Настройки repository secrets: VPS_HOST, VPS_DEPLOY_USER, VPS_SSH_KEY. Текущий workflow получает SSH host key через ssh-keyscan; при подготовке нового сервера дополнительно сверяйте его fingerprint с доверенным каналом администратора. Встроенной проверки отдельного секрета с заранее закреплённым fingerprint в текущем workflow нет.

Deploy-пользователь garagebalance-deploy получает право только на root-owned apply-скрипт /usr/local/bin/garagebalance-deploy-apply, не общий sudo. Не переносите приватный SSH-ключ и файл secrets в исходный архив. Для самостоятельной установки CI и SSH-ключ проекта не нужны.

Для действующего сервера проверяйте полномочия и запуск именно опубликованного workflow:

~~~bash
sudo -l -U garagebalance-deploy
sudo systemctl status garagebalance-staging.service --no-pager
curl -fsS https://sgk.blagodaty.ru/health/ready
~~~

Workflow находится в .github/workflows/deploy-staging.yml. Применение загруженного согласованного релиза: /usr/local/bin/garagebalance-deploy-apply <release-id>. Выпускающий администратор проверяет desktop/mobile вход, формы и Cache-Control. Для перевыпуска сертификата именно этого домена: certbot --nginx -d sgk.blagodaty.ru; на новой установке используется её собственный домен.

При выборе ручного миграционного SQL вместо автоматического режима, из корня исходников:

~~~bash
dotnet tool restore
mkdir -p artifacts
dotnet tool run dotnet-ef migrations script --idempotent --project backend/GarageBalance.Api/GarageBalance.Api.csproj --startup-project backend/GarageBalance.Api/GarageBalance.Api.csproj --output artifacts/deploy-migrations.sql
~~~

Сгенерированный SQL применяется только после backup/restore-check при остановленном API. Для обычной установки с автоматическими миграциями этот шаг не нужен.

### Условия финального закрытия VPS/domain deployment

Проверены readiness, сертификат и renewal, полномочия deploy-аккаунта, backup/restore, вход, desktop/mobile и предыдущий комплект для Rollback. В опубликованном appsettings уровень Microsoft.EntityFrameworkCore.Database.Command оставляйте Warning: каждый успешный SQL-запрос не должен записываться в рабочий журнал. Не коммитить реальные secrets, pgdump, ключи защиты, Access-данные и диагностические материалы.

## 9. Диагностика и приёмка

Без Docker:

~~~bash
sudo systemctl status garagebalance-staging --no-pager
sudo journalctl -u garagebalance-staging -n 100 --no-pager
sudo nginx -t
pg_lsclusters
curl -fsS http://127.0.0.1:3101/health/live
curl -fsS http://127.0.0.1:3101/health/ready
curl -fsS https://garage.example.org/health/ready
sudo certbot renew --dry-run
~~~

Docker:

~~~bash
cd /opt/garagebalance/source
sudo docker compose ps
sudo docker compose logs --tail=100 api
sudo docker compose logs --tail=100 postgres
curl -fsS http://127.0.0.1:5173/health/ready
~~~

Liveness проверяет процесс, readiness — доступ к PostgreSQL. HTTP 200 только у /health/live ещё не подтверждает работоспособность БД.

| Симптом | Что проверить |
| --- | --- |
| 502 nginx | служба API / контейнеры, порт upstream, readiness |
| password authentication failed | фактический порт, роль, пароль, порядок pg_hba |
| API падает перед миграцией | pg_dump/pg_restore, права backups, свободное место |
| permission denied в logs/keys/import-queue | владелец и права постоянных каталогов, ReadWritePaths systemd |
| Пустая страница после обновления | путь frontend, наличие assets, nginx cache для index.html |
| Форма не редактируется / 401 / 403 | права пользователя и новый вход после изменения роли |
| TLS не выпускается | DNS A/AAAA, доступность 80/443, отсутствие чужого listener |
| Старый пароль БД после изменения .env | POSTGRES_PASSWORD не меняет пароль в уже созданном Docker volume |
| Расшифровка настроек не работает | сохранность прежних Data Protection keys |

Установка принята, когда успешны readiness, TLS renewal, вход администратора и ограниченной роли, просмотр данных, отчёты, резервное копирование и restore-check. Финансовые пробные операции выполняйте на отдельной учебной базе, а не добавляйте мусор в рабочую. В журнале администратора запишите версию, дату, место хранения secrets/keys, последнюю проверенную копию и ответственного.
