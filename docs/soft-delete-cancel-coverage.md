# Покрытие Soft Delete, Archive, Cancel И Restore

Дата инвентаризации: 03.07.2026.

Этот документ фиксирует текущие backend и frontend действия, где объект не удаляется физически, а архивируется, отменяется, помечается решенным или восстанавливается. Он дополняет `docs/audit-event-coverage.md` и нужен для дальнейшей реализации подтверждений, истории изменений и кнопок `Вернуть`.

## Источники

- `backend/GarageBalance.Api/Application/Dictionaries/DictionaryService.cs`
- `backend/GarageBalance.Api/Application/Finance/FinanceService.cs`
- `backend/GarageBalance.Api/Application/Users/UserManagementService.cs`
- `backend/GarageBalance.Api/Application/Import/ImportQuarantineService.cs`
- `backend/GarageBalance.Api/Controllers/DictionariesController.cs`
- `backend/GarageBalance.Api/Controllers/FinanceController.cs`
- `backend/GarageBalance.Api/Controllers/UsersController.cs`
- `backend/GarageBalance.Api/Controllers/ImportController.cs`
- `frontend/src/App.tsx`
- `backend/GarageBalance.Api.Tests/**`
- `frontend/src/App.test.tsx`

## Backend Справочники

| Объект | Маркер | Archive endpoint/service | Restore endpoint/service | Причина обязательна | Audit | Конфликты восстановления | Тесты |
|---|---|---|---|---|---|---|---|
| Владелец | `IsArchived` | `ArchiveOwnerAsync` / `ArchiveOwner` | `RestoreOwnerAsync` / `RestoreOwner` | Да | `dictionary.owner_archived`, `dictionary.owner_restored` | активная уникальность имени/связанных ограничений не требуется отдельно | `DictionaryServiceTests`, `DictionariesControllerTests` |
| Гараж | `IsArchived` | `ArchiveGarageAsync` / `ArchiveGarage` | `RestoreGarageAsync` / `RestoreGarage` | Да | `dictionary.garage_archived`, `dictionary.garage_restored` | номер активного гаража | `DictionaryServiceTests`, `DictionariesControllerTests` |
| Группа поставщиков | `IsArchived` | `ArchiveSupplierGroupAsync` / `ArchiveSupplierGroup` | `RestoreSupplierGroupAsync` / `RestoreSupplierGroup` | Да | `dictionary.supplier_group_archived`, `dictionary.supplier_group_restored` | имя активной группы | `DictionaryServiceTests`, `DictionariesControllerTests` |
| Поставщик | `IsArchived` | `ArchiveSupplierAsync` / `ArchiveSupplier` | `RestoreSupplierAsync` / `RestoreSupplier` | Да | `dictionary.supplier_archived`, `dictionary.supplier_restored` | активная группа и active-only уникальность поставщика | `DictionaryServiceTests`, `DictionariesControllerTests` |
| Вид поступления | `IsArchived` | `ArchiveIncomeTypeAsync` / `ArchiveIncomeType` | `RestoreIncomeTypeAsync` / `RestoreIncomeType` | Да | `dictionary.income_type_archived`, `dictionary.income_type_restored` | имя активного вида поступления | `DictionaryServiceTests`, `DictionariesControllerTests` |
| Вид выплаты | `IsArchived` | `ArchiveExpenseTypeAsync` / `ArchiveExpenseType` | `RestoreExpenseTypeAsync` / `RestoreExpenseType` | Да | `dictionary.expense_type_archived`, `dictionary.expense_type_restored` | имя активного вида выплаты | `DictionaryServiceTests`, `DictionariesControllerTests` |
| Тариф | `IsArchived` | `ArchiveTariffAsync` / `ArchiveTariff` | `RestoreTariffAsync` / `RestoreTariff` | Да | `dictionary.tariff_archived`, `dictionary.tariff_restored` | активный тариф с тем же именем и датой действия | `DictionaryServiceTests`, `DictionariesControllerTests` |
| Регулярная услуга | `IsArchived` | `ArchiveChargeServiceSettingAsync` / `ArchiveChargeServiceSetting` | `RestoreChargeServiceSettingAsync` / `RestoreChargeServiceSetting` | Да | `dictionary.charge_service_archived`, `dictionary.charge_service_restored` | активная услуга с тем же названием | `DictionaryServiceTests`, `DictionariesControllerTests` |
| Сбор | `IsArchived` | `ArchiveFeeCampaignAsync` / `ArchiveFeeCampaign` | `RestoreFeeCampaignAsync` / `RestoreFeeCampaign` | Да | `dictionary.fee_campaign_archived`, `dictionary.fee_campaign_restored` | активный сбор с тем же названием | `DictionaryServiceTests`, `DictionariesControllerTests` |

## Backend Финансы

