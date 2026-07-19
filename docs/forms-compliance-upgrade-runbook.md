# Обновление данных для соответствия формам ГСК

Этот runbook описывает безопасное применение migrations и backfill, которые обеспечивают просрочку, FIFO-распределение поступлений, годовые обязательства, показания счетчиков, системные назначения доходов и движение фондов. Он дополняет `docs/migration-verification-checklist.md`, `docs/postgres-backup-restore.md` и инструкции конкретного способа установки.

Команды выполняются только в согласованное окно обслуживания. Имена backup и агрегированные результаты проверки можно записывать в журнал работ; connection string, пароль, `.pgdump` и строки с персональными или финансовыми данными в Git и диагностические сообщения не переносятся.

## 1. Состав обновления и backfill

Порядок migrations определяется EF Core и не изменяется вручную:

| Migration | Что меняется | Что происходит с историческими данными |
| --- | --- | --- |
| `20260717042446_AccrualDueDateSnapshots` | В начислении сохраняются срок оплаты и начало просрочки. | Однозначные строки получают детерминированные снимки срока. |
| `20260717044239_AccrualPaymentAllocations` и `20260717050320_AccrualPaymentAllocationAmountConstraint` | Создается журнал `поступление → начисление` с положительной суммой. | Активные поступления распределяются FIFO внутри пары `гараж + вид поступления`; остаток остается нераспределенной переплатой. |
| `20260717073701_HistoricalAccrualDueDateReconciliation` | Добавляется признак ручной сверки исторического срока. | Неоднозначные строки помечаются `DueDateNeedsReview`; их распределения отключаются до подтверждения. |
| `20260717124326_DistinguishCashToBankFundTransfers` | Сдача кассы в банк отделяется от обычного изъятия фонда. | Однозначные исторические операции получают соответствующий признак. |
| `20260717140011_MeterReadingOptimisticConcurrency` | Показания получают версию конкурентного изменения. | Существующие строки получают исходную версию без изменения показаний. |
| `20260718135608_AnnualAccrualAccountingYear` | Для годовых начислений сохраняется учетный год. | Членский, целевой взнос и наружное освещение получают год из `AccountingMonth`. |
| `20260718160621_AddStableIncomeDestinations` | Создаются стабильные коды `other_payments` и `other_income` и связь с фондом. | Подходящие системные назначения восстанавливаются без создания дублей; действие фиксируется в audit. |
| `20260718163348_RouteIrregularAccrualsToOtherPayments` и `20260718172521_RouteFeeCampaignAccrualsToOtherIncome` | Начисления связываются с разовой оплатой или объявленным сбором. | Только однозначно найденные исторические сборы получают связь; каталоги сборов направляются в `other_income`. |
| `20260718183543_LinkIncomeFundAssignments` | Операция фонда связывается с исходным поступлением. | Для направляемых поступлений создается не более одной операции фонда, пересчитываются остатки и пишется audit; при недостаточном нераспределенном остатке migration останавливается. |

Backfill не должен исправляться ручным редактированием `__EFMigrationsHistory`. Если migration остановилась на защитной проверке, рабочая база остается объектом расследования: сохранить ошибку без чувствительных данных, восстановить копию в отдельную базу и сначала устранить расхождение там.

## 2. До обновления

1. Остановить ввод финансовых операций и зафиксировать начало окна обслуживания.
2. Записать текущий commit/release и последнюю примененную migration:

```sql
SELECT "MigrationId"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId" DESC
LIMIT 1;
```

3. Проверить PostgreSQL и клиентские инструменты:

```powershell
.\infrastructure\scripts\check-local-postgres.ps1 `
  -Database garagebalance_local -HostName 127.0.0.1 -Port 5432 `
  -Username garagebalance_local -RequirePsql
```

4. Создать custom-format backup и сохранить выведенный `backupPath` вне каталога Git:

```powershell
.\infrastructure\scripts\backup-postgres.ps1 `
  -Database garagebalance_local -HostName 127.0.0.1 -Port 5432 `
  -Username garagebalance_local -BackupDirectory C:\GarageBalance\Backups
```

5. Обязательно проверить восстановление, не затрагивая рабочую базу:

