# Backend Report Tests Verification

## Scope

Пункт Stage 7 "Добавить backend-тесты отчетных запросов, фильтров, итогов и прав" закрывается по текущему набору отчетов: сводный отчет, поступления, выплаты, оплаты из кассы, сдача кассы в банк, сборы и изменение фондов. Production-код этим шагом не менялся; добавлены только прямые permission-handler tests для `reports.read`, evidence-документ и roadmap guard.

## Покрытие запросов и итогов

- `ReportServiceTests` покрывает сводные агрегаты периода, нормализацию периода, строки гаражей, totals без искажения при row limit и поиск по гаражу/владельцу.
- `ReportServiceTests` покрывает поступления: начисления, платежи, opening debt, долг после платежа, фильтры по владельцу/виду поступления/rowMode, invalid period и XLSX/PDF export с фильтрами.
- `ReportServiceTests` покрывает выплаты: платежи, начисления поставщикам, opening obligation, фильтры по поставщику/виду выплаты/search, invalid period и XLSX/PDF export с фильтрами.
- `ReportServiceTests` покрывает оплату из кассы, сдачу кассы в банк, сборы и изменение фондов: строки, totals, search/variation/date filters, audit generation и XLSX/PDF exports.

## Покрытие controller surface

- `ReportsControllerTests` покрывает успешные GET/POST actions, bad request для невалидных периодов, file responses, `FileDownloadName`, content type и POST routes для exports.
- `ControllerAuthorizationCoverageTests.ReportActionsRequireReportsReadPermission` закрепляет `reports.read` на `ReportsController` и отсутствие anonymous report actions.
- `PermissionAuthorizationHandlerTests` проверяет положительный и отрицательный сценарий `reports.read`, чтобы право отчетов не было только декларацией на контроллере.

## Осталось

- Runtime 401/403 через полный HTTP pipeline можно расширить вместе с будущими e2e/API tests и test host infrastructure.
- Для новых отчетов нужно добавлять service/controller tests на successful path, validation, filters, totals, exports, audit и `reports.read`.
- Новая запись "Что нового" не нужна: пользовательское поведение, API, бизнес-правила и release notes не менялись этим шагом; закрыт backend test/evidence status.
