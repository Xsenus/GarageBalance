# Установка и обновление GarageBalance через Docker

Инструкция рассчитана на администратора, который раньше не работал с Docker. Основной пользовательский способ требует только Docker Desktop и одного ZIP из GitHub Releases. Сборка из исходников оставлена отдельным сценарием для разработчика.

Отдельная подробная инструкция для Windows, смены портов и подключения других компьютеров локальной сети: [GarageBalance на Windows и в LAN](docker-windows-lan-guide.md).

## 1. Установка из готового ZIP — рекомендуемый способ

### Требования

- Windows 10/11 x64;
- установленный и запущенный Docker Desktop в режиме Linux containers;
- интернет только для скачивания Docker Desktop и установочного ZIP; после скачивания ZIP установка работает автономно;
- свободные локальные порты `5173`, `5080` и `5432`.

Git, .NET SDK, Node.js, PostgreSQL и ручная генерация паролей не нужны.

### Первый запуск

1. Откройте [GarageBalance Releases](https://github.com/Xsenus/GarageBalance/releases).
2. Скачайте `GarageBalance-Docker-ВЕРСИЯ.zip` и, при необходимости, проверьте SHA-256 по соседнему `.sha256`.
3. Полностью распакуйте ZIP в постоянную папку, например `C:\GarageBalance`.
4. Запустите `start.cmd`.
5. Дождитесь сообщения об успешном health-check и открытия `http://127.0.0.1:5173`.

`start.cmd` автоматически:

- проверяет Docker Engine и Compose v2;
- создает `.env` и криптографически стойкие уникальные секреты;
- загружает готовые API, frontend и PostgreSQL images из ZIP без входа в container registry и без обращения к Docker Hub;
- создает постоянные volumes базы и ключей защиты;
- запускает PostgreSQL, API и frontend;
- применяет EF Core migrations и ждёт успешного `/health`.

Повторный `start.cmd` использует прежний `.env`, volumes и данные. Секреты повторно не генерируются.

### Обновление готового ZIP

1. Скачайте ZIP новой версии.
2. Не удаляйте старую папку, `.env`, `backups`, `logs` и Docker volumes.
3. Распакуйте новый ZIP поверх старой папки с заменой файлов.
4. Запустите `update.cmd`.

Перед импортом новой версии `update.cmd` создает PostgreSQL backup в `backups`, проверяет его через `pg_restore --list` и прекращает обновление при любой ошибке. Затем он загружает versioned images, пересоздает контейнеры с прежними volumes и ждёт health-check. Старую проверенную копию не удаляйте до приемки версии.

Дополнительные команды в распакованной папке:

- `backup.cmd` — ручная проверенная копия базы;
- `diagnostics.cmd` — отчет о Docker, контейнерах и последних логах без `.env` и секретов;
- `stop.cmd` — безопасная остановка без удаления данных;
- `start.cmd` — повторный запуск текущей версии.

Не выполняйте `docker compose down -v`: ключ `-v` удаляет постоянные volumes с базой и ключами защиты.

## 2. Что хранится где

Docker запускает три изолированных сервиса:

- `garagebalance-postgres` — база PostgreSQL;
- `garagebalance-api` — backend, миграции и резервное копирование;
- `garagebalance-frontend` — интерфейс и прокси запросов `/api` к backend.

Контейнер — заменяемая программа. Рабочие данные не должны храниться только внутри него:

- база хранится в постоянном volume `garagebalance_postgres-data`;
- ключи защиты интеграционных настроек и cookies — в `garagebalance_data-protection-keys`;
- резервные `.pgdump` — в обычной папке компьютера из `BACKUP_HOST_PATH`;
- обезличенные журналы ошибок — в обычной папке компьютера из `LOG_HOST_PATH`;
- конфигурация и секреты — в локальном `.env`, который не публикуется в Git.

`docker compose build`, `up -d`, `restart`, `stop` и `down` не удаляют volumes и backup-папку. Никогда не выполняйте `docker compose down -v` и не удаляйте volumes вручную на рабочей установке: `-v` означает явное удаление постоянных данных.

## 3. Сборка из исходников для разработчика

На Windows 10/11 установите Docker Desktop с WSL 2 и убедитесь, что он запущен. На Linux/VPS установите Docker Engine и Docker Compose Plugin. Проверка:

```powershell
docker version
docker compose version
```

Обе команды должны показать клиент и сервер. Ошибка про `dockerDesktopLinuxEngine` означает, что Docker Desktop не запущен.

Рекомендуется не менее 4 ГБ свободной оперативной памяти и отдельное место для PostgreSQL и backup. Свободное место проверяйте регулярно: каждая копия содержит всю базу.

### Подготовка папки и секретов

Откройте PowerShell в корне проекта и создайте локальную конфигурацию:

```powershell
Copy-Item .env.example .env
New-Item -ItemType Directory -Force C:\GarageBalance\Backups
New-Item -ItemType Directory -Force C:\GarageBalance\Logs
```

Откройте `.env` и обязательно замените:

- `POSTGRES_PASSWORD` — длинный уникальный пароль PostgreSQL;
- `JWT_SIGNING_KEY` — случайная строка минимум 32 символа;
- `BACKUP_HOST_PATH` — абсолютный путь, например `C:/GarageBalance/Backups`.
- `LOG_HOST_PATH` — отдельный абсолютный путь, например `C:/GarageBalance/Logs`.

Случайные значения можно получить в PowerShell без внешнего сайта:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

Запустите команду два раза и используйте разные результаты. Не отправляйте `.env` в чат, почту или Git. Для Linux пример backup-пути: `/opt/garagebalance/backups`; доступ на запись должен быть у Docker.

По умолчанию приложение доступно только на текущем компьютере: `127.0.0.1:5173`. Для локальной сети сначала настройте firewall и доверенную сеть, затем задайте `FRONTEND_BIND_ADDRESS=0.0.0.0` и `FRONTEND_ORIGIN` с фактическим адресом. Для интернета нужен HTTPS reverse proxy; не публикуйте PostgreSQL-порт наружу.

### Первый запуск из исходников

Проверьте итоговую конфигурацию. Команда не должна печатать файл `.env` в журнал, доступный посторонним:

```powershell
docker compose config --quiet
docker compose build --pull
docker compose up -d
docker compose ps
```

Ожидается статус `healthy` у трех сервисов. Затем:

```powershell
curl.exe -fsS http://127.0.0.1:5173/health
docker compose logs --tail=100 api
```

Откройте `http://127.0.0.1:5173`. При первой пустой базе создайте администратора. API применяет EF Core migrations автоматически. Если миграций еще нет, база просто запускается; если схема меняется, сначала обязана создаться и пройти проверку предобновляющая копия.

## 4. Автоматические и ручные копии

Параметры по умолчанию:

- `DATABASE_BACKUP_AUTOMATIC_ENABLED=true` — автоматическое копирование включено;
- `DATABASE_BACKUP_INTERVAL_HOURS=24` — не реже одной копии за 24 часа, когда API работает;
- `DATABASE_BACKUP_RETENTION_COUNT=30` — хранить 30 последних управляемых копий;
- `REQUIRE_PRE_MIGRATION_BACKUP=true` — запрет миграции без проверенной предобновляющей копии.

API создает backup в custom-формате PostgreSQL во временный файл, проверяет его через `pg_restore --list` и только затем переименовывает в готовый `.pgdump`. Незавершенный или непроверяемый файл готовой копией не считается. При следующем запуске пропущенная по времени автоматическая копия догоняется.

Ручная копия:

1. Войти администратором.
2. Открыть `Настройки` → `Резервные копии`.
3. Нажать `Создать резервную копию`.
4. Указать понятную причину, например `Перед импортом показаний за июль`.
5. Дождаться сообщения и проверить новую строку в таблице и файл в `BACKUP_HOST_PATH`.

Одновременно запускается только одна копия. Если уже идет автоматическая или ручная операция, второй запуск корректно отклоняется. Создание ручной/автоматической копии записывается в историю изменений без пароля и содержимого базы.

Автоматическая ротация защищает диск от бесконечного роста, но не заменяет внешнее хранение. Не реже раза в неделю копируйте свежий `.pgdump` на другой физический диск или в защищенное хранилище с ограниченным доступом.

Backup базы содержит персональные и финансовые данные. Ограничьте доступ к `BACKUP_HOST_PATH`, не размещайте его в общей сетевой папке без шифрования и не добавляйте `.pgdump` в Git.

Ключи защиты не входят в PostgreSQL backup. Без них после полной потери компьютера нельзя будет расшифровать сохраненные секреты интеграций, поэтому после первичной настройки и после смены ключей сохраните защищенную внешнюю копию volume:

```powershell
docker run --rm -v garagebalance_data-protection-keys:/keys:ro -v C:/GarageBalance/Backups:/backup alpine:3.22 tar -czf /backup/data-protection-keys.tar.gz -C /keys .
```

Архив ключей храните отдельно от общедоступных файлов и не публикуйте. Он не заменяет `.pgdump`, а дополняет его для полного аварийного восстановления.

## 5. Обновление сборки из исходников

Перед каждым обновлением:

1. Создайте ручную копию через настройки и проверьте файл.
2. Запишите текущую версию: `git rev-parse HEAD`.
3. Получите обновление и пересоберите образы.

```powershell
git status --short
git pull --ff-only
docker compose build --pull
docker compose up -d --remove-orphans
docker compose ps
curl.exe -fsS http://127.0.0.1:5173/health
docker compose logs --tail=200 api
```

Во время `up -d` Docker заменяет контейнеры, но подключает те же volumes и backup-папку. Поэтому записи PostgreSQL не стираются. Перед применением новых миграций API дополнительно создает `pre_update` backup; если `pg_dump` или проверка файла завершается ошибкой, миграция и запуск API останавливаются вместо рискованного продолжения.

После обновления войдите и проверьте `Что нового`, гаражи, тарифы, платежи, отчеты и `Настройки` → `Резервные копии`. Не удаляйте старый образ и предыдущий backup до приемки.

Если обновление не запустилось, сначала изучите `docker compose logs api`. Возврат старого кода не всегда означает возврат схемы БД: при несовместимой миграции требуется согласованное восстановление backup. Не запускайте восстановление поверх рабочей базы наугад.

## 6. Проверка backup без затрагивания рабочей базы

Выберите файл из `BACKUP_HOST_PATH`; внутри обоих контейнеров он виден как `/backups/ИМЯ.pgdump`.

```powershell
docker compose exec postgres pg_restore --list /backups/garagebalance_manual_ГГГГММДД_ЧЧММСС_ммм.pgdump
docker compose exec postgres dropdb --if-exists -U garagebalance garagebalance_restore_check
docker compose exec postgres createdb -U garagebalance garagebalance_restore_check
docker compose exec postgres pg_restore --exit-on-error --no-owner --no-privileges -U garagebalance -d garagebalance_restore_check /backups/garagebalance_manual_ГГГГММДД_ЧЧММСС_ммм.pgdump
```

Если в `.env` изменен `POSTGRES_USER`, замените `garagebalance` в командах. После успешной проверки удалите только тестовую базу:

```powershell
docker compose exec postgres dropdb --if-exists -U garagebalance garagebalance_restore_check
```

Проводите restore-check минимум раз в месяц и после изменения процедуры backup. Наличие файла без успешной проверки восстановления недостаточно.

## 7. Аварийное восстановление рабочей базы

Это разрушительная операция. Сначала остановите запись данных и сохраните даже текущее поврежденное состояние:

```powershell
docker compose stop frontend api
docker compose exec postgres pg_dump --format=custom --no-owner --no-privileges -U garagebalance -d garagebalance -f /backups/before_emergency_restore.pgdump
```

Затем подтвердите нужный файл через `pg_restore --list` и сначала восстановите его в `garagebalance_restore_check` по разделу 7. Только после проверки и отдельного решения администратора можно пересоздать рабочую базу:

```powershell
docker compose exec postgres dropdb --if-exists -U garagebalance garagebalance
docker compose exec postgres createdb -U garagebalance garagebalance
docker compose exec postgres pg_restore --exit-on-error --no-owner --no-privileges -U garagebalance -d garagebalance /backups/ИМЯ_ПРОВЕРЕННОЙ_КОПИИ.pgdump
docker compose up -d
docker compose ps
curl.exe -fsS http://127.0.0.1:5173/health
```

При нестандартных именах базы/пользователя используйте значения `POSTGRES_DB` и `POSTGRES_USER` из `.env`. После восстановления проверьте вход, последние платежи, тарифы, отчеты и историю изменений.

## 8. Перезагрузка, остановка и удаление

Обычная остановка без потери данных:

```powershell
docker compose stop
docker compose start
```

Пересоздание контейнеров без потери данных:

```powershell
docker compose down
docker compose up -d
```

Полное удаление допустимо только после проверенной внешней копии. Опасная команда `docker compose down -v` удаляет базу и ключи защиты; в штатной эксплуатации ее не использовать.

Для переноса на другой компьютер перенесите проект без `.git`, защищенный `.env`, свежий проверенный `.pgdump` и архив volume ключей защиты. Разверните чистую установку, восстановите ключи до запуска API и затем восстановите `.pgdump`; секреты и backup передавайте только защищенным способом. Восстановление архива ключей — отдельная административная операция, которую сначала проверяют на тестовой установке.

## 9. Диагностика

При ошибке сначала запишите показанный код, затем администратор может открыть `Настройки` → `Диагностика` и скачать ограниченный ZIP с маскированными техническими событиями. Полное описание состава, хранения и безопасной передачи: `docs/diagnostic-logging-guide.md`.

```powershell
docker compose ps
docker compose logs --tail=200 postgres
docker compose logs --tail=200 api
docker compose logs --tail=200 frontend
docker system df
docker volume ls
```

- PostgreSQL unhealthy: проверить пароль, свободное место и логи `postgres`.
- API не стартует после обновления: найти сообщение backup/migration; не отключать `REQUIRE_PRE_MIGRATION_BACKUP`, а исправить доступ к `BACKUP_HOST_PATH` или место на диске.
- Интерфейс открывается, но нет данных: проверить `/health`, затем логи API и наличие `proxy_pass http://api:8080` в собранном frontend-образе.
- Backup не создается: проверить статус в настройках, права записи папки, свободное место и наличие `pg_dump`/`pg_restore` в API-образе.
- Порт занят: изменить соответствующий внешний порт в `.env`; внутренние порты Compose не менять.

Не публикуйте в диагностических материалах `.env`, connection string, backup, пароли, токены, реальные адреса, телефоны и финансовые данные.
