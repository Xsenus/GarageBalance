# Покрытие Форм И Действий Сохранения

Дата инвентаризации: 03.07.2026.

Этот документ разделяет текущие формы и действия проекта на `create`, `edit`, `delete/archive`, `restore`, `cancel`, `import`, `export` и `generated` flows. Он дополняет `docs/audit-event-coverage.md` и `docs/soft-delete-cancel-coverage.md`.

## Источники

- `frontend/src/App.tsx`
- `frontend/src/App.test.tsx`
- `backend/GarageBalance.Api/Controllers/AuthController.cs`
- `backend/GarageBalance.Api/Controllers/UsersController.cs`
- `backend/GarageBalance.Api/Controllers/DictionariesController.cs`
- `backend/GarageBalance.Api/Controllers/FinanceController.cs`
- `backend/GarageBalance.Api/Controllers/ImportController.cs`
- `backend/GarageBalance.Api/Controllers/ReportsController.cs`
- `backend/GarageBalance.Api.Tests/**`

## Backend API Flows

| Раздел | Create | Edit | Delete/Archive | Restore | Cancel | Import | Export/Generate |
|---|---|---|---|---|---|---|---|
| Auth | `BootstrapAdmin` | `ChangeOwnPassword` | Нет | Нет | Нет | Нет | Нет |
| Users | `CreateUser` | `UpdateUser` | отключение через `UpdateUser` | `RestoreUser` | Нет | Нет | Нет |
| Dictionaries: owners | `CreateOwner` | `UpdateOwner` | `ArchiveOwner` | `RestoreOwner` | Нет | Нет | Нет |
| Dictionaries: garages | `CreateGarage` | `UpdateGarage` | `ArchiveGarage` | `RestoreGarage` | Нет | Нет | Нет |
| Dictionaries: supplier groups | `CreateSupplierGroup` | `UpdateSupplierGroup` | `ArchiveSupplierGroup` | `RestoreSupplierGroup` | Нет | Нет | Нет |
| Dictionaries: suppliers | `CreateSupplier` | `UpdateSupplier` | `ArchiveSupplier` | `RestoreSupplier` | Нет | Нет | Нет |
| Dictionaries: income types | `CreateIncomeType` | `UpdateIncomeType` | `ArchiveIncomeType` | `RestoreIncomeType` | Нет | Нет | Нет |
| Dictionaries: expense types | `CreateExpenseType` | `UpdateExpenseType` | `ArchiveExpenseType` | `RestoreExpenseType` | Нет | Нет | Нет |
| Dictionaries: tariffs | `CreateTariff` | `UpdateTariff` | `ArchiveTariff` | `RestoreTariff` | Нет | Нет | Нет |
| Finance: operations | `CreateIncome`, `CreateExpense`, `CreateStaffPayment`, `CreateGarageDebtPayment` | `UpdateOperation` | Нет | `RestoreOperation` | `CancelOperation` | Нет | Нет |
| Finance: owner accruals | `CreateAccrual` | `UpdateAccrual` | Нет | `RestoreAccrual` | `CancelAccrual` | Нет | `GenerateRegularAccruals`, `GenerateRegularCatalogAccruals` |
| Finance: supplier accruals | `CreateSupplierAccrual` | `UpdateSupplierAccrual` | Нет | `RestoreSupplierAccrual` | `CancelSupplierAccrual` | Нет | `GenerateSupplierGroupSalaryAccruals` |
| Finance: meter readings | `CreateMeterReading` | `UpdateMeterReading` | Нет | `RestoreMeterReading` | `CancelMeterReading` | Нет | Нет |
| Funds: operations | `CreateFundOperation` | `UpdateFundOperation` | Нет | `RestoreFundOperation` | `CancelFundOperation` | Нет | reverse via opposite `CreateFundOperation` |
| Import Access | Нет | `ResolveQuarantineItem`, `CancelAccessImportApplyRequest` | Нет | Нет | `RequestAccessImportRollback` | `DryRunAccessImport`, `RequestAccessImportApply` | `ExportAccessImportRunReport` |
| Integrations: 1C Fresh | Нет | `StartOneCFreshSync`, `RetryOneCFreshSync` | Нет | Нет | Нет | Нет | Нет |
| Reports | Нет | Нет | Нет | Нет | Нет | Нет | `Get*Report`, `Export*ReportXlsx`, `Export*ReportPdf` |
| Audit | Нет | Нет | Нет | Нет | Нет | Нет | `ExportEvents`, `ExportEventsXlsx` |

## Frontend Формы И Диалоги

