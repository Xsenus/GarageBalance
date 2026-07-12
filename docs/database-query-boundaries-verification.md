# Database Query Boundaries Verification

Этот документ фиксирует evidence для закрытия архитектурного правила server-side filtering, aggregation и bounds текущих списков/отчетов.

## Растущие Списки

- [x] Пользователи используют normalized limit и page-запросы с `CountAsync`, `Skip` и `Take`.
- [x] Audit history использует server filters, `CountAsync`, `Skip` и `Take` до materialization.
- [x] Владельцы, гаражи, поставщики и остальные рабочие справочники используют normalized backend limits; индексируемый поиск выполняется в БД.
- [x] Платежи, начисления, начисления поставщикам и показания имеют bounded list/page queries.
- [x] Access runs, log, created records и quarantine имеют default/max limits; created records ограничены 100/500.
- [x] Операции фондов ограничены max 100 в PostgreSQL и SQLite ветках.
- [x] Публичный и административный списки "Что нового" отдают default 10 / max 50 записей.

## Отчеты

- [x] Сводный отчет ограничивает видимые гаражные строки и считает полные итоги PostgreSQL-агрегатами.
- [x] Отчеты поступлений и выплат без text search ограничивают starting/accrual/payment rows до materialization и отдельно считают totals/counts.
- [x] Отчет изменения фондов выполняет search/count/deposit/withdrawal totals и limit в PostgreSQL.
- [x] Отчеты оплат из кассы и сдачи кассы в банк выполняют search/count/sum и limit в PostgreSQL.
- [x] Отчет сборов агрегирует исторические начисления/платежи в PostgreSQL до materialization по гаражам и видам поступлений.
- [x] Export requests используют `Limit=null` и получают полный отфильтрованный набор; экранные requests передают явный limit.

## Автоматические Гарантии

- [x] `BackendPerformanceGuardTests` проверяет query shape пользователей, audit, справочников, финансов, импорта, фондов, releases и отчетов.
- [x] Service tests проверяют, что visible limits не искажают полные totals/rowCount.
- [x] `ProjectWideRoadmapStatusTests.DatabaseQueryBoundariesAreCompleteForCurrentListsReportsImportsFundsAndReleases` удерживает roadmap-status вместе с matrix.

## Отдельная Приемка

- [x] Это архитектурное закрытие не заменяет живой PostgreSQL performance-run: timings, logs и `EXPLAIN (ANALYZE, BUFFERS)` на реалистичном объеме остаются отдельными `[~]`/`[acceptance]` пунктами roadmap.

## Future Rule

- [x] Новый растущий list/report endpoint добавляется только с server bounds/aggregation, service tests и обновлением performance guard.

## Release Notes

- [x] Новая запись "Что нового" не нужна: production-код и пользовательское поведение в этом test/evidence-срезе не меняются.
