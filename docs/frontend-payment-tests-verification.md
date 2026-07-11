# Frontend Payment Tests Verification

Этот документ фиксирует evidence для закрытия roadmap-пункта Stage 6 "Добавить React-тесты таблиц, модалок, платежей, подсветок, предупреждений и ошибок".

## Закрытая Область

- [x] Таблицы платежного раздела покрыты workflow-тестами: поступления, выплаты, история платежей, начисления владельцев, начисления поставщиков, показания счетчиков и server-side ведомости.
- [x] Платежные dialogs покрыты открытием, безопасным фокусом, Escape/отменой, возвратом фокуса, подтверждением и обязательными причинами.
- [x] Context menu покрыт для строк платежей, начислений, начислений поставщиков, показаний счетчиков и пустых таблиц.
- [x] Формы поступлений, выплат, ручных начислений, зарплатных начислений, показаний и регулярных начислений покрыты успешными сценариями и client validation.
- [x] Ошибки server-side ведомостей поступлений/выплат покрыты пустыми состояниями без возврата prototype/demo-строк.
- [x] Read-only/permission state покрыт: пользователь без write-permission не вызывает защищенные действия платежей.
- [x] Предупреждения покрыты: warning по разрыву истории электроэнергии и предупреждение перед закрытием измененного payment editor.
- [x] Подсветки покрыты: гаражи без показаний за выбранный месяц выделяются в платежном workflow.

## Проверочное Покрытие

- [x] `App.test.tsx` содержит `shows payments prototype and opens payment form modals`.
- [x] `App.test.tsx` содержит `loads selected garage income worksheet from finance backend`.
- [x] `App.test.tsx` содержит `loads expense worksheet from finance backend`.
- [x] `App.test.tsx` содержит `does not show prototype expense rows when expense worksheet is unavailable`.
- [x] `App.test.tsx` содержит `edits income operation from payments table`.
- [x] `App.test.tsx` содержит `edits expense operation from payments table with confirmation`.
- [x] `App.test.tsx` содержит `does not call finance APIs when payment forms fail client validation`.
- [x] `App.test.tsx` содержит `cancels income operation with required reason from payments workspace`.
- [x] `App.test.tsx` содержит `cancels expense operation with required reason from payments table context menu`.
- [x] `App.test.tsx` содержит `cancels accruals and meter readings with required reasons from payments workspace`.
- [x] `App.test.tsx` содержит `warns before closing changed payment editor`.
- [x] `App.test.tsx` содержит `shows electricity gap warning returned by API`.
- [x] `App.test.tsx` содержит `highlights garages without meter readings for selected month`.
- [x] `App.test.tsx` содержит `keeps dictionary and payment actions read-only without write permissions`.
- [x] `ProjectWideRoadmapStatusTests.FrontendPaymentTestCoverageRoadmapItemIsCompleteWhenTablesDialogsPaymentsWarningsAndErrorsAreCovered` закрепляет этот evidence.

## Acceptance Notes

- [x] Новая запись "Что нового" не нужна: production-код, API, бизнес-правила и пользовательские сценарии не менялись; закрыт roadmap-status/evidence по уже существующему React coverage.
- [x] Локальная PostgreSQL-проверка выполняется через idempotent EF migration script, если локальный PostgreSQL/psql/docker недоступны.
