# Income Report Verification

## Scope

Пункт Stage 7 "Реализовать отчет по поступлениям" закрывается как уже реализованный и покрытый проверками. Production-код этим шагом не менялся: задача состояла в повторной сверке готового API/UI/export-поведения, фиксации доказательств и защите статуса roadmap guard-тестом.

## Реализовано

- Backend endpoint `GET /api/reports/income` возвращает отчет по поступлениям с фильтрами периода, поиска, гаражей, владельцев, видов поступлений и режима строк `all`/`accruals`/`payments`.
- Backend export endpoints `POST /api/reports/income/export/xlsx` и `POST /api/reports/income/export/pdf` формируют файлы через тот же набор фильтров и пишут audit export-события без сырой строки поиска.
- `ReportService` считает строки начислений, оплат, opening debt, остаток долга после платежа, итоги начислено/оплачено/долг и сохраняет bounded row limit без изменения totals.
- Frontend `reportsApi` передает фильтры поступлений в экранный отчет и оба export endpoint через стабильные query parameters.
- Экран "Отчеты" содержит вкладку "Поступления", таблицу "Отчет по поступлениям", фильтры дат, быстрый период "Сегодня", поиск, мультивыборы и режим строк.
- `reportFilters` сохраняет и восстанавливает `garageIds`, `ownerIds`, `incomeTypeIds`, `rowMode`, а validation блокирует обратный период до запроса к backend.

## Тестовое покрытие

- `ReportServiceTests`: `GetIncomeReportAsync_ReturnsAccrualAndPaymentRows`, `GetIncomeReportAsync_AppliesRowLimitWithoutChangingTotals`, `GetIncomeReportAsync_ReturnsDebtAfterEachPayment`, `GetIncomeReportAsync_IncludesGarageStartingBalanceAsDebt`, `GetIncomeReportAsync_FiltersByOwnerIncomeTypeAndRowMode`, `GetIncomeReportAsync_ReturnsErrorForInvalidPeriod`, `ExportIncomeReportXlsxAsync_ReturnsWorkbookWithFilteredRows`, `ExportIncomeReportPdfAsync_ReturnsDocumentWithFilteredRows`, `ExportIncomeReportXlsxAsync_WritesGeneratedAndExportedAuditWithoutRawSearch`.
- `ReportsControllerTests`: `GetIncomeReport_ReturnsOk`, `GetIncomeReport_ReturnsBadRequestForInvalidPeriod`, `ExportIncomeReportXlsx_ReturnsFile`, `ExportIncomeReportXlsx_ReturnsBadRequestForInvalidPeriod`, `ExportIncomeReportPdf_ReturnsFile`, `ExportIncomeReportPdf_ReturnsBadRequestForInvalidPeriod`.
- `reportsApi.test.ts`: export XLSX/PDF по поступлениям выполняется `POST` с `dateFrom`, `dateTo`, `rowMode`, `garageIds`, `ownerIds`, `incomeTypeIds`.
- `App.test.tsx`: вкладка "Поступления" показывает таблицу, быстрый период, время платежа, сумму, остаток долга после платежа и фильтрацию режима оплат без строк начислений.
- `reportFilters.test.ts` и `validation.test.ts`: фильтры поступлений создаются, сохраняются, восстанавливаются, нормализуются и валидируют период.

## Осталось

- Сводный export для дополнительных отчетов и будущие печатные формы остаются в отдельных пунктах Stage 7 (`Реализовать экспорт XLSX`, `Реализовать экспорт PDF`) и не блокируют базовый отчет по поступлениям.
- Новая запись "Что нового" не нужна: пользовательское поведение, API, бизнес-правила и файлы release notes не менялись этим шагом; закрыт только roadmap-status/evidence по уже существующей функциональности.
