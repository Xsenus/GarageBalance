# Покрытие Backend Истории Изменений

Дата инвентаризации: 03.07.2026.

Этот документ фиксирует текущие backend audit-события: какое действие пишется, к какому объекту относится, есть ли `было/стало`, причина, actor и тестовое покрытие. Документ нужен как карта для следующих срезов по истории изменений, восстановлению и подтверждениям.

## Источники

- `backend/GarageBalance.Api/Application/Auth/AuthService.cs`
- `backend/GarageBalance.Api/Application/Users/UserManagementService.cs`
- `backend/GarageBalance.Api/Application/Dictionaries/DictionaryService.cs`
- `backend/GarageBalance.Api/Application/Finance/FinanceService.cs`
- `backend/GarageBalance.Api/Application/Import/ImportService.cs`
- `backend/GarageBalance.Api/Application/Import/ImportQuarantineService.cs`
- `backend/GarageBalance.Api/Application/Import/ImportFingerprintService.cs`
- `backend/GarageBalance.Api/Application/Integrations/IntegrationSecretSettingsService.cs`
- `backend/GarageBalance.Api/Application/Reports/ReportService.cs`
- `backend/GarageBalance.Api.Tests/**`

## Обозначения

- `Да` означает, что событие записывает поле явно или через `AuditEventWriter`.
- `Нет` означает, что для этого действия поле пока не применяется.
- `Частично` означает, что поле есть только в части сценариев или выводится через metadata/нормализацию, а не как полноценный diff.
- `Н/п` означает, что поле неприменимо для события.

## Общие Правила

- `Section` и `ActionKind` выводятся из `Action`, если сервис не передал их явно.
- `AuditEventWriter` маскирует чувствительные значения в summary и metadata.
- Для `archive`, `delete`, `cancel` `AuditEventWriter` требует причину.
- `OldValues/NewValues` дают structured diff `было/стало`; если diff пустой, событие не создается.

## Сводка

| Раздел | События | Сильные стороны | Открытые хвосты |
|---|---:|---|---|
| Auth | 7 | actor для известных пользователей, metadata без паролей, rate-limit audit | у неизвестного email actor отсутствует по природе сценария; нет `было/стало` |
| Users | 3 | create/update/restore, diff и причина отключения | отдельной матрицы прав пока нет |
| Dictionaries | 32 | create/update/archive/restore для текущих справочников, diff на update, причина archive | начисление участников сборов и полная UI-связка сборов остаются следующим срезом |
| Finance | 16 | create/update/cancel/generate, diff на update, связанные месяц/гараж/контрагент/документ | restore финансовых операций зависит от бизнес-решения |
| Import | 5 | dry-run, отчет, карантин, fingerprints, безопасная metadata | фактический импорт/rollback еще не завершены |
| Integrations | 1 | secret upsert без plaintext-секретов, diff состояния секрета | запуск синхронизации и конфликты будущих интеграций впереди |
| Reports | 6 | формирование/выгрузка, период, строка, формат, audit-writing export через POST | тест прямо закрепляет income export; остальные report actions покрыты общими report-service и endpoint тестами |

## Auth

| Action | Объект | Было/стало | Причина | Actor | Тесты | Примечание |
|---|---|---|---|---|---|---|
| `auth.bootstrap_admin_created` | `user` | Нет | Нет | Да | `AuthServiceTests`, `EfAuthServiceTests` | email маскируется в публичном выводе истории |
| `auth.login_success` | `user` | Нет | Нет | Да | `AuthServiceTests` | роли пишутся в metadata |
| `auth.login_failed` | `user` или `login_email` | Нет | Частично | Частично | `AuthServiceTests` | для неизвестного email actor `null`, entity id хранит hash |
| `auth.login_rate_limited` | `user` или `login_email` | Нет | Частично | Частично | `AuthServiceTests` | фиксирует лимит входа и окно попыток |
| `auth.login_inactive` | `user` | Нет | Частично | Да | `AuthServiceTests` | reason хранится в metadata |
| `auth.password_change_failed` | `user` | Нет | Частично | Да | `AuthServiceTests` | пароль не пишется в summary/metadata |
| `auth.password_changed` | `user` | Нет | Нет | Да | `AuthServiceTests` | пароль не пишется в summary/metadata |

