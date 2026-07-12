# React Tariff Tests Verification

Этот документ фиксирует evidence для закрытия Stage 3 пункта React-тестов форм тарифов, валидации и истории.

## Формы И Состояния

- [x] `creates electricity tariff with editable thresholds and three rates` проверяет создание ступенчатого тарифа и отображение ставок.
- [x] `confirms tariff dictionary edits with labels dates and electricity tier diff` проверяет no-op, поля `было/стало` и подтверждение изменения.
- [x] `shows backend error when tariff effective date moves after existing accruals` оставляет форму открытой и показывает серверное правило.
- [x] `validates tariff rate effective date and electricity tiers before calling api` проверяет ставку, дату, порядок и полноту ступеней без API.
- [x] `allows tariff management without broad dictionary write permission` закрепляет независимое право `tariffs.manage`.
- [x] `shows clear conflict message for $name restore conflicts` покрывает понятный restore conflict для дубля тарифа.
- [x] `edits tariffs and one-time payments without local history access` подтверждает отсутствие дублирующей локальной вкладки истории.

## Централизованная История

- [x] `shows tariff changes in central audit and opens tariffs workspace` показывает тарифное событие, поле, значения `было/стало`, причину и переход в `Тарифы и сборы`.
- [x] `ProjectWideRoadmapStatusTests.TariffReactTestsAreCompleteWhenFormsValidationConfirmationsPermissionsAndCentralAuditExist` связывает roadmap с React и shared validation coverage.

## Acceptance Notes

- [x] Новая запись "Что нового" не нужна: production-код, API и пользовательское поведение не менялись; закрыт test/evidence статус существующих возможностей.
- [x] Изменение не затрагивает schema; при недоступной PostgreSQL migrations проверяются idempotent EF script.
