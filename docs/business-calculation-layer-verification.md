# Business Calculation Layer Verification

Этот документ фиксирует evidence для закрытия архитектурного правила размещения бизнес-расчетов в сервисах и домене.

## Закрытая Область

- [x] Тарифы, ставки, даты действия и денежное округление обрабатываются в dictionary/finance services и domain helpers.
- [x] Ручные и регулярные начисления, счетчики, долги, переплаты и распределение платежей выполняются в `FinanceService`.
- [x] Остатки и доступные суммы фондов рассчитываются в `FundService`.
- [x] Access dry-run, проверки файла и import report выполняются в `ImportService`.
- [x] Сводные, доходные и расходные итоги/агрегации выполняются в `ReportService`.
- [x] HTTP-контроллеры не используют domain calculation helpers и LINQ-агрегации.

## Проверочное Покрытие

- [x] `DictionaryServiceTests` покрывает тарифы, даты действия, валидацию баз и округление.
- [x] `FinanceServiceTests` покрывает начисления, счетчики, долги, балансы, распределение платежей и крайние финансовые сценарии.
- [x] `FundServiceTests` покрывает пересчет остатков, лимиты операций, отмену и восстановление.
- [x] `ImportServiceTests` покрывает Access dry-run, ошибки расширения, повторный файл, отчет и audit.
- [x] `ReportServiceTests` покрывает сводный отчет, поступления, выплаты, долги, обязательства, фильтры и итоги.
- [x] `ControllerThinnessTests.Controllers_DoNotContainBusinessCalculationHelpersOrAggregations` удерживает расчеты вне HTTP layer.
- [x] `ProjectWideRoadmapStatusTests.BusinessCalculationsRemainInServicesAndDomainWhenControllersForbidDomainHelpers` удерживает roadmap-status вместе с production/service coverage.

## Future Rule

- [x] Любой новый бизнес-расчет добавляется в application/domain layer и получает unit/service tests до подключения controller endpoint.

## Acceptance Notes

- [x] Новая запись "Что нового" не нужна: production-расчеты и пользовательские правила не менялись.
- [x] Схема данных в этом срезе не меняется; при недоступной локальной PostgreSQL миграции проверяются idempotent EF script.
