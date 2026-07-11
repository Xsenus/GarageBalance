# Expense Excel Scenario Decision Checklist

Этот checklist фиксирует состояние roadmap-пункта Stage 6 "Реализовать Excel-сценарий выплат" и решения, без которых его нельзя безопасно закрывать как `[x]`.

## Уже Готово

- [x] Backend строит месячную ведомость выплат через `GetExpenseWorksheetAsync`.
- [x] Ведомость объединяет начисления поставщиков, выплаты поставщикам, активных сотрудников, выплаты сотрудникам и сборы по связанным видам поступлений.
- [x] В строках ведомости есть `Оплачено`, `Остаток`, `Собрано`, `Разница`, итоги, касса и банк.
- [x] UI загружает серверную форму выплат за выбранный месяц и показывает таблицу `Форма выплат`.
- [x] UI позволяет добавить начисление поставщику по счету через `createSupplierAccrual`.
- [x] UI позволяет провести выплату поставщику через `createExpense` и выплату сотруднику через `createStaffPayment`.
- [x] Кнопка `Оплатить` открывает форму выплаты с предложенной суммой остатка, а для авансовых выплат и выплат без чека кнопка не показывается.
- [x] Backend проверяет доступный банк/кассу для выплат и пишет audit по созданию, изменению и отмене финансовых строк.

## Нужны Бизнес-Решения

- [decision] Нужно определить источник ручного ввода стоимости услуг по счетам: отдельная ячейка в Excel-таблице выплат, существующая форма `Добавить начисление`, массовый ввод счетов или импорт счетов.
- [decision] Нужно определить, что переносится в следующий месяц: неоплаченный остаток поставщика, несобранная с владельцев сумма, отрицательная `Разница`, долг конкретного поставщика или отдельная строка переноса.
- [decision] Нужно выбрать момент переноса: вручную бухгалтером при закрытии месяца, автоматически при открытии нового месяца, при генерации регулярных начислений или отдельной операцией закрытия месяца.
- [decision] Нужно определить правило обнуления полностью оплаченных услуг: скрывать строку в новом месяце, переносить с нулем, создавать новую строку только при новом счете или закрывать отдельным статусом.
- [decision] Нужно определить audit-событие и документ для переноса/обнуления, чтобы было понятно, кто и почему закрыл обязательство.
- [decision] Нужно определить, как отмена выплаты, отмена начисления или возврат после переноса влияют на уже созданный новый месяц.

## Required Implementation After Decisions

- [ ] Backend model/use case for approved supplier obligation rollover and fully-paid zeroing.
- [ ] Controller/API tests for rollover, zeroing, permission denial, validation and audit.
- [ ] EF migration/indexes if the chosen model needs persistent carry-forward rows or close statuses.
- [ ] Frontend workflow for approved manual cost entry and month-close/rollover action.
- [ ] React tests for visible rollover rows, zeroed/hidden paid services, validation, error and permission states.
- [ ] Local DB or migration-safe verification that repeated month close is idempotent.
- [ ] Release note for cooperative staff/admins describing the final monthly payout workflow.

## Current Status

- [decision] Roadmap-пункт нельзя закрывать как `[x]`, пока решения выше не утверждены и не покрыты backend/frontend тестами.

## Guard

- [ ] Guard `ExpenseExcelScenarioRoadmapItemRequiresBusinessDecisionForRolloverZeroingAndManualCostEntry` can be removed only after the business decision is implemented and verified.