## Users

| Action | Объект | Было/стало | Причина | Actor | Тесты | Примечание |
|---|---|---|---|---|---|---|
| `users.user_created` | `user` | Нет | Нет | Да | `UserManagementServiceTests` | email и пароль не раскрываются |
| `users.user_updated` | `user` | Да | Частично | Да | `UserManagementServiceTests` | отключение пользователя требует и пишет reason |
| `users.user_restored` | `user` | Да | Нет | Да | `UserManagementServiceTests` | restore меняет active-state |

## Dictionaries

| Action | Объект | Было/стало | Причина | Actor | Тесты | Примечание |
|---|---|---|---|---|---|---|
| `dictionary.owner_created` | `owner` | Нет | Нет | Да | `DictionaryServiceTests` | текущая backend-модель владельцев |
| `dictionary.owner_updated` | `owner` | Да | Нет | Да | `DictionaryServiceTests` | no-op не создает событие |
| `dictionary.owner_archived` | `owner` | Нет | Да | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | archive вместо физического удаления |
| `dictionary.owner_restored` | `owner` | Нет | Нет | Да | `DictionaryServiceTests` | проверяет конфликт активной уникальности |
| `dictionary.garage_created` | `garage` | Нет | Нет | Да | `DictionaryServiceTests` | текущая backend-модель гаражей |
| `dictionary.garage_updated` | `garage` | Да | Нет | Да | `DictionaryServiceTests` | включает владельца и стартовые значения |
| `dictionary.garage_archived` | `garage` | Нет | Да | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | archive вместо физического удаления |
| `dictionary.garage_restored` | `garage` | Нет | Нет | Да | `DictionaryServiceTests` | проверяет конфликт номера гаража |
| `dictionary.supplier_group_created` | `supplier_group` | Нет | Нет | Да | `DictionaryServiceTests` | группы поставщиков |
| `dictionary.supplier_group_updated` | `supplier_group` | Да | Нет | Да | `DictionaryServiceTests` | no-op не создает событие |
| `dictionary.supplier_group_archived` | `supplier_group` | Нет | Да | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | archive вместо физического удаления |
| `dictionary.supplier_group_restored` | `supplier_group` | Нет | Нет | Да | `DictionaryServiceTests` | проверяет конфликт активного имени |
| `dictionary.supplier_created` | `supplier` | Нет | Нет | Да | `DictionaryServiceTests` | текущая backend-модель поставщиков |
| `dictionary.supplier_updated` | `supplier` | Да | Нет | Да | `DictionaryServiceTests` | включает группу и реквизиты без лишних секретов |
| `dictionary.supplier_archived` | `supplier` | Нет | Да | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | archive вместо физического удаления |
| `dictionary.supplier_restored` | `supplier` | Нет | Нет | Да | `DictionaryServiceTests` | проверяет конфликт active-only уникальности |
| `dictionary.income_type_created` | `income_type` | Нет | Нет | Да | `DictionaryServiceTests` | виды поступлений |
| `dictionary.income_type_updated` | `income_type` | Да | Нет | Да | `DictionaryServiceTests` | no-op не создает событие |
| `dictionary.income_type_archived` | `income_type` | Нет | Да | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | archive вместо физического удаления |
| `dictionary.income_type_restored` | `income_type` | Нет | Нет | Да | `DictionaryServiceTests` | проверяет конфликт активного имени |
| `dictionary.expense_type_created` | `expense_type` | Нет | Нет | Да | `DictionaryServiceTests` | виды выплат |
| `dictionary.expense_type_updated` | `expense_type` | Да | Нет | Да | `DictionaryServiceTests` | no-op не создает событие |
| `dictionary.expense_type_archived` | `expense_type` | Нет | Да | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | archive вместо физического удаления |
| `dictionary.expense_type_restored` | `expense_type` | Нет | Нет | Да | `DictionaryServiceTests` | проверяет конфликт активного имени |
| `dictionary.tariff_created` | `tariff` | Нет | Нет | Да | `DictionaryServiceTests` | включает пороги электроэнергии в summary |
| `dictionary.tariff_updated` | `tariff` | Да | Нет | Да | `DictionaryServiceTests` | diff по ставке/периоду/порогам |
| `dictionary.tariff_archived` | `tariff` | Нет | Да | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | archive вместо физического удаления |
| `dictionary.tariff_restored` | `tariff` | Нет | Нет | Да | `DictionaryServiceTests` | проверяет конфликт активного тарифа |
| `dictionary.charge_service_created` | `charge_service` | Нет | Нет | Да | `DictionaryServiceTests` | регулярные услуги, периодичность, сроки, счетчик, пороговая тарификация и единица |
| `dictionary.charge_service_updated` | `charge_service` | Да | Нет | Да | `DictionaryServiceTests` | diff по единице, периодичности, срокам, признакам счетчика/порогов и связям учета |
| `dictionary.charge_service_archived` | `charge_service` | Нет | Да | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | archive вместо физического удаления |
| `dictionary.charge_service_restored` | `charge_service` | Нет | Нет | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | проверяет конфликт активной услуги |
| `dictionary.fee_campaign_created` | `fee_campaign` | Нет | Нет | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | объявление сбора с целью, суммами, датами и переносом долга |
| `dictionary.fee_campaign_updated` | `fee_campaign` | Да | Нет | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | diff по цели, суммам, периоду, участникам и дням просрочки |
| `dictionary.fee_campaign_archived` | `fee_campaign` | Нет | Да | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | archive вместо физического удаления |
| `dictionary.fee_campaign_restored` | `fee_campaign` | Нет | Нет | Да | `DictionaryServiceTests`, `DictionariesControllerTests` | проверяет конфликт активного сбора с тем же названием |

