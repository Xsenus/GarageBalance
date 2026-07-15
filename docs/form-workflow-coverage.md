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
| Integrations: receipt printing | Нет | `RegisterReceiptPrintingAction` | Нет | Нет | `RegisterReceiptPrintingAction` | Нет | `RegisterReceiptPrintingAction` |
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
| Настройки | запуск и повтор 1C Fresh | edit | Backend API, UI временно скрыт | Вкладка интеграций не отображается и не загружает статусы ни для одной роли; backend flow сохранен для будущего включения |
| История платежей гаража | печать, отмена и повтор квитанции | export/cancel | Backend API | icon-actions с confirmation; cancel/reprint требуют reason, `pending_adapter` до устройства |
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

## История выполнения

- 2026-07-15 — выполнен сквозной визуальный аудит на отдельной PostgreSQL-базе через реальный UI: создание и изменение владельца, гаражей, группы и поставщика, видов поступлений/выплат, тарифа, регулярной услуги, отдела, сотрудника и пользователя; архивирование и восстановление гаража; ввод показаний с проверкой после перезагрузки; регулярное и ручное начисление, построчная и полная оплата, начисление и выплата поставщику с проверкой ограничения банковского остатка; пополнение и изъятие фонда; все восемь отчетных вкладок, импортное пустое состояние, история изменений, «Что нового» и настройки. Найден и устранен серверный сбой полной оплаты под локалью `ru-RU`: денежная валидация больше не пытается разобрать точку как локальный десятичный разделитель. В самостоятельных формах гаража и поставщика добавлены доступные подсказки стартовых значений, а широкая форма владельца защищена от лишней горизонтальной прокрутки. Автоматическая проверка: backend `1514/1514`, frontend `480/480`, lint, production build, контроль бюджета bundle, форматирование, сборка решения и генерация идемпотентного SQL миграций. Проверка выполнялась только на синтетических данных изолированного контура; реальные пользовательские данные не изменялись.
- 2026-07-13 — настройки получили внутреннюю вкладку «Безопасность». Формы 1С Fresh и печати временно скрыты настройкой `VITE_SHOW_INTEGRATION_SETTINGS=false`; защищённая настройка DaData доступна администратору отдельно, а её backend и frontend по-прежнему требуют разрешение `users.manage`.
- 2026-07-15 — поле адреса гаража подключено к общему combobox подсказок DaData: запрос выполняется только после ручного ввода с задержкой, выбор подставляет полный адрес, а при отсутствии ключа или ошибке сервиса ручной ввод остаётся доступным.
- 2026-07-15 — в карточках гаража и поставщика зарезервировано постоянное место под состояние поиска DaData; поиск по ИНН и адресу, отсутствие результатов, ошибка и выбор подсказки больше не меняют высоту строки формы, а длинное сообщение аккуратно ограничивается на узком экране.
- 2026-07-15 — combobox DaData больше не открывает сохранённые варианты при простом фокусе: запрос и раскрытие выполняются только после изменения текста. Адресные подсказки открываются вверх; список показывает до пяти строк на широком экране и до трёх на узком, сохраняя остальные варианты во внутренней прокрутке.
- 2026-07-15 — календари формы объявления сбора открываются вверх и закрываются при клике вне календаря или при открытии соседней даты. Суммы взноса и сбора выровнены вправо и используют единый формат с пробелами между разрядами, точкой и двумя десятичными знаками при просмотре и редактировании.

## Открытые Хвосты

- Для будущих backend-сценариев начисления участников сборов и пользовательских форм сборов добавить отдельные строки create/edit/archive/restore/cancel.
- Для импортного resolve и будущего rollback решить, нужен ли отдельный confirmation dialog сверх текущего backend audit.
- Для финансовых операций принять бизнес-решение: restore или обратная операция.
- Следующим срезом проверить все UI controls: кнопки, icon-only кнопки, input, textarea, select, date/month/year controls, checkbox, toggle, tabs, dialogs, tables, pagination, filters, empty/loading/error states.
