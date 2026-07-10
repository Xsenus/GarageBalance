# Access Transfer Implementation Checklist

Этот checklist фиксирует условия фактического переноса данных из старой Access-БД в рабочие таблицы GarageBalance.
Пункт нельзя закрывать как `[x]`, пока перенос не выполнен на приватной копии Access-БД и не сверяется с baseline.

## Что Уже Есть В Системе

- [x] Dry-run run сохраняется в `access_import_runs`.
- [x] Пошаговый run log сохраняется в `access_import_run_log_entries`.
- [x] JSON-отчет dry-run можно скачать через audited export.
- [x] Заявка на фактический импорт требует причину и подтверждение backup.
- [x] Пока reader Access не подключен, заявка фиксирует `pending_access_reader`, а данные не переносятся.
- [x] `access_import_row_fingerprints` готов для идемпотентности строк по external id или row hash.
- [x] `access_import_quarantine_items` готов для строк, которые нельзя перенести автоматически.
- [x] `access_import_created_records` готов для учета созданных target records и будущего rollback/status tracking.

## Предусловия Перед Фактическим Переносом

- [ ] Есть приватная working copy `.accdb`/`.mdb`; оригинал не изменяется.
- [ ] Есть live Access reader или утвержденная конвертация.
- [ ] Есть schema inventory с таблицами, колонками, keys/relationships/indexes и row counts.
- [ ] Есть pre-import baseline с checksums/aggregates по ключевым таблицам.
- [ ] Есть финальный field-level Access -> PostgreSQL mapping.
- [ ] Есть решение по old forms/queries: UI-only, report-only, business-rule, import-helper, skip/quarantine.
- [ ] Есть backup PostgreSQL перед переносом и restore-check последнего backup.
- [ ] Есть dry-run report без error blockers.
- [ ] Privacy-check подтверждает, что Access-файлы, raw rows, exports, screenshots и private import folders не попадут в Git.

## Обязательные Transfer Flows

- [ ] Владельцы: `Vladelci` -> `owners`, включая split ФИО, телефон, адрес и archive/duplicate policy.
- [ ] Гаражи: `garage` -> `garages`, включая номер, владельца, people/floors, стартовые счетчики, стартовый баланс и comment.
- [ ] Виды оплат/начислений: `vidoplati`/`KOPLATE`/`CENAPLATEZ` -> `income_types`, `tariffs`, `charge_service_settings`, `irregular_payments`.
- [ ] Виды выплат/контрагенты: `VidViplat` и связанные источники -> `expense_types`, `supplier_groups`, `suppliers`, `staff_members` where applicable.
- [ ] Исторические поступления: Access payments -> `financial_operations` and/or `accruals` with accounting month, amount sign, document/comment policy.
- [ ] Исторические выплаты: Access payouts -> `financial_operations` and/or `supplier_accruals` with counterparty resolution.
- [ ] Счетчики: `PREDZNACHENIE`, `NOVZNACHENIE`, `RAZNICA` -> `meter_readings` with consumption validation.
- [ ] Начальные остатки: Access debts/overpayments -> `garages.StartingBalance`, `suppliers.StartingBalance` or explicit opening operations after business decision.

## Required Safety Behavior

- [ ] Каждая переносимая строка получает deterministic idempotency key.
- [ ] Повторный запуск не создает дубли и не меняет уже созданные target records без отдельного update policy.
- [ ] Ошибочные/неоднозначные строки уходят в quarantine с reason code, severity, safe row hash and safe snapshot.
- [ ] Все created target records фиксируются в `access_import_created_records`.
- [ ] Все финансовые значения переносятся как decimal with explicit rounding.
- [ ] Все даты приводятся к `DateOnly`, `DateTimeOffset` or accounting month по mapping rules.
- [ ] Все изменения пишут audit history без raw sensitive rows.
- [ ] Transfer выполняется транзакционно: ошибка critical batch откатывает batch или весь run according to documented strategy.

## Acceptance And Reconciliation

- [ ] Count imported owners, garages, income types, expense types, payments, payouts and meter readings.
- [ ] Compare imported counts with pre-import baseline.
- [ ] Compare checksums/aggregates where possible.
- [ ] Compare opening balances and report totals with customer expectations.
- [ ] List skipped, quarantined and decision-required rows.
- [ ] Export final reconciliation report without raw personal/payment rows.

## Когда Roadmap-Пункт Можно Закрыть

- [ ] Фактический перенос выполнен на приватной Access working copy.
- [ ] Backend tests cover mapping, duplicates, malformed rows, quarantine, idempotency, transactions and rollback/status tracking.
- [ ] Frontend tests cover transfer request, progress/result states, created records, quarantine and reconciliation summary.
- [ ] PostgreSQL migration/apply check passed on local or acceptance DB.
- [ ] Reconciliation report is reviewed with the customer.
- [ ] Guard `AccessTransferRoadmapItemRemainsBlockedUntilLiveReaderMappingAndReconciliationExist` can be removed only with live transfer and reconciliation evidence.
