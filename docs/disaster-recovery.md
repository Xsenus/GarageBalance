# Независимое восстановление и еженедельная проверка

Рабочий механизм — команды `GarageBalance.StorageTool` ниже. Старый `recovery-bundle.ps1` создаёт только локальный архив v1 и не доказывает независимую защиту; `restore-postgres.ps1` проверяет только структуру дампа. Для полной проверки нужны **архив v2, независимая копия БД, ключи, существующий защищённый контрольный секрет и запуск настоящего API**.

## Что хранится независимо от основной БД

- Политика `RecoverySecrets` в отдельном pool обязана требовать как минимум два разных `FailureDomain`. Поддерживается до 100 назначений; совпадающие домены не увеличивают защиту. Для S3 сохраняются общие private/TLS/encryption/allowlist ограничения. Две папки одного компьютера допустимы лишь как тестовые имитации, не как независимая production-защита.
- AES-256-GCM архив содержит XML-кольцо Data Protection, защищённый контрольный секрет, типизированные настройки Storage/DatabaseBackup без секретов, версию приложения/список миграций и полный постранично прочитанный каталог резервных копий: поколения, размеры, SHA-256, реальные locators, состояния и tombstone. В архив не включаются `.env`, connection strings, облачные credentials или пароль шифрования.
- Маленький bootstrap JSON содержит указатели на зашифрованный архив в base64 и HMAC-SHA-256 подпись (base64 **не является шифрованием**); в нём есть технические locators, поэтому хранить его нужно в закрытом операторском хранилище. Указатели bootstrap аутентифицируются независимым ключом и позволяют найти архив без PostgreSQL.
- Ключ шифрования — отдельный случайный 32-байтовый base64-секрет. Его нельзя хранить в кольце ключей, рядом с bootstrap или внутри назначения копий. Права на файл: только оператор восстановления. Credentials читателя провайдера выдаются отдельно и также не зависят от production-БД.
- Начальные параметры подключения хотя бы к одному recovery-провайдеру (ID, endpoint, bucket, prefix и region без credentials) сохраняются оператором в конфигурационном управлении отдельно от БД: они нужны, чтобы скачать зашифрованный архив. После расшифровки полный сохранённый typed config извлекается в `appsettings.Recovery.json` вместе с версией приложения, tenant/policy и миграциями. Архив не пытается угадывать или копировать произвольную секретную конфигурацию сервера.

## Первичная подготовка

Сначала создать обычную резервную копию, дождаться независимых реплик и выполнить `cutover-check`. Опубликовать обе .NET-программы одной версии в закрытый операторский каталог:

```powershell
dotnet publish backend/GarageBalance.StorageTool -c Release -o C:\GarageBalanceRecovery\tool
dotnet publish backend/GarageBalance.Api -c Release -o C:\GarageBalanceRecovery\api
```

Пример отдельной политики в настройках `Storage` (существующие назначения должны быть уже проверены):

```json
{
  "Pools": [{ "Id": "recovery-secrets", "DestinationIds": ["offsite-a", "offsite-b"] }],
  "Policies": [{
    "Id": "recovery-secrets", "DataClass": "RecoverySecrets", "PoolId": "recovery-secrets",
    "RequiredIndependentCopies": 2, "DesiredCopies": 2, "MinimumOffsiteCopies": 2,
    "RequiredCapabilities": ["Read", "Write", "Stat"]
  }]
}
```

Это **добавление** к существующим массивам, не замена политики `DatabaseBackup`.

Один раз создать контрольный секрет существующим кольцом приложения. Команда не меняет БД и отказывается перезаписывать прежний canary:

```powershell
dotnet C:\GarageBalanceRecovery\tool\GarageBalance.StorageTool.dll recovery-canary --execute --key-ring C:\GarageBalance\Keys --canary C:\GarageBalanceRecovery\canary.json
```

Кольцо должно быть уже создано приложением; инструмент не создаёт новое взамен потерянного. Затем после каждой новой резервной копии/ротации ключей опубликовать новый снимок:

