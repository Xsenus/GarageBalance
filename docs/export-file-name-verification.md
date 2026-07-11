# Export File Name Verification

## Scope

Пункт Stage 7 "Реализовать единый формат имени экспортируемых файлов с датой периода и типом отчета" закрывается как уже реализованный для текущих report exports. Production-код этим шагом не менялся: задача состояла в повторной сверке filename helpers, service/controller tests и roadmap-статуса.

## Реализовано

- Period exports используют формат `garagebalance-{type}-{yyyyMMdd}-{yyyyMMdd}.{xlsx|pdf}` через `BuildExportFileName`.
- Snapshot exports используют формат `garagebalance-{type}.{xlsx|pdf}` через `BuildSnapshotExportFileName`.
- Текущие period report types: `consolidated`, `income`, `expense`, `cash-payments`, `bank-deposits`, `fund-changes`.
- Текущий snapshot report type: `fees`.

## Тестовое покрытие

- `ReportServiceTests` проверяет имена файлов для XLSX/PDF: `garagebalance-consolidated-20260601-20260601`, `garagebalance-income-20260601-20260630`, `garagebalance-expense-20260601-20260630`, `garagebalance-cash-payments-20260601-20260630`, `garagebalance-bank-deposits-20260601-20260630`, `garagebalance-fund-changes-20260601-20260630`, `garagebalance-fees`.
- `ReportsControllerTests` проверяет, что `FileDownloadName` возвращает имя файла из `ReportExportFileDto` для report export endpoints.
- XLSX/PDF verification docs подтверждают, что текущие report exports используют единый naming convention вместе с форматами файлов.

## Осталось

- Future report exports должны использовать те же helpers или иметь отдельный явно описанный snapshot/period contract.
- Новая запись "Что нового" не нужна: пользовательское поведение, API, бизнес-правила и release notes не менялись этим шагом; закрыт только roadmap-status/evidence по уже существующей функциональности.