```powershell
.\infrastructure\scripts\restore-postgres.ps1 `
  -BackupFile C:\GarageBalance\Backups\ИМЯ_КОПИИ.pgdump `
  -TargetDatabase garagebalance_restore_check -DropAndCreate
```

6. На восстановленной базе проверить количество migrations, активных/отмененных начислений и операций, а также только агрегированные денежные итоги. Эти значения становятся контрольной точкой:

```sql
SELECT 'accruals' AS entity, "IsCanceled", COUNT(*) AS rows, COALESCE(SUM("Amount"), 0) AS total
FROM accruals GROUP BY "IsCanceled"
UNION ALL
SELECT 'financial_operations', "IsCanceled", COUNT(*), COALESCE(SUM("Amount"), 0)
FROM financial_operations GROUP BY "IsCanceled";

SELECT COUNT(*) AS migration_count, MAX("MigrationId") AS latest_migration
FROM "__EFMigrationsHistory";
```

Если backup не создан, пуст, не читается `pg_restore` или не восстанавливается в проверочную базу, обновление не начинать.

## 3. Применение

Сначала сформировать и сохранить как временный артефакт непустой idempotent SQL:

```powershell
.\infrastructure\scripts\generate-migration-script.ps1 `
  -OutputPath artifacts\deploy-migrations.sql
```

Для локальной установки API применяет ожидающие migrations при старте только после успешного предобновляющего backup. Для ручной изолированной проверки используется EF Core:

```powershell
$env:ConnectionStrings__DefaultConnection="Host=127.0.0.1;Port=5432;Database=garagebalance_upgrade_check;Username=garagebalance_local;Password=REPLACE_WITH_SECRET"
dotnet tool run dotnet-ef database update `
  --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj `
  --startup-project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
```

Повторный `database update` обязан завершиться без новых изменений. На VPS и в Docker использовать соответствующий deployment-runbook; не обходить обязательный backup и health-check прямым запуском SQL в рабочей базе.

## 4. Автоматическая сверка после обновления

Следующие запросы возвращают только агрегаты и технические идентификаторы. Для успешного обновления все поля с префиксом `invalid_` должны быть равны нулю, оба системных назначения должны присутствовать по одному, а последняя migration должна быть `20260718183543_LinkIncomeFundAssignments` или новее.

```sql
SELECT COUNT(*) AS invalid_allocations
FROM accrual_payment_allocations allocation
JOIN accruals accrual ON accrual."Id" = allocation."AccrualId"
JOIN financial_operations payment ON payment."Id" = allocation."FinancialOperationId"
WHERE allocation."IsActive"
  AND (allocation."Amount" <= 0
       OR accrual."IsCanceled"
       OR payment."IsCanceled"
       OR payment."OperationKind" <> 'income'
       OR accrual."GarageId" IS DISTINCT FROM payment."GarageId"
       OR accrual."IncomeTypeId" IS DISTINCT FROM payment."IncomeTypeId");

SELECT COUNT(*) AS invalid_annual_years
FROM accruals accrual
JOIN income_types income_type ON income_type."Id" = accrual."IncomeTypeId"
WHERE LOWER(BTRIM(COALESCE(income_type."Code", ''))) IN ('membership', 'target', 'outdoor_lighting')
  AND accrual."AccountingYear" IS DISTINCT FROM EXTRACT(YEAR FROM accrual."AccountingMonth")::integer;

SELECT LOWER(BTRIM("Code")) AS code, COUNT(*) AS rows,
       BOOL_AND("IsSystem" AND NOT "IsArchived" AND "DestinationFundId" IS NOT NULL) AS valid
FROM income_types
WHERE LOWER(BTRIM(COALESCE("Code", ''))) IN ('other_payments', 'other_income')
GROUP BY LOWER(BTRIM("Code"));

SELECT COUNT(*) AS invalid_fund_assignments
FROM fund_operations assignment
JOIN financial_operations source ON source."Id" = assignment."SourceFinancialOperationId"
JOIN income_types income_type ON income_type."Id" = source."IncomeTypeId"
WHERE assignment."FundId" IS DISTINCT FROM income_type."DestinationFundId"
   OR assignment."Amount" IS DISTINCT FROM source."Amount"
   OR assignment."IsCanceled" IS DISTINCT FROM source."IsCanceled";

