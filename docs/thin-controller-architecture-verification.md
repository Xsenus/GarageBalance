# Thin Controller Architecture Verification

Этот документ фиксирует evidence для закрытия верхнеуровневого правила тонких backend-контроллеров.

## Закрытая Область

- [x] Все текущие API-контроллеры получают application use cases через interfaces.
- [x] Контроллеры не зависят напрямую от EF Core, `GarageBalanceDbContext`, repositories или Infrastructure services.
- [x] Контроллеры не выполняют raw SQL/DDL и не материализуют бизнес-выборки.
- [x] Controller actions не возвращают Domain entities через HTTP-контракт.
- [x] Авторизация и permission policies проверяются отдельным общим policy-тестом.
- [x] Dangerous и mutating actions не используют безопасные HTTP methods; причина опасного действия ограничена backend request contract.

## Проверочное Покрытие

- [x] `ControllerThinnessTests.Controllers_DoNotUseEfCoreOrInfrastructureDataDirectly` сканирует все текущие `*Controller.cs`.
- [x] `ControllerThinnessTests.ControllerConstructors_DoNotDependOnInfrastructureServices` запрещает Infrastructure, DbContext и Repository dependencies.
- [x] `ControllerThinnessTests.ControllerConstructors_DependOnServiceAbstractionsOnly` запрещает concrete project services.
- [x] `ControllerThinnessTests.ControllerActions_DoNotExposeDomainEntities` защищает DTO boundary.
- [x] `ControllerAuthorizationCoverageTests` проверяет authorization metadata текущих controller actions.
- [x] `ProjectWideRoadmapStatusTests.ThinControllerArchitectureIsCompleteWhenAllControllersUseApplicationAbstractionsAndPolicyTests` удерживает roadmap-status вместе с policy coverage.

## Future Rule

- [x] Любой новый controller endpoint обязан пройти существующие architecture/authorization policy-тесты и получить свой controller workflow test.

## Acceptance Notes

- [x] Новая запись "Что нового" не нужна: это архитектурная гарантия без изменения пользовательского поведения.
- [x] Схема и production-код в этом срезе не меняются; при недоступной локальной PostgreSQL миграции проверяются idempotent EF script.
