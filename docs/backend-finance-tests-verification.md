# Backend Finance Tests Verification

Этот документ фиксирует evidence для закрытия roadmap-пункта Stage 6 "Добавить backend-тесты расчетов, долгов, ручных корректировок, прав и audit".

## Закрытая Область

- [x] Создание поступлений и выплат проверяется service-тестами с audit-summary, документами, комментариями и связями с гаражами/поставщиками.
- [x] Долги владельцев и поставщиков проверяются через `debtBefore`, `debtAfter`, распределение платежей по старейшим долгам и историю баланса.
- [x] Стартовый долг гаража закрывается системной оплатой и проверяется через allocations.
- [x] Ручные начисления владельцев и поставщиков требуют комментарий и пишут audit-summary.
- [x] Ручные корректировки начислений владельцев и поставщиков пишут `было/стало`.
- [x] Отмена и восстановление финансовых операций, начислений, начислений поставщиков и показаний счетчиков проверяют active totals, duplicate conflicts и audit.
- [x] Регулярные начисления, начисления по каталогу услуг, сборы и зарплатные начисления проверяют результат, пропуски и audit.
- [x] Показания воды/электроэнергии проверяют расход, округление, дубли, отмену, восстановление и audit.
- [x] Серверные ведомости поступлений и выплат проверяют строки, итоги, кассу/банк и собранные суммы.
- [x] Backend-права проверяются через controller authorization coverage и permission handler: finance/funds mutating actions требуют `payments.write`, read actions требуют read policies, а permission claim реально проверяется handler-ом.

## Проверочное Покрытие

- [x] `FinanceServiceTests` покрывает расчеты, долги, ручные корректировки, дубли, отмены, восстановления, ведомости, кассу/банк и audit.
- [x] `FinanceControllerTests` покрывает HTTP-surface, actor user, validation/not found/conflict mapping и обязательные причины отмены.
- [x] `ControllerAuthorizationCoverageTests.FinanceActionsRequireExpectedPaymentPermissions` закрепляет `payments.read` на контроллере и `payments.write` на mutating actions.
- [x] `ControllerAuthorizationCoverageTests.FundsActionsRequireReportsReadAndPaymentsWritePermissions` закрепляет права фондов для кассы/банка.
- [x] `PermissionAuthorizationHandlerTests` проверяет успешную и неуспешную авторизацию по permission claim.
- [x] `ProjectWideRoadmapStatusTests.BackendFinanceTestCoverageRoadmapItemIsCompleteWhenCalculationsDebtsPermissionsAndAuditAreCovered` закрепляет этот evidence.

## Acceptance Notes

- [x] Новая запись "Что нового" не нужна: production-код, API, бизнес-правила и пользовательские сценарии не менялись; закрыт roadmap-status/evidence по уже существующему backend coverage.
- [x] Локальная PostgreSQL-проверка выполняется через idempotent EF migration script, если локальный PostgreSQL/psql/docker недоступны.
