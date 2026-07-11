# Income Excel Scenario Verification

Этот документ фиксирует evidence для закрытия roadmap-пункта Stage 6 "Реализовать Excel-сценарий поступлений".

## Закрытая Область

- [x] Выбор гаража выполняется через поиск по номеру гаража или ФИО владельца.
- [x] После выбора гаража показывается шапка карточки с номером, владельцем и телефоном.
- [x] Поступления загружаются из backend-ведомости `getGarageIncomeWorksheet`.
- [x] Ведомость показывает помесячные строки от новых месяцев к старым через grouping/sorting UI.
- [x] Таблица содержит колонки `Платёж`, `Оплачено` и `Задолженность`.
- [x] Прямой ввод суммы в ячейку `Платёж` сохраняет поступление через `createIncome`.
- [x] После Enter ячейка платежа очищается, а строка обновляет `paid` и `debt`.
- [x] История платежей гаража пополняется новой операцией и затем перезагружается из backend.
- [x] Итоги кассы и банка остаются в форме платежей и учитывают созданные финансовые операции.
- [x] Пустой или недоступный backend worksheet не подставляет старые prototype-строки.

## Проверочное Покрытие

- [x] `FinanceServiceTests.GetGarageIncomeWorksheetAsync_BuildsRowsFromAccrualsPaymentsAndMeters` проверяет сборку ведомости из начислений, поступлений и показаний.
- [x] `FinanceServiceTests.GetGarageIncomeWorksheetAsync_CarriesOpeningDebtIntoPeriodTotals` проверяет входящий долг и итоги периода.
- [x] `FinanceControllerTests.GetGarageIncomeWorksheet_PassesGarageAndPeriodToService` проверяет HTTP-surface ведомости.
- [x] `FinanceControllerTests.GetGarageIncomeWorksheet_ReturnsNotFoundForMissingGarage` проверяет отсутствующий гараж.
- [x] `FinanceControllerTests.GetGarageIncomeWorksheet_ReturnsBadRequestForInvalidPeriod` проверяет невалидный период.
- [x] `App.test.tsx` проверяет выбор гаража через поиск, загрузку backend worksheet, шапку карточки, период, таблицу, прямой ввод платежа, очистку ячейки после Enter, пересчет сумм и защиту от stale prototype rows.
- [x] `ProjectWideRoadmapStatusTests.IncomeExcelScenarioRoadmapItemIsCompleteWhenWorksheetPaymentCellCashAndHistoryAreCovered` закрепляет этот evidence.

## Acceptance Notes

- [x] Новая запись "Что нового" не нужна для этого среза: пользовательская возможность формы поступлений и платежной истории уже описана существующими release notes, а текущая задача закрывает roadmap-status/evidence.
- [x] Локальная PostgreSQL-проверка выполняется через idempotent EF migration script, если локальный PostgreSQL/psql/docker недоступны.
