# XLSX Export Verification

## Scope

Документ фиксирует актуальный экспорт восьми рабочих отчетов. Экран и выгрузка используют одинаковые периоды, множественные фильтры, группировку и сортировку; XLSX формируется по полной отфильтрованной выборке, а не только по видимой странице.

## Реализовано

- Сводный отчет: `POST /api/reports/consolidated/export/xlsx`, файл `garagebalance-consolidated-{yyyyMMdd}-{yyyyMMdd}.xlsx`, листы/данные месяцев и гаражей.
- Отчет по гаражам: `POST /api/reports/garages/export/xlsx`, файл `garagebalance-garages-{yyyyMMdd}-{yyyyMMdd}.xlsx`, множественные фильтры гаражей, группировка и сортировка экрана без ограничения текущей страницей.
- Отчет по поступлениям: `POST /api/reports/income/export/xlsx`, файл `garagebalance-income-{yyyyMMdd}-{yyyyMMdd}.xlsx`, фильтры периода, поиска, гаражей, владельцев, видов поступлений и режима строк.
- Отчет по выплатам: `POST /api/reports/expense/export/xlsx`, файл `garagebalance-expense-{yyyyMMdd}-{yyyyMMdd}.xlsx`, фильтры периода, поиска, поставщиков, видов выплат и режима строк.
- Отчет по оплатам из кассы: `POST /api/reports/cash-payments/export/xlsx`, файл `garagebalance-cash-payments-{yyyyMMdd}-{yyyyMMdd}.xlsx`, фильтры периода и поиска.
- Отчет по сдаче кассы в банк: `POST /api/reports/bank-deposits/export/xlsx`, файл `garagebalance-bank-deposits-{yyyyMMdd}-{yyyyMMdd}.xlsx`, фильтры периода и поиска.
- Отчет по сборам: `POST /api/reports/fees/export/xlsx`, файл `garagebalance-fees.xlsx`, листы summary, garages and debtors.
- Отчет по изменению фондов: `POST /api/reports/fund-changes/export/xlsx`, файл `garagebalance-fund-changes-{yyyyMMdd}-{yyyyMMdd}.xlsx`, фильтры периода и поиска.
- Frontend `reportsApi` использует `POST` для всех XLSX-выгрузок отчетов, а каждая из восьми вкладок показывает кнопку `Скачать XLSX` и передает активные фильтры и сортировку.

## Тестовое покрытие

- `ReportServiceTests` проверяет workbook content type, имена файлов, наличие ожидаемых строк/листов и фильтрацию лишних строк для всех восьми XLSX-отчетов, включая полный экспорт гаражей независимо от текущей экранной страницы.
- `ReportsControllerTests` закрепляет `POST` route templates для всех XLSX endpoints, file response contract и invalid period mapping там, где endpoint принимает период.
- `reportsApi.test.ts` проверяет, что frontend-клиент отправляет XLSX-экспорты всех текущих отчетов через `POST` и передает те же query filters, что экранные отчеты.
- `App.test.tsx` проверяет доступные кнопки `Скачать XLSX`, icon/tooltip attributes и одинаковые активные фильтры, группировку и сортировку для экспорта всех восьми вкладок.

## Осталось

- XLSX для будущих дополнительных отчетов подключается отдельными шагами после реализации самих отчетов из `docs/additional-report-slots.md`.