```powershell
dotnet C:\GarageBalanceRecovery\tool\GarageBalance.StorageTool.dll recovery-publish --execute --key-ring C:\GarageBalance\Keys --canary C:\GarageBalanceRecovery\canary.json --key-file E:\IndependentSecrets\recovery.key --bootstrap C:\GarageBalanceRecovery\bootstrap.json
```

Для публикации нужны текущая `ConnectionStrings__DefaultConnection` и конфигурация `Storage`. Архив ограничен 64 MiB и 100 000 записями каталога: превышение — ошибка, не тихое усечение. После записи **каждой** копии инструмент заново читает её целиком и сверяет SHA/размер, атомарно сохраняет подписанный partial bootstrap в отдельный `.pending.json`. При неполной защите код `21`; сохранённые копии не удаляются и предыдущий успешный bootstrap не заменяется. Повторная публикация создаёт новый immutable архив; основной bootstrap атомарно заменяется только после полной защиты. Автоматического retention секретных архивов нет.

Скопировать успешный bootstrap в независимое операторское хранилище. Bootstrap без ключа и ключ без доступных провайдеров не обеспечивают восстановление. После изменения состава копий/удаления backup публиковать новый снимок: старый bootstrap — историческая точка восстановления, не актуальная проверка retention.

## Проверка после потери основной БД

Указать провайдеры и их read-only credentials из независимого операторского контура. `recovery-fetch` и `restore-drill` **не открывают production connection string**.

```powershell
dotnet C:\GarageBalanceRecovery\tool\GarageBalance.StorageTool.dll recovery-fetch --execute --bootstrap C:\GarageBalanceRecovery\bootstrap.json --key-file E:\IndependentSecrets\recovery.key --output C:\GarageBalanceRecovery\recovered-new
```

Команда проверяет HMAC bootstrap, SHA полученных байтов, AES-GCM, tenant/bundle identity и расшифровку прежнего canary. При недоступности/повреждении первой копии пробует следующую. Результат содержит восстановленные ключи и каталог: это намеренная приватная выдача оператору, после использования её удалить безопасно; каталог должен быть новым и ACL-защищённым заранее на уровне родителя.

## Полная проверка без изменения production

Нужен отдельный PostgreSQL на loopback, предпочтительно отдельный сервер/контейнер восстановления. У пользователя PostgreSQL должны быть права создать и удалить только проверочные базы; **не использовать production superuser и не запускать недоверенный дамп**. Инструмент требует `Database=postgres`, числовой loopback host и отдельный `Recovery__DrillConnectionString`. Он сам создаёт новое имя `gb_restore_<случайный GUID>` и никогда не принимает целевое имя БД от оператора. Пароль передавать через закрытые переменные окружения/secret manager, не аргументом команды.

Обязательные настройки:

```text
Recovery__DrillConnectionString=<отдельное loopback-соединение оператора>
Storage__Recovery__VerificationReportPath=C:\GarageBalanceRecovery\status\verification.json
Storage__Recovery__MaximumDrillSeconds=1800
Storage__Recovery__MaximumBackupBytes=21474836480
Storage__Recovery__MaximumVerificationAgeHours=168
```

`DatabaseBackup__PgRestorePath` при необходимости указывает соответствующую версию `pg_restore`. Для GUI API настройка `VerificationReportPath` указывает на тот же безопасный отчёт (API нужен только read-доступ).

```powershell
.\infrastructure\scripts\invoke-recovery-drill.ps1 -StorageToolDll C:\GarageBalanceRecovery\tool\GarageBalance.StorageTool.dll -ApiDll C:\GarageBalanceRecovery\api\GarageBalance.Api.dll -BootstrapFile C:\GarageBalanceRecovery\bootstrap.json -EncryptionKeyFile E:\IndependentSecrets\recovery.key -PrimaryFailureDomain primary-host
```