SELECT COUNT(*) AS invalid_fund_balances
FROM funds fund
WHERE fund."Balance" IS DISTINCT FROM COALESCE((
    SELECT SUM(CASE WHEN operation."OperationKind" = 'deposit' THEN operation."Amount" ELSE -operation."Amount" END)
    FROM fund_operations operation
    WHERE operation."FundId" = fund."Id" AND NOT operation."IsCanceled"), 0);

SELECT "DueDateReviewReason", COUNT(*) AS rows
FROM accruals
WHERE "DueDateNeedsReview"
GROUP BY "DueDateReviewReason"
ORDER BY "DueDateReviewReason";
```

Строки `DueDateNeedsReview` не являются ошибкой migration: это очередь ручной бухгалтерской сверки. Причина должна входить в известный набор `regular_service_not_unique`, `fee_campaign_not_unique`, `historical_source_unknown`; после подтвержденного исправления повторно проверить просрочку и FIFO-распределения.

## 5. Функциональная сверка

После успешных SQL-проверок:

- `/health` отвечает успешно, в логах API/PostgreSQL нет ошибок migration, отсутствующих колонок или ограничений;
- вход администратора и раздел «Что нового» открываются;
- в гараже совпадают общий баланс, просрочка и расшифровка непогашенных начислений;
- частичная оплата не скрывает годовое обязательство, полная оплата скрывает его из списка долга;
- в выплатах долг/аванс переносится только внутри пары `поставщик + вид выплаты`;
- сохранение показания пересчитывает связанную услугу, конфликт версии дает понятную ошибку без потери введенных данных;
- `other_payments` и `other_income` существуют по одному и направляют новые поступления в связанный фонд;
- остаток каждого фонда совпадает с движением операций;
- восемь вкладок отчетов, XLSX и PDF используют те же фильтры и итоги полной выборки.

Сравнить агрегаты начислений и финансовых операций с контрольной точкой до обновления. Допустимы только документированные новые технические строки распределений, связей и audit; исходные суммы и признаки отмены не должны самопроизвольно меняться.

## 6. Rollback

Rollback кода и rollback данных — разные операции. Возврат предыдущих файлов приложения не откатывает PostgreSQL-схему.

1. При ошибке остановить API и запретить новые записи.
2. Сохранить логи без секретов и создать отдельный аварийный backup текущего состояния для расследования.
3. Не выполнять `dotnet-ef database update <СТАРАЯ_MIGRATION>` на рабочей базе и не редактировать `__EFMigrationsHistory`: `Down` удаляет новые связи/журналы и не восстанавливает исходное состояние всех backfill.
4. Проверить предобновляющий `.pgdump` через восстановление в `garagebalance_restore_check` и повторить контрольные агрегаты.
5. Если предыдущий код явно совместим с новой схемой, можно вернуть только приложение, сохранив новую БД. Совместимость должна быть подтверждена тестом, а не предположением.
6. Если требуется возврат данных, восстановить целиком проверенный предобновляющий backup в заново созданную рабочую базу только по отдельному подтверждению администратора. Все записи после начала окна обслуживания будут потеряны или должны быть отдельно согласованно перенесены.
7. Вернуть предыдущий код, запустить `/health`, выполнить функциональную сверку и записать причину/результат rollback в журнал работ.

До приемки обновления не удалять предыдущий образ/каталог приложения, предобновляющий backup и защищенную копию Data Protection keys. Сам backup и ключи не прикладывать к roadmap или commit.

## 7. Критерии завершения обновления

- backup создан, непустой и успешно восстановлен в отдельную проверочную базу;
- idempotent SQL сформирован, migrations применены и повторный запуск ничего не изменил;
- последняя migration и агрегаты до/после записаны без чувствительных данных;
- все `invalid_*` проверки вернули `0`, системные коды уникальны и связаны с фондами;
- очередь `DueDateNeedsReview` либо пуста, либо передана на ручную сверку с количеством по причинам;
- `/health`, ключевые финансовые сценарии и восемь отчетов проверены;
- в логах нет необъясненных ошибок, временные базы/SQL удалены или оставлены с указанной причиной;
- backup и предыдущая версия сохраняются до ручной приемки результата.