## Finance

| Action | Объект | Было/стало | Причина | Actor | Тесты | Примечание |
|---|---|---|---|---|---|---|
| `finance.income_created` | `financial_operation` | Нет | Нет | Да | `FinanceServiceTests` | гараж, месяц, документ и сумма в structured metadata |
| `finance.income_updated` | `financial_operation` | Да | Нет | Да | `FinanceServiceTests` | diff суммы, даты, месяца, документа, комментария |
| `finance.expense_created` | `financial_operation` | Нет | Нет | Да | `FinanceServiceTests` | поставщик, месяц, документ и сумма в structured metadata |
| `finance.expense_updated` | `financial_operation` | Да | Нет | Да | `FinanceServiceTests` | diff суммы, даты, месяца, документа, комментария |
| `finance.operation_canceled` | `financial_operation` | Нет | Да | Да | `FinanceServiceTests`, `FinanceControllerTests` | request требует reason; audit reason пока нормализован как отмена записи, а текстовая причина остается в summary |
| `finance.accrual_created` | `accrual` | Нет | Нет | Да | `FinanceServiceTests` | гараж, месяц, вид начисления |
| `finance.accrual_updated` | `accrual` | Да | Нет | Да | `FinanceServiceTests` | diff суммы, месяца, источника, комментария |
| `finance.accrual_canceled` | `accrual` | Нет | Да | Да | `FinanceServiceTests`, `FinanceControllerTests` | request требует reason |
| `finance.supplier_accrual_created` | `supplier_accrual` | Нет | Нет | Да | `FinanceServiceTests` | поставщик, месяц, документ |
| `finance.supplier_accrual_updated` | `supplier_accrual` | Да | Нет | Да | `FinanceServiceTests` | diff суммы, месяца, документа, комментария |
| `finance.supplier_accrual_canceled` | `supplier_accrual` | Нет | Да | Да | `FinanceServiceTests`, `FinanceControllerTests` | request требует reason |
| `finance.regular_accruals_generated` | `accrual_batch` | Нет | Нет | Да | `FinanceServiceTests` | массовая генерация начислений |
| `finance.fee_campaign_accruals_generated` | `accrual_batch` | Нет | Нет | Да | `FinanceServiceTests` | массовая генерация начислений по объявленному сбору |
| `finance.supplier_group_salary_accruals_generated` | `supplier_accrual_batch` | Нет | Нет | Да | `FinanceServiceTests` | массовая генерация зарплаты/поставщиков |
| `finance.meter_reading_created` | `meter_reading` | Нет | Нет | Да | `FinanceServiceTests` | гараж, месяц, тип счетчика, расход |
| `finance.meter_reading_updated` | `meter_reading` | Да | Нет | Да | `FinanceServiceTests` | diff показаний и расхода |
| `finance.meter_reading_canceled` | `meter_reading` | Нет | Да | Да | `FinanceServiceTests`, `FinanceControllerTests` | request требует reason |
| `finance.meter_reading_restored` | `meter_reading` | Нет | Нет | Да | `FinanceServiceTests`, `FinanceControllerTests` | восстановление отмененного показания с проверкой активного дубля |

