# Развёртывание на VPS с доменом и TLS

Документ фиксирует безопасную процедуру тестового и будущего production-размещения GarageBalance на VPS с доменом `sgk.blagodaty.ru`. Реальные пароли, токены, `.env`, дампы PostgreSQL и приватные файлы импорта в Git не добавляются.

## 1. Предварительная проверка VPS

- [ ] Подключиться к серверу по SSH только с доверенной машины.
- [ ] Проверить, какие проекты уже запущены: `systemctl --type=service`, `docker ps`, `ss -tulpn`, `ls /etc/nginx/sites-enabled`.
- [ ] Убедиться, что новые порты и имена сервисов не конфликтуют с существующими проектами.
- [ ] Создать отдельный каталог приложения: `/opt/garagebalance-staging`.
- [ ] Создать отдельный каталог резервных копий: `/opt/garagebalance-staging/backups`.
- [ ] Создать постоянный каталог Data Protection keys: `/var/lib/garagebalance-staging/data-protection-keys` и выдать доступ только учетной записи API.
- [ ] Создать файл окружения вне Git: `/etc/garagebalance-staging.env`.
- [ ] Ограничить права на secrets-файл: `chmod 600 /etc/garagebalance-staging.env`.

## 2. Обязательные секреты и настройки

В `/etc/garagebalance-staging.env` должны быть заданы только реальные значения:

```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:3101
HttpsRedirection__Enabled=false
ConnectionStrings__DefaultConnection=Host=127.0.0.1;Port=5432;Database=garagebalance_staging;Username=garagebalance_staging;Password=REPLACE_WITH_SECRET
Jwt__Issuer=GarageBalance
Jwt__Audience=GarageBalance
Jwt__SigningKey=REPLACE_WITH_AT_LEAST_32_UTF8_BYTES_SECRET
DataProtection__KeysPath=/var/lib/garagebalance-staging/data-protection-keys
```

- Параметры `DatabasePerformance` по умолчанию ограничивают Npgsql-пул 32 соединениями, держат 2 тёплых соединения и очищают простаивающие соединения через 300 секунд с интервалом 10 секунд. Для PostgreSQL на том же VPS `KeepAliveSeconds=0`: прикладной ping не нужен. Менять эти значения следует только после измерения ожиданий пула и числа соединений.
- [ ] Не использовать примерный `change-this`/`development` JWT secret вне Development.
- [ ] Не хранить `/etc/garagebalance-staging.env` в рабочем каталоге проекта.
- [ ] Проверить, что database user имеет права только на базу `garagebalance_staging`.
- [ ] Не удалять Data Protection keys при deploy/rollback: без прежних ключей защищенные настройки интеграций нельзя расшифровать.

## 3. Сборка и миграции

- [ ] Перед обновлением получить актуальный код в отдельный release-каталог.
- [ ] Выполнить backend-тесты: `dotnet test`.
- [ ] Выполнить frontend-тесты: `npm run test -- --runInBand`.
- [ ] Выполнить production-сборку frontend: `npm run build`.
- [ ] Проверить форматирование backend: `dotnet format --verify-no-changes`.
- [ ] Сгенерировать idempotent SQL до применения миграций:

```bash
dotnet tool run dotnet-ef migrations script --idempotent \
  --project ./backend/GarageBalance.Api/GarageBalance.Api.csproj \
  --startup-project ./backend/GarageBalance.Api/GarageBalance.Api.csproj \
  --output ./artifacts/deploy-migrations.sql
```

- [ ] Сохранить SQL-скрипт как артефакт релиза.
- [ ] Применять миграции только после backup PostgreSQL.

## 3.1. Автоматический deploy через GitHub Actions

Workflow `.github/workflows/deploy-staging.yml` запускается при `push` в `master` и вручную через `workflow_dispatch`. Он не хранит секреты в репозитории и перед выкладкой выполняет обязательные проверки: privacy-check, backend-тесты, frontend-тесты, `dotnet format`, `npm run lint`, `npm run build`, bundle budget, генерацию idempotent SQL миграций и publish backend под `linux-x64`.

На GitHub должны быть заданы repository secrets:

- [ ] `VPS_HOST` - `31.192.110.221`.
- [ ] `VPS_DEPLOY_USER` - `garagebalance-deploy`.
- [ ] `VPS_SSH_KEY` - приватный SSH-ключ deploy-пользователя; ключ не хранится в Git и не вставляется в документы.

На VPS автоматический deploy должен идти только через root-owned скрипт `/usr/local/bin/garagebalance-deploy-apply`, установленный из `infrastructure/scripts/vps-apply-release.sh`.

