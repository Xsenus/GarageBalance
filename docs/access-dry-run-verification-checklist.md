# Access Dry-Run Verification Checklist

Этот checklist фиксирует границу готовности dry-run импорта Access.
Текущий dry-run уже проверяет файл на уровне безопасной загрузки и отчета, но не может считаться полностью закрытым без live reader старой Access-БД и подсчета реальных таблиц/строк.

## Что Уже Реализовано

- [x] Принимается файл `.accdb`/`.mdb` через backend endpoint dry-run.
- [x] Проверяется расширение файла и максимальный размер.
- [x] Рассчитывается SHA-256 содержимого для повторяемости и duplicate warning.
- [x] Проверяется OLE/Access-like signature.
- [x] Сохраняется dry-run run в `access_import_runs`.
- [x] Сохраняется JSON report с checks, warnings и summary.
- [x] Пишется пошаговый run log в `access_import_run_log_entries`.
- [x] Пишется audit event `import.access_dry_run`.
- [x] Можно скачать JSON-отчет dry-run через audited POST export.
- [x] UI показывает reader status, checks, warnings/errors, run log, history, created records placeholder и quarantine tab.

## Что Остается Заблокированным

- [ ] Live Access reader или согласованная конвертация.
- [ ] Приватная рабочая копия `.accdb`/`.mdb`; оригинал не изменяется.
- [ ] Schema inventory с реальными tables/columns/relationships/indexes.
- [ ] Row counts по пользовательским таблицам.
- [ ] Safe checksums/aggregates по ключевым таблицам.
- [ ] Сверка dry-run report с `docs/access-postgresql-mapping-checklist.md`.
- [ ] Сверка dry-run report с `docs/access-forms-queries-decision-checklist.md`.
- [ ] Privacy-check, что raw rows, Access-файлы, screenshots и private exports не попадут в Git.

## Финальный Dry-Run Report Должен Содержать

- [ ] file name, extension, size and SHA-256.
- [ ] reader/provider status.
- [ ] tables discovered count.
- [ ] per-table row counts.
- [ ] warnings for missing required tables, unexpected empty tables and unreadable tables.
- [ ] mapping readiness summary: mapped, skipped, quarantine-required and decision-required sources.
- [ ] `rawRowsExported=false`.
- [ ] no names, addresses, phones, document numbers or owner-specific payment rows.

## Когда Roadmap-Пункт Можно Закрыть

- [ ] Dry-run выполнен на приватной working copy Access-БД.
- [ ] Reader/conversion returned real schema and row counts.
- [ ] Report persisted in PostgreSQL and can be downloaded.
- [ ] Run log contains reader, schema, counts, warnings and final summary steps.
- [ ] Backend tests cover success, validation errors, duplicate file, reader unavailable, report export and run log.
- [ ] Frontend tests cover file selection, dry-run start, checks, warnings/errors, history, log and report download.
- [ ] Idempotent EF migration script remains clean.
- [ ] Guard `AccessDryRunImportRoadmapItemRemainsBlockedUntilLiveReaderCountsAndReportExist` can be removed only with live reader/counts evidence.
