# Контракт сортировки отчетов

Дата утверждения: 19.07.2026.

Статус: утвержден для реализации Этапа 6 `docs/forms-compliance-fixes-roadmap.md`.

## Область действия

Контракт применяется к восьми рабочим вкладкам раздела `Отчеты` и к их XLSX/PDF-экспорту:

- консолидированный отчет;
- отчет по гаражам;
- отчет по выплатам;
- отчет по поступлениям;
- отчет по оплатам из кассы;
- отчет по сдаче кассы в банк;
- отчет по сборам;
- отчет по изменениям фондов.

## Единый API-контракт

- `sortBy` — одно поле из allowlist конкретного отчета.
- `sortDirection` — только `asc` или `desc` в нижнем регистре.
- Если оба параметра отсутствуют или состоят только из пробелов, применяется установленный для отчета порядок по умолчанию.
- Если передан `sortBy`, а `sortDirection` отсутствует, используется `asc`.
- Если передан только `sortDirection`, он применяется к полю отчета по умолчанию.
- Неизвестный `sortBy` отклоняется с HTTP `400` и кодом `report_sort_field_invalid`; сервер не подменяет его полем по умолчанию.
- Неизвестный `sortDirection` отклоняется с HTTP `400` и кодом `report_sort_direction_invalid`.
- Явный сброс в UI удаляет оба query-параметра и возвращает порядок по умолчанию.
- Экран, XLSX и PDF обязаны получать одинаковые `sortBy/sortDirection`; экспорт не вводит отдельный порядок.
- Сортировка применяется ко всей отфильтрованной выборке до `offset/limit`, а не только к текущей странице.
- После выбранного поля всегда применяется стабильный tie-breaker. Технический идентификатор используется только как последний tie-breaker и не входит в публичный allowlist.

## Allowlist и порядок по умолчанию

### Консолидированный отчет `/api/reports/consolidated`

Сортируется основная помесячная таблица. Сводка гаражей остается отдельной агрегированной частью ответа.

Allowlist: `accountingMonth`, `incomeTotal`, `expenseTotal`, `accrualTotal`, `balance`, `debt`, `operationCount`, `accrualCount`, `meterReadingCount`.

По умолчанию: `accountingMonth desc`. Tie-breaker: `accountingMonth desc`.

### Отчет по гаражам `/api/reports/garages`

Allowlist: `accountingMonth`, `garageNumber`, `ownerName`, `incomeTypeName`, `accrualAmount`, `incomeAmount`, `difference`.

По умолчанию: `accountingMonth desc`. Tie-breakers: `garageNumber asc`, `incomeTypeName asc`, идентификатор строки.

### Отчет по выплатам `/api/reports/expense`

Allowlist: `date`, `accountingMonth`, `supplierName`, `expenseTypeName`, `accrualAmount`, `expenseAmount`, `difference`, `documentNumber`.

По умолчанию: `date desc`. Tie-breakers: `supplierName asc`, `expenseTypeName asc`, идентификатор строки.

### Отчет по поступлениям `/api/reports/income`

Allowlist: `date`, `accountingMonth`, `garageNumber`, `ownerName`, `incomeTypeName`, `accrualAmount`, `incomeAmount`, `debt`, `documentNumber`.

По умолчанию: `date desc`. Tie-breakers: фактическое время создания `desc`, `garageNumber asc`, идентификатор строки.

### Отчет по оплатам из кассы `/api/reports/cash-payments`

Allowlist: `date`, `amount`, `hasReceipt`, `purpose`, `supplierName`, `expenseTypeName`, `documentNumber`.

По умолчанию: `date desc`. Tie-breaker: `operationId desc`.

### Отчет по сдаче кассы в банк `/api/reports/bank-deposits`

Allowlist: `date`, `amount`, `fundName`, `comment`.

По умолчанию: `date desc`. Tie-breaker: `operationId desc`.

### Отчет по сборам `/api/reports/fees`

Сортируется детальная таблица гаражей выбранного сбора. Сводная таблица сборов сохраняет порядок каталога, а режим должников использует тот же порядок детальных строк после фильтра `debt > 0`.

Allowlist: `garageNumber`, `ownerName`, `feeName`, `accrued`, `paid`, `lastPaymentDate`, `debt`.

По умолчанию: `garageNumber asc`. Tie-breakers: `feeName asc`, `garageId asc`.

### Отчет по изменениям фондов `/api/reports/fund-changes`

Allowlist: `date`, `fundName`, `changeName`, `amount`, `balanceBefore`, `balanceAfter`, `actorDisplayName`, `reason`.

По умолчанию: `date desc`. Tie-breaker: `operationId desc`.

## Границы следующей реализации

- Добавить параметры во все экранные и экспортные controller actions без изменения permission `reports.read`.
- Валидировать контракт в Application до запуска запроса и до записи audit-события успешного формирования.
- Передать нормализованное поле и направление в Infrastructure query каждого отчета.
- Выполнять сортировку до `Count`, `Skip` и `Take`; итоги рассчитывать по полной отфильтрованной выборке и не менять от направления.
- Неизвестное поле или направление не должно выполнять SQL-запрос отчета и не должно создавать audit успешного формирования/экспорта.
- Покрыть каждое разрешенное поле, оба направления, порядок по умолчанию, стабильные tie-breakers и ошибки контракта автоматическими тестами.

## Критерий готовности

Пункт выполнения сортировки считается завершенным только после одинаковой проверки экранного JSON, XLSX и PDF на синтетическом наборе, PostgreSQL-теста сортировки до пагинации и подтверждения отсутствия новых ошибок в логах.