## Import

| Action | Объект | Было/стало | Причина | Actor | Тесты | Примечание |
|---|---|---|---|---|---|---|
| `import.access_dry_run` | `access_import_run` | Нет | Нет | Да | `ImportServiceTests` | file name, run id и безопасная metadata |
| `import.access_dry_run_report_exported` | `access_import_run` | Нет | Нет | Да | `ImportServiceTests`, `ImportControllerTests` | export endpoint использует POST, потому пишет audit |
| `import.quarantine_registered` | `access_import_quarantine_item` | Нет | Да | Да | `ImportQuarantineServiceTests` | reason code хранится как причина/metadata |
| `import.quarantine_resolved` | `access_import_quarantine_item` | Нет | Да | Да | `ImportQuarantineServiceTests`, `ImportControllerTests` | resolution comment пишется как reason |
| `import.row_fingerprint_registered` | `access_import_row_fingerprint` | Нет | Нет | Да | `ImportFingerprintServiceTests` | защищает idempotency импортных строк |

## Integrations

| Action | Объект | Было/стало | Причина | Actor | Тесты | Примечание |
|---|---|---|---|---|---|---|
| `integration.secret_upserted` | `integration_secret_setting` | Да | Нет | Да | `IntegrationSecretSettingsServiceTests` | plaintext-секреты не раскрываются, diff показывает состояние секрета |

## Reports

| Action | Объект | Было/стало | Причина | Actor | Тесты | Примечание |
|---|---|---|---|---|---|---|
| `reports.consolidated_generated` | `report` | Нет | Нет | Да | `ReportServiceTests` | период, количество строк и фильтры в metadata |
| `reports.income_generated` | `report` | Нет | Нет | Да | `ReportServiceTests` | период, количество строк и фильтры в metadata |
| `reports.expense_generated` | `report` | Нет | Нет | Да | `ReportServiceTests` | период, количество строк и фильтры в metadata |
| `reports.consolidated_exported` | `report` | Нет | Нет | Да | `ReportsControllerTests`, `ReportServiceTests` | export endpoint использует POST, потому пишет audit |
| `reports.income_exported` | `report` | Нет | Нет | Да | `ReportServiceTests` | прямой service-test проверяет action, формат и file name |
| `reports.expense_exported` | `report` | Нет | Нет | Да | `ReportsControllerTests`, `ReportServiceTests` | export endpoint использует POST, потому пишет audit |

## Что Остается

- Составить аналогичную инвентаризацию soft delete / archive / cancel действий backend и frontend.
- Для будущего начисления участников сборов добавить отдельные финансовые события генерации/изменения долгов по гаражам.
- После бизнес-решения по финансовому restore обновить строки `finance.*_canceled` и добавить обратные операции или restore-события.
- Для отчетов можно усилить прямые service-tests на `reports.consolidated_exported` и `reports.expense_exported`, если потребуется отдельное подтверждение каждого action-кода.
