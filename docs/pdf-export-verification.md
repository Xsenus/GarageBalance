# PDF Export Verification

## Scope

Пункт Stage 7 "Реализовать экспорт PDF" закрывается как уже реализованный для текущего набора отчетов. Production-код этим шагом не менялся: задача состояла в повторной сверке готовых PDF endpoints, содержимого документов, frontend-клиента, UI-кнопок и тестового покрытия.

## Реализовано

- Сводный отчет: `POST /api/reports/consolidated/export/pdf`, файл `garagebalance-consolidated-{yyyyMMdd}-{yyyyMMdd}.pdf`.
- Отчет по поступлениям: `POST /api/reports/income/export/pdf`, файл `garagebalance-income-{yyyyMMdd}-{yyyyMMdd}.pdf`.
- Отчет по выплатам: `POST /api/reports/expense/export/pdf`, файл `garagebalance-expense-{yyyyMMdd}-{yyyyMMdd}.pdf`.
- Отчет по оплатам из кассы: `POST /api/reports/cash-payments/export/pdf`, файл `garagebalance-cash-payments-{yyyyMMdd}-{yyyyMMdd}.pdf`.
- Отчет по сдаче кассы в банк: `POST /api/reports/bank-deposits/export/pdf`, файл `garagebalance-bank-deposits-{yyyyMMdd}-{yyyyMMdd}.pdf`.
- Отчет по сборам: `POST /api/reports/fees/export/pdf`, файл `garagebalance-fees.pdf`.
- Отчет по изменению фондов: `POST /api/reports/fund-changes/export/pdf`, файл `garagebalance-fund-changes-{yyyyMMdd}-{yyyyMMdd}.pdf`.
- Frontend `reportsApi` использует `POST` для всех PDF-выгрузок отчетов, а экран отчетов показывает кнопки `Скачать PDF` для текущих export-ready вкладок.

## Тестовое покрытие

- `ReportServiceTests` проверяет content type `application/pdf`, имена файлов, ожидаемые строки и фильтрацию лишних строк для consolidated, income, expense, cash payments, bank deposits, fees and fund changes PDF.
- `ReportsControllerTests` закрепляет `POST` route templates для всех PDF endpoints, file response contract и invalid period mapping там, где endpoint принимает период.
- `reportsApi.test.ts` проверяет, что frontend-клиент отправляет PDF-экспорты всех текущих отчетов через `POST` и передает те же query filters, что экранные отчеты.
- `App.test.tsx` проверяет доступные кнопки `Скачать PDF`, icon/tooltip attributes и вызовы export-клиента для сдачи кассы в банк; shared report workflow держит PDF-клиенты для сводного отчета, поступлений, выплат, кассы, сборов and fund changes.

## Осталось

- PDF для будущих дополнительных отчетов и более специализированные печатные формы подключаются отдельными шагами после реализации самих отчетов и утверждения печатных шаблонов.
- Новая запись "Что нового" не нужна: пользовательское поведение, API, бизнес-правила и release notes не менялись этим шагом; закрыт только roadmap-status/evidence по уже существующей функциональности.
