# Export Filter And Permission Verification

## Scope

Пункт Stage 7 "Проверить, что экспорт учитывает те же фильтры и права, что и экранный отчет" закрывается как уже реализованный для текущих XLSX/PDF exports. Production-код этим шагом не менялся: задача состояла в сверке backend contracts, frontend API clients, существующих тестов и roadmap-статуса.

## Реализовано

- Все endpoints `api/reports/*` находятся в `ReportsController` под class-level `[Authorize(Policy = SystemPermissions.ReportsRead)]`, поэтому экранные отчеты и XLSX/PDF exports требуют одно и то же право `reports.read`.
- Экспортные endpoints используют POST, потому что формируют audit events, но принимают те же фильтры, что соответствующие экранные GET endpoints.
- Backend exports создают те же request DTO, что экранные отчеты: `ConsolidatedReportRequest`, `IncomeReportRequest`, `ExpenseReportRequest`, `FundChangeReportRequest`, `CashPaymentReportRequest`, `BankDepositReportRequest`, `FeeReportRequest`.
- Frontend `reportsApi` строит query для экранных отчетов и exports через одни и те же builders: `buildConsolidatedReportQuery`, `buildIncomeReportQuery`, `buildExpenseReportQuery`, `buildFundChangeReportQuery`, `buildCashPaymentReportQuery`, `buildBankDepositReportQuery`, `buildFeeReportQuery`.

## Тестовое покрытие

- `ReportsControllerTests` закрепляет POST routes, file responses, invalid-period errors и передачу фильтров в request DTO для export endpoints.
- `ReportServiceTests` проверяет filtered rows для consolidated, income, expense, cash-payments, bank-deposits, fees и fund-changes XLSX/PDF exports.
- `reportsApi.test.ts` проверяет, что frontend exports отправляют те же query-параметры, включая period, search, rowMode, garageIds, ownerIds, incomeTypeIds, supplierIds, expenseTypeIds и variation.
- `App.test.tsx` покрывает передачу текущих экранных фильтров в export-клиенты для report tabs.
- Guard `ReportExportFiltersAndPermissionsRoadmapItemIsCompleteWhenExportsShareScreenContracts` связывает закрытый roadmap-статус с фактическими controller/service/frontend признаками.

## Осталось

- Для новых отчетов нужно добавлять export через тот же pattern: общий request DTO, общий frontend query builder, `reports.read`, POST для audit и отдельные tests на фильтры.
- Новая запись "Что нового" не нужна: пользовательское поведение, API, бизнес-правила и release notes не менялись этим шагом; закрыт только roadmap-status/evidence по уже существующей функциональности.