- [ ] Deploy-пользователь не входит в группу `sudo`.
- [ ] В `/etc/sudoers.d/garagebalance-deploy` разрешен только запуск `/usr/local/bin/garagebalance-deploy-apply *` без пароля.
- [ ] GitHub Actions загружает артефакты только в `/home/garagebalance-deploy/uploads/<release-id>`.
- [ ] Серверный apply-скрипт сам создает `pg_dump`, применяет SQL миграций, заменяет только `/opt/garagebalance-staging/api` и `/opt/garagebalance-staging/frontend`, запускает health-check и возвращает предыдущие каталоги при ошибке.
- [ ] Перед включением автоматического deploy проверить вручную `sudo -l -U garagebalance-deploy` и убедиться, что лишних sudo-команд нет.

## 4. Backup перед обновлением

- [ ] Создать ручной backup перед миграциями, импортом или обновлением:

```bash
pg_dump --format=custom --file=/opt/garagebalance-staging/backups/garagebalance_$(date +%Y%m%d_%H%M%S).pgdump garagebalance_staging
```

- [ ] Проверить, что файл backup создан и не пустой.
- [ ] Зафиксировать имя backup-файла в истории работ.
- [ ] Для проверки восстановления использовать отдельную test-базу, а не рабочую.

## 5. systemd service

Рекомендуемое имя сервиса: `garagebalance-staging.service`.

Источник конфигурации хранится в
`infrastructure/deployment/garagebalance-staging.service`. Он задаёт
`Restart=on-failure`, ограничение памяти 1,2 ГБ, мягкий порог 900 МБ,
до 150% CPU и 512 задач, а также изоляцию файловой системы процесса.
Таймер `garagebalance-healthcheck.timer` проверяет локальный `/health/ready`
каждую минуту и перезапускает API только после трёх последовательных ошибок.

- [ ] Создать `/etc/systemd/system/garagebalance-staging.service`.
- [ ] Выполнить `systemctl daemon-reload`.
- [ ] Запустить `systemctl enable --now garagebalance-staging.service`.
- [ ] Проверить `systemctl status garagebalance-staging.service`.
- [ ] Проверить процесс: `curl -fsS -H "Host: sgk.blagodaty.ru" http://127.0.0.1:3101/health/live`; затем PostgreSQL и готовность приложения: `curl -fsS -H "Host: sgk.blagodaty.ru" http://127.0.0.1:3101/health/ready`.
- [ ] Проверить логи без вывода secrets: `journalctl -u garagebalance-staging.service -n 100 --no-pager`.

## 6. nginx и домен

Рекомендуемый файл: `/etc/nginx/sites-available/garagebalance-staging`.

Для отдельного безопасного журнала длительности создать `/etc/nginx/conf.d/garagebalance-timing.conf`. Формат использует `$uri`, а не `$request_uri`, поэтому поисковые строки и другие query-параметры в этот журнал не попадают:

```nginx
log_format garagebalance_timing '$time_iso8601 request_id=$request_id remote=$remote_addr method=$request_method uri=$uri status=$status bytes=$body_bytes_sent request_time=$request_time upstream_connect_time=$upstream_connect_time upstream_header_time=$upstream_header_time upstream_response_time=$upstream_response_time';
```

Полная воспроизводимая конфигурация хранится в
`infrastructure/deployment/garagebalance-staging.nginx.conf`. Она включает
HTTP/2, persistent upstream-соединения, явные connect/read/send timeout,
буферизацию API, ограничение медленных и чрезмерно частых соединений,
gzip для текстовой статики, immutable cache для hashed assets и `no-store`
для `index.html`. Прямой DNS-маршрут не использует Cloudflare-трансформацию,
поэтому прежние `gzip off` и `no-transform` не применяются.

Установка выполняется скриптом
`infrastructure/scripts/install-vps-performance-configuration.sh`: перед
заменой он создаёт резервные копии nginx и systemd, проверяет `nginx -t`,
после запуска проверяет локальный и публичный health и автоматически
возвращает предыдущие конфигурации при любой ошибке.

- [ ] Создать symlink в `/etc/nginx/sites-enabled`.
- [ ] Проверить конфигурацию: `nginx -t`.
- [ ] Перезагрузить nginx: `systemctl reload nginx`.
- [ ] Проверить HTTP до TLS: `curl -fsS http://sgk.blagodaty.ru/health/ready`.
- [ ] Проверить строку времени без query-параметров: `tail -n 20 /var/log/nginx/garagebalance-staging-timing.log`.
- [ ] Проверить сжатие hashed asset: `curl --http2 --compressed -sSI https://sgk.blagodaty.ru/assets/<entry>.js`.
- [ ] Проверить отсутствие лишнего HTTPS redirect: `curl --http2 -sSI https://sgk.blagodaty.ru/`.
- [ ] Проверить таймеры: `systemctl list-timers 'garagebalance-*'`.
- [ ] Проверить ротацию без изменения файлов: `logrotate --debug /etc/logrotate.d/garagebalance`.