| Экран | Форма/диалог | Категория | Backend или прототип | Confirmation / no-op |
|---|---|---|---|---|
| Авторизация | вход | auth | Backend API | без confirmation |
| Первый администратор | bootstrap admin | create | Backend API | без публичной кнопки на текущем auth-экране |
| Смена пароля | password change | edit | Backend API | backend audit, без `было/стало` пароля |
| Пользователи | редактор пользователя | create/edit | Backend API | edit с изменениями показывает diff; no-op закрывается без update |
| Пользователи | отключение пользователя | delete/archive | Backend API | confirmation с обязательной причиной |
| Пользователи | восстановление пользователя | restore | Backend API | confirmation dialog |
| Справочники | единый редактор записи | create/edit | Backend API | edit с изменениями показывает diff; no-op закрывается без update |
| Справочники | архивирование записи | delete/archive | Backend API | confirmation с обязательной причиной |
| Справочники | восстановление записи | restore | Backend API | confirmation dialog и понятные restore-conflict сообщения |
| Платежи/финансы | финансовый редактор | create/edit | Backend API | закрытие с несохраненными изменениями требует confirmation; create/edit сохраняют через API |
| Платежи/финансы | отмена записи | cancel | Backend API | confirmation с обязательной причиной |
| Платежи/финансы | сдача кассы в банк | create | Backend API через `FundsClient.createOperation` | modal form, Escape/отмена без API, reason `Сдача кассы в банк ...` |
| Платежи-прототип | выплата, начисление, полная оплата | create | React-state прототип | modal forms, без backend |
| Управление фондами | пополнить/изъять, изменить, отменить, восстановить, создать обратную операцию | create/edit/cancel/restore | Backend API | modal forms/dialogs с причиной, confirmation, restore и reverse |
| Импорт Access | dry-run загрузка файла | import | Backend API | file validation, progress/status |
| Импорт Access | заявка на фактический импорт | import | Backend API | confirmation с причиной и подтверждением backup; статус `import_requested` |
| Импорт Access | отмена заявки на импорт | cancel | Backend API | confirmation с обязательной причиной; статус `import_request_cancelled` |
| Импорт Access | rollback-заявка dry-run | cancel | Backend API | confirmation с обязательной причиной; фактический rollback не выполняется |
| Импорт Access | закрытие карантина | edit/resolve | Backend API | button action с audit; отдельного confirmation пока нет |
| Импорт Access | JSON-отчет dry-run | export | Backend API | POST export, пишет audit |
| Настройки | запуск и повтор 1C Fresh | edit | Backend API | confirmation с optional comment; `pending_adapter`, без plaintext refresh token |
| Отчеты | фильтры и просмотр | generated/read | Backend API | generate/read, пишет audit для формирования |
| Отчеты | XLSX/PDF export | export | Backend API | POST export, пишет audit |
| История изменений | CSV/XLSX export | export | Backend API | export текущей выборки |
| Сборы | объявление/изменение/архив/восстановление сбора | create/edit/archive/restore | Backend API | цель, суммы, период, участники, перенос долга; audit с причиной архива |
| Тарифы-прототип | редактируемые значения, услуга, сбор | create/edit | React-state прототип | изменения тарифов с confirmation; локальные данные до backend |
| Тарифы-прототип | нерегулярный платеж | delete/restore | React-state прототип | delete с причиной, restore с confirmation |
| Контрагенты-прототип | гаражи, поставщики, персонал | create/edit | React-state прототип | модалки разделов; delete/restore локальные |
| Показания-прототип | годовая таблица показаний | edit | React-state прототип | редактирование ячеек с confirmation; локальные данные до backend |

## Что Уже Закреплено Тестами

- Backend controller tests покрывают success, validation и часть not found/conflict сценариев по текущим endpoints.
- `ControllerThinnessTests` закрепляет thin controllers, запрет dangerous safe methods и обязательный body reason для `Archive*`, `Cancel*`, `Delete*`.
- Frontend workflows в `App.test.tsx` покрывают user, dictionary, finance, import, reports, tariffs prototype, contractors prototype, meter prototype, funds prototype и payment prototype сценарии.
- Документационные tests закрепляют API documentation, audit coverage и soft-delete/cancel coverage.

## Открытые Хвосты

- Для будущих backend-сценариев начисления участников сборов и пользовательских форм сборов добавить отдельные строки create/edit/archive/restore/cancel.
- Для импортного resolve и будущего rollback решить, нужен ли отдельный confirmation dialog сверх текущего backend audit.
- Для финансовых операций принять бизнес-решение: restore или обратная операция.
- Следующим срезом проверить все UI controls: кнопки, icon-only кнопки, input, textarea, select, date/month/year controls, checkbox, toggle, tabs, dialogs, tables, pagination, filters, empty/loading/error states.