| Объект | Маркер | Cancel endpoint/service | Restore | Причина обязательна | Audit | Бизнес-правило |
|---|---|---|---|---|---|---|
| Поступление/выплата | `IsCanceled` | `CancelOperationAsync` / `CancelOperation` | `RestoreOperationAsync` / `RestoreOperation` | Да | `finance.operation_canceled`, `finance.operation_restored` | отмененная операция скрывается из рабочих списков и расчетов; восстановление проверяет активный документ, кассу/банк и лимит сотрудника |
| Начисление владельцу | `IsCanceled` | `CancelAccrualAsync` / `CancelAccrual` | `RestoreAccrualAsync` / `RestoreAccrual` | Да | `finance.accrual_canceled`, `finance.accrual_restored` | отмененное начисление скрывается из рабочих списков и расчетов; восстановление проверяет активный дубль по гаражу, виду начисления, месяцу и источнику |
| Начисление поставщику | `IsCanceled` | `CancelSupplierAccrualAsync` / `CancelSupplierAccrual` | `RestoreSupplierAccrualAsync` / `RestoreSupplierAccrual` | Да | `finance.supplier_accrual_canceled`, `finance.supplier_accrual_restored` | отмененное начисление скрывается из рабочих списков и расчетов; восстановление проверяет активный дубль по поставщику, виду выплаты, месяцу, источнику и документу |
| Показание счетчика | `IsCanceled` | `CancelMeterReadingAsync` / `CancelMeterReading` | `RestoreMeterReadingAsync` / `RestoreMeterReading` | Да | `finance.meter_reading_canceled`, `finance.meter_reading_restored` | отмененное показание исключается из активной цепочки показаний; восстановление проверяет активный дубль |
| Операция фонда | `IsCanceled` | `CancelOperationAsync` / `CancelOperation` в `FundsController` | `RestoreOperationAsync` / `RestoreOperation` в `FundsController` | Да | `fund.operation_canceled`, `fund.operation_restored` | отмененная операция исключается из остатка фонда и доступной суммы распределения; восстановление проверяет, что активная последовательность не уйдет в минус и не превысит доступный лимит |

Финансовые cancel endpoints принимают `CancelFinanceEntryRequest` через body. `ControllerThinnessTests` закрепляет правило: dangerous actions `Archive*`, `Cancel*`, `Delete*` не должны быть safe HTTP methods и должны иметь request с обязательной причиной до 1000 символов.

## Backend Users И Import

| Раздел | Объект | Маркер | Действие | Restore/Resolve | Причина | Audit | Тесты |
|---|---|---|---|---|---|---|---|
| Пользователи | `AppUser` | `IsActive = false` | отключение через update пользователя | `RestoreUserAsync` / `RestoreUser` | причина обязательна при отключении | `users.user_updated`, `users.user_restored` | `UserManagementServiceTests`, frontend workflow |
| Импорт Access | `AccessImportQuarantineItem` | `ResolvedAtUtc` | регистрация карантина | `ResolveAsync` / `ResolveQuarantineItem` | reason code/comment | `import.quarantine_registered`, `import.quarantine_resolved` | `ImportQuarantineServiceTests`, `ImportControllerTests` |

## Frontend Рабочие Экраны

| Экран | Действие | Backend или локальный прототип | Confirmation | Restore | Тесты |
|---|---|---|---|---|---|
| Справочники | архивировать/вернуть владельцев, гаражи, группы, поставщиков, виды, тарифы | Backend API | Да, причина для архива | Да | `App.test.tsx`, `DictionaryPanelV2` workflows |
| Пользователи | отключить/вернуть пользователя | Backend API | Да, причина для отключения | Да | `App.test.tsx`, user management workflows |
| Финансы | отменить поступление, выплату, начисление, показание | Backend API | Да, причина отмены | Да для отмененных платежей, начислений и показаний | `App.test.tsx`, finance workflows |
| Контрагенты-прототип | удалить/вернуть гараж, поставщика, сотрудника | React-state прототип | Да в модалках раздела | Да | `App.test.tsx`, contractors prototype |
| Тарифы-прототип | удалить/вернуть нерегулярный платеж | React-state прототип | Да, причина удаления | Да | `App.test.tsx`, tariffs prototype |

## Физическое Удаление

Production backend-код не должен выполнять физическое удаление рабочих объектов. Это закреплено policy-тестом, который запрещает `.Remove`, `.RemoveRange`, `ExecuteDelete` и raw `DELETE FROM` вне EF migrations. Исключения допустимы только для тестовой очистки и migration DDL.

## Открытые Хвосты

- Реализовать backend restore для оставшихся пользовательских форм и будущего начисления участников сборов.
- Принять бизнес-решение по финансовым операциям: прямой restore или обратная операция с audit-связью.
- Составить отдельную таблицу всех форм сохранения: create/edit/delete/restore/cancel/import/export.
- Продолжить проверку всех frontend confirmation dialogs и единых стилей контролов.