Проверка выбирает самую свежую доступную текущую копию вне исключённого домена исходного компьютера; исключает stale/corrupt/deleted/tombstoned. Затем потоково скачивает с лимитом размера и дедлайном, сверяет SHA/TOC, восстанавливает новую БД, проверяет EF migrations и чтение таблиц, расшифровывает прежний секрет. В копии создаётся случайная временная учётная запись **только с чтением отчётов**. Настоящий API запускается на случайном loopback-порту без наследования production credentials, без фоновых задач, миграций/инициализаторов и внешних интеграций. Разрешены только `/health`, вход, текущий пользователь и чтение сводного отчёта; остальные HTTP-маршруты блокируются. Успех подтверждается настоящими HTTP login/report, не имитацией ответа.

По успеху, исключению и отмене `finally` останавливает API, удаляет созданную БД и временные dump/keys. Ошибка очистки не выдаётся за успех. Если машину аварийно выключили/процесс принудительно убили, отчёт остаётся `in_progress`/устаревшим; оператор проверяет остатки только с соответствующим `runId`, а не удаляет все базы/папки по маске.

Отчёт записывается атомарно вне основной БД, без credentials, SQL, путей и PII. Поля: `schemaVersion`, `runId`, `startedAtUtc`, `completedAtUtc`, `succeeded`, `backupCreatedAtUtc`, `rtoSeconds`, `sourceDestinationId`, `errorCode`. RTO — фактическая длительность этой проверки; RPO определяется временем `backupCreatedAtUtc`, а не временем доставки архивов. Устаревшая удачная проверка не означает готовность текущих копий. Незавершённый запуск считается устаревшим после `MaximumDrillSeconds` плюс 60 секунд на очистку, а не после недельного интервала; API и операторское задание должны использовать одинаковую настройку дедлайна.

## Недельный запуск и наблюдение

Шаблоны `infrastructure/deployment/garagebalance-restore-drill.service` и `.timer` **не устанавливаются автоматически**. Оператор выбирает отдельный сервисный аккаунт, пути, домен исходного хоста, read-only credentials, secret injection для `/run/credentials/garagebalance-recovery.key` и отдельный restore PostgreSQL. После ручного теста включить timer (пример: воскресенье 04:00 с разбросом 15 минут). У сервиса закрытый `UMask=0077`, отдельный `/var/lib/garagebalance-recovery` и private tmp. Установленный API должен иметь read-only доступ к статусу, но не к ключу шифрования.

Отдельные `garagebalance-recovery-publish.service`/`.timer` обновляют снимок каталога и ключей ежечасно (до 5 минут разброса). У publisher — отдельный аккаунт с чтением production-каталога/кольца и записью recovery-копий; у drill — чтение провайдеров и доступ лишь к отдельному проверочному PostgreSQL. Перед первым включением проверить оба задания вручную. Для меньшего RPO вызывать publisher после завершения защиты каждого backup; часовой timer служит безопасным catch-up. Период выбран как пример, его утверждает оператор вместе с расписанием создания дампов.

На Windows создать недельное задание Task Scheduler, запускающее `pwsh.exe -NoProfile -NonInteractive -File ...\invoke-recovery-drill.ps1` с теми же **несекретными** параметрами. Переменные/секреты предоставляет аккаунт задания; не помещать пароль в XML/командную строку. Запретить параллельные запуски; инструмент дополнительно держит эксклюзивный `.lock` рядом с отчётом. Файл блокировки сохраняется намеренно как стабильный объект синхронизации.

Коды `20` (настройка/ввод/архив), `21` (недостаточно recovery-копий), `22` (полная проверка неуспешна), `130` (отмена) должны вызывать операторское уведомление штатным мониторингом. Проверять и возраст последнего успеха: выключенный scheduler не создаёт новых ошибок сам по себе.

## Граница готовности

Автотесты проходят на реальном локальном PostgreSQL с отдельными каталогами, имитирующими failure domains, потерянными исходными ключами/дампом и недоступной production connection string. Это доказывает локальный механизм, **не независимость дисков/аккаунтов заказчика**. До эксплуатации обязательны реальные A/B credentials/resources, соответствие RPO/RTO, проверка прав/квот/сетевых отказов и отдельная приёмка восстановления на инфраструктуре заказчика. Production restore остаётся только ручной операцией с согласованным окном; таймер его никогда не запускает.
