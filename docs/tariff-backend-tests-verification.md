# Backend Tariff Tests Verification

Этот документ фиксирует evidence для закрытия Stage 3 пункта backend-тестов расчетов и историчности тарифов.

## Расчеты

- [x] `fixed`: `GenerateRegularAccrualsAsync_CreatesFixedAccrualsForActiveGarages` проверяет сумму, снимок тарифа, `TariffId` и audit.
- [x] `people`: `GenerateRegularAccrualsAsync_CalculatesPeopleAmountForEachActiveGarage` проверяет ставку на число людей, итог по активным гаражам и исключение архива.
- [x] `meter_water`: `GenerateRegularAccrualsAsync_CalculatesMeterAmountFromReading` проверяет расход воды и сумму.
- [x] `meter_electricity`: `GenerateRegularAccrualsAsync_CalculatesTieredElectricityAmountFromReading` проверяет пороги, ставки, сумму и снимок в audit.
- [x] `GenerateRegularAccrualsAsync_RejectsSecondRunForSameMonth` защищает месяц от повторного начисления.

## Историчность И Валидация

- [x] `GenerateRegularAccrualsAsync_AppliesTariffOnlyFromEffectiveMonth` проверяет отказ до даты действия и начисление с effective month.
- [x] `GenerateRegularAccrualsAsync_KeepsExistingAccrualAmountAfterTariffUpdate` подтверждает неизменность проведенного снимка.
- [x] Service-тесты справочников покрывают новую effective-dated версию, дубли названия+даты, сортировку, неподдержанную базу, audit и запрет переноса даты позже начисления.
- [x] Controller-тесты покрывают conflict и bad request для тарифных ограничений.
- [x] `ProjectWideRoadmapStatusTests.TariffBackendTestsAreCompleteWhenAllBasesVersionsSnapshotsValidationDuplicatesAndAuditAreCovered` связывает roadmap с этим покрытием.

## Acceptance Notes

- [x] Новая запись "Что нового" не нужна: production-код, API и бизнес-правила не изменялись; закрыт test/evidence статус уже существующей функциональности.
- [x] При недоступной локальной PostgreSQL migrations проверяются idempotent EF script, а сервисные сценарии выполняются на SQLite.
