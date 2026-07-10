# Receipt Printing Design

Документ фиксирует целевую архитектуру печати чеков и квитанций до выбора юридического сценария, оборудования и реального тестового устройства. Здесь нет секретов, параметров кассы, персональных данных, банковских реквизитов или реальных документов кооператива.

## Source Materials Considered

- `docs/project-roadmap.md`, этап 10: печать чеков и квитанций.
- Текущие backend-контракты `ReceiptPrintingAdapter`, `ReceiptPrintingService`, `ReceiptPrintingContracts` и `IntegrationsController`.
- Текущий frontend: статус печати в настройках и действия печати/отмены/повторной печати в истории платежей гаража.
- Правила проекта: финансовые и интеграционные действия требуют backend-audit, защищенного хранения параметров интеграций и явных прав.

## Assumptions And Decisions

- `[decision]` Нужно выбрать юридический сценарий: фискальный чек по 54-ФЗ или внутренняя печатная квитанция.
- `[decision]` Нужно выбрать оборудование и способ подключения: АТОЛ, Эвотор, другое устройство, локальный драйвер, HTTP-сервис, файл обмена или ручной PDF.
- `[decision]` Нужно подтвердить, кто обязан выдавать чек и какие операции подлежат печати.
- `[decision]` Нужно утвердить обязательные реквизиты внутренней квитанции: название ГСК, ИНН/реквизиты, подписи, назначение платежа, номер гаража, период, сумма, номер документа.
- До решений выше production-код остается в режиме безопасной регистрации действий через `DisabledReceiptPrintingAdapter`.
- Для финансовых операций печать разрешается только для поступлений владельцев; выплаты и отмененные поступления не печатаются как первичные квитанции.

## Target User Flows

1. Бухгалтер открывает историю платежей гаража и выбирает поступление.
2. Для первичной печати нажимает `Печать квитанции`.
3. Для отмены печати указывает причину; действие попадает в историю как `receipt.print_canceled`.
4. Для повторной печати указывает причину; копия должна явно отличаться от первичного документа.
5. Администратор в настройках видит, настроена ли печать, без раскрытия параметров устройства.
6. В общей истории изменений видны действие, документ, гараж, владелец, месяц, сумма, статус адаптера и безопасный код ошибки.

## Printable Document Types

### Internal Receipt

Минимальный внутренний документ для локальной установки и нефискального сценария:

- номер квитанции или номер финансового документа;
- дата операции и учетный месяц;
- гараж и владелец;
- вид поступления;
- сумма цифрами и при необходимости прописью;
- назначение платежа;
- отметка `Копия` для повторной печати;
- причина отмены или повторной печати хранится в audit, но не обязана печататься на первичной квитанции;
- технический идентификатор операции не должен заменять пользовательский номер документа, но может использоваться как fallback.

### Fiscal Receipt

Фискальный сценарий должен быть отдельной реализацией адаптера:

- адаптер получает нормализованную модель платежа без доступа к `GarageBalanceDbContext`;
- драйвер или внешний сервис фискализации возвращает статус, код устройства и внешний идентификатор чека;
- фискальные реквизиты, QR-код и ФН/ФД/ФПД хранятся только после подтверждения выбранного оборудования и требований 54-ФЗ;
- ошибки фискализации не должны терять audit-событие запроса.

## Integration Layers

Текущая граница уже правильная и должна сохраняться:

- `IntegrationsController`: HTTP surface, авторизация, DTO, mapping ошибок.
- `ReceiptPrintingService`: проверка операции, нормализация action/reason, сбор безопасной модели, audit.
- `IReceiptPrintingAdapter`: единственная точка подключения устройства, PDF renderer или локального print-сервиса.
- `DisabledReceiptPrintingAdapter`: безопасный режим до реального устройства, возвращает `pending_adapter`.

Контроллеры не должны обращаться напрямую к драйверам, `GarageBalanceDbContext`, принтерам, файловой системе печати или внешним API.

## Adapter Request Model

Текущий `ReceiptPrintingAdapterRequest` является минимальным payload для следующих срезов:

- `Action`: `print`, `cancel`, `reprint`.
- `FinancialOperationId`: внутренняя связь с платежом.
- `DocumentNumber`: пользовательский номер документа или fallback.
- `Amount`, `OperationDate`, `AccountingMonth`.
- `GarageNumber`, `OwnerName`, `IncomeTypeName`.
- `Reason`: обязательна для `cancel` и `reprint`.

После выбора сценария модель можно расширить, но только совместимо:

- `ReceiptKind`: `internal` или `fiscal`.
- `CopyMark`: для повторной печати.
- `TemplateCode`: выбранная печатная форма.
- `FiscalDeviceId`: только безопасный идентификатор настройки, не секрет подключения.
- `Lines`: детализация, если нужно печатать несколько назначений платежа.

## Statuses And Errors

Базовые статусы адаптера:

- `pending_adapter`: действие зарегистрировано, реальная печать еще не подключена.
- `printed`: документ сформирован или устройство подтвердило печать.
- `device_error`: устройство, драйвер или локальный print-сервис вернул ошибку.
- `template_error`: шаблон не найден или содержит неподдержанные поля.
- `fiscalization_error`: фискализация не завершилась.
- `reprint_required_reason`: повторная печать отклонена без причины.

Ошибки должны возвращаться как безопасные `ProblemDetails`/DTO-поля без строк подключения, токенов, полных ответов драйвера и персональных данных.

## Audit And History

Каждое действие создает backend-audit:

- `receipt.print_requested`;
- `receipt.print_canceled`;
- `receipt.reprint_requested`.

Audit metadata должна содержать:

- `receiptAction`;
- `operationKind`;
- `amount`;
- `incomeTypeName`;
- `adapterStatus`;
- `deviceResponseCode`;
- `externalReceiptId`, если внешний контур его вернул;
- связанные поля `RelatedGarageNumber`, `RelatedAccountingMonth`, `RelatedCounterpartyName`, `RelatedDocumentNumber`.

Причина обязательна для `cancel` и `reprint`. Plaintext-секреты оборудования, токены, строки подключения, полные персональные данные и raw device logs не пишутся.

## Permissions

- Статус печати и действия печати требуют backend-права `payments.write`.
- UI может скрывать кнопки, но запрет должен оставаться на backend.
- Будущие админ-настройки устройства должны требовать отдельное право на настройки или администрирование, если такое право будет введено.

## Frontend Expectations

- В настройках показывать только безопасный статус: настроено/не настроено, доступна/ожидает адаптер, количество защищенных настроек.
- В платежах использовать icon buttons с понятными `aria-label`: печать, отмена печати, повторная печать.
- `cancel` и `reprint` всегда открывают confirmation dialog с обязательной причиной.
- Повторная печать должна визуально объяснять, что это копия, а не первичный документ.
- Ошибки устройства показываются в рабочем статусе и остаются в общей истории.

## Test Plan

Backend:

- adapter factories and disabled adapter without leaking request data;
- service success for `print`, `cancel`, `reprint`;
- reason required for cancel/reprint;
- rejected expense/canceled operation cases;
- controller success, validation, conflict/not-found mapping;
- authorization for `payments.write`;
- audit metadata and masking.

Frontend:

- receipt status in settings;
- print/cancel/reprint dialogs, Escape and focus restore;
- required reason validation;
- backend error states;
- copy/reprint warning text once real template exists.

Integration/acceptance:

- internal receipt PDF/print preview on local PC;
- selected fiscal device or emulator;
- device unavailable/retry behavior;
- printed sample checked by accountant/admin.

## Definition Of Done

- `[x]` Design separates internal receipt and fiscal receipt paths without forcing a legal decision.
- `[x]` Integration boundary stays behind `IReceiptPrintingAdapter`.
- `[x]` Audit, permissions, statuses, errors and protected settings rules are defined.
- `[x]` Frontend expectations and test plan are documented.
- `[decision]` Legal scenario, equipment and mandatory receipt fields are still business decisions.
- `[acceptance]` Real print/emulator acceptance remains required before closing Stage 10.
