# Balance Model Decision Checklist

Этот checklist фиксирует, что в модели баланса уже реализовано, а какие правила требуют бизнес-подтверждения перед закрытием roadmap-пункта.

## Что Уже Закреплено В Коде

- [x] Отрицательный долг отображается как переплата в UI-форматтерах и отчетных состояниях.
- [x] Стартовый баланс гаража принимает положительный долг и отрицательную переплату.
- [x] Поступление владельца возвращает `paymentAllocations`, `debtBefore` и `debtAfter`.
- [x] Выплата поставщику возвращает `paymentAllocations`, `debtBefore` и `debtAfter`.
- [x] Платежи распределяются по старейшим долгам владельца и поставщика.
- [x] Стартовый долг можно закрыть системной оплатой opening debt.
- [x] История баланса гаража показывает помесячный running debt.
- [x] Ручные начисления и корректировки автосумм требуют комментарий и пишут audit-summary.

## Что Требует Бизнес-Решения

- [decision] Переплата переносится на будущие месяцы автоматически или должна закрываться отдельной операцией.
- [decision] Можно ли вручную списывать/возвращать переплату, каким документом и с каким audit-событием.
- [decision] Можно ли ручной корректировкой сделать отрицательное начисление, или переплата создается только платежом.
- [decision] Как показывать переплату в отчетах: отдельной колонкой, отрицательным долгом или обоими способами.
- [decision] Как закрывать переплату при смене владельца гаража: переносить на гараж, владельца или требовать ручное решение.
- [decision] Нужен ли отдельный тип операции для возврата переплаты из кассы/банка.

## Проверки Перед Закрытием

- [ ] Business rules for overpayment carry-forward, write-off, refund and owner change are approved.
- [ ] Backend service tests cover the approved rules.
- [ ] Controller/API tests cover validation, permission denial and audit for approved overpayment actions.
- [ ] React workflow tests cover visible states, validation and report display for approved overpayment actions.
- [ ] Reports and exports show debt/overpayment according to the approved rule.
- [ ] Roadmap line can move to `[x]` only after the approved rule is implemented, tested and verified on a local DB or migration-safe substitute.

## Guard

- [ ] Guard `BalanceModelRoadmapItemRequiresBusinessDecisionForManualCorrectionsAndOverpaymentClosure` can be removed only after the business decision is implemented and verified.