## 7. TLS

- [ ] Убедиться, что DNS `sgk.blagodaty.ru` указывает на нужный VPS.
- [ ] Установить сертификат: `certbot --nginx -d sgk.blagodaty.ru`.
- [ ] Повторно проверить `nginx -t`.
- [ ] Перезагрузить nginx: `systemctl reload nginx`.
- [ ] Проверить HTTPS readiness: `curl -fsS https://sgk.blagodaty.ru/health/ready`.
- [ ] Проверить, что опубликованный `appsettings.json` задает уровень `Warning` для категории `Microsoft.EntityFrameworkCore.Database.Command` и рабочее окружение не пишет каждый успешно выполненный SQL-запрос.
- [ ] Проверить `Server-Timing: app;dur=...` в ответе API и отсутствие предупреждений `SlowHttpRequest` для обычных экранов.
- [ ] Проверить главную страницу с телефона и desktop-браузера.
- [ ] Проверить, что `index.html` и SPA fallback отдаются с `Cache-Control: no-store`.

## 8. Smoke-проверка после выкладки

- [ ] Открыть `https://sgk.blagodaty.ru`.
- [ ] Создать первого администратора, если база пустая.
- [ ] Войти под администратором.
- [ ] Проверить разделы: пользователи, справочники, платежи, отчеты, импорт, audit, "Что нового".
- [ ] Проверить `GET /api/releases`.
- [ ] Проверить, что защищенные разделы без входа не открываются.
- [ ] Проверить, что frontend не остается на старой версии после hard refresh на телефоне.

## 9. Rollback

- [ ] Не удалять предыдущий release-каталог до завершения приемки.
- [ ] При ошибке остановить сервис: `systemctl stop garagebalance-staging.service`.
- [ ] Вернуть symlink/каталог frontend и backend на предыдущую версию.
- [ ] При необходимости восстановить backup в отдельную проверочную базу.
- [ ] Только после проверки восстановленной базы переключать рабочую БД.
- [ ] Запустить сервис и проверить `curl -fsS https://sgk.blagodaty.ru/health/ready`.
- [ ] Записать причину rollback и результат проверки в журнал технических работ.

## 10. Что нельзя делать

- [ ] Не менять существующие nginx-сайты без предварительного просмотра `nginx -T`.
- [ ] Не использовать общую БД другого проекта.
- [ ] Не запускать миграции без свежего `pg_dump`.
- [ ] Не коммитить `/etc/garagebalance-staging.env`, backup, `.accdb`, `.mdb`, `.pgdump`, `.sql.gz`.
- [ ] Не публиковать проект в Git и не выполнять push без отдельного разрешения пользователя.

## 11. Условия финального закрытия VPS/domain deployment

- [ ] GitHub Secrets `VPS_HOST`, `VPS_DEPLOY_USER` и `VPS_SSH_KEY` добавлены в репозиторий на GitHub без раскрытия в Git.
- [ ] На VPS проверены `sudo -l -U garagebalance-deploy`, `/etc/sudoers.d/garagebalance-deploy`, права `/etc/garagebalance-staging.env` и отсутствие лишних sudo-команд.
- [ ] Workflow `.github/workflows/deploy-staging.yml` успешно прошел до конца на актуальном коммите.
- [ ] Apply-скрипт `/usr/local/bin/garagebalance-deploy-apply <release-id>` создал непустой `pg_dump`, применил idempotent SQL, заменил API/frontend и сохранил предыдущие каталоги для rollback.
- [ ] Проверены `systemctl status garagebalance-staging.service`, `journalctl -u garagebalance-staging.service -n 100 --no-pager`, `nginx -t` и `systemctl reload nginx`.
- [ ] Проверены `curl -fsS https://sgk.blagodaty.ru/health/live`, `curl -fsS https://sgk.blagodaty.ru/health/ready`, открытие `https://sgk.blagodaty.ru` на desktop/mobile и hard refresh frontend.
- [ ] Выполнен smoke входа, справочников, платежей, отчетов, импорта dry-run, audit и "Что нового".
- [ ] В журнале технических работ записаны commit, workflow run, release id, backup-файл, health-check и все оставшиеся блокеры.
