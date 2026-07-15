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
- [x] `App.test.tsx` содержит отдельные детерминированные сценарии отмены начисления владельцу, начисления поставщику и показания счетчика с обязательной причиной; они не зависят от общего длинного таймаута.
- [x] `App.test.tsx` содержит `warns before closing changed payment editor`.
- [x] `App.test.tsx` содержит `shows electricity gap warning returned by API`.
- [x] `App.test.tsx` содержит `highlights garages without meter readings for selected month`.
- [x] `App.test.tsx` содержит `keeps dictionary and payment actions read-only without write permissions`.
- [x] `ProjectWideRoadmapStatusTests.FrontendPaymentTestCoverageRoadmapItemIsCompleteWhenTablesDialogsPaymentsWarningsAndErrorsAreCovered` закрепляет этот evidence.

## Acceptance Notes

- [x] Новая запись "Что нового" не нужна: production-код, API, бизнес-правила и пользовательские сценарии не менялись; закрыт roadmap-status/evidence по уже существующему React coverage.
- [x] Локальная PostgreSQL-проверка выполняется через idempotent EF migration script, если локальный PostgreSQL/psql/docker недоступны.

## История выполнения

- 2026-07-15 — длинный сценарий отмены начислений и показаний разделён на три независимых workflow-теста. Все исходные проверки Escape, обязательной причины, сохранения записи после отмены dialog и удаления после подтверждения сохранены, а каждый финансовый поток теперь укладывается в стандартный таймаут даже при сборе покрытия в CI.
- 2026-07-13 — ускорено открытие раздела платежей: вспомогательные финансовые превью вынесены из блокирующей первоначальной загрузки и подгружаются отдельно в фоне, а история активного гаража ограничена последними 100 операциями. Поиск гаражей переведён на отложенный серверный запрос с лимитом 20; результаты поддерживают выбор нескольких гаражей флажками и переключение активной карточки. Поведение закреплено workflow-тестом формы платежей, frontend build/lint и полным регрессом; ручная браузерная проверка и результат PostgreSQL smoke-check фиксируются в итоговой передаче задачи.
- 2026-07-13 — добавлена глобальная настройка режима открытия платежей. Стандартное значение `выключено` оставляет экран пустым до поиска и не запускает запросы общей ведомости. Администратор может включить на вкладке «Настройки → Отображение» постраничный обзор поступлений и выплат; поиск гаражей работает в обоих режимах. Настройка хранится в PostgreSQL, защищена правами чтения платежей/управления пользователями и записывает изменение в audit.
- 2026-07-13 — проверка настройки завершена: backend 1487/1487, полный frontend 337/337 и отдельные API-тесты настройки 3/3 прошли; production build, ESLint, bundle budget, `dotnet format`, UTF-8/no BOM и idempotent migration SQL прошли. Миграция применена к локальной PostgreSQL `garagebalance_local` на порту 5433, таблица по умолчанию пуста и сервис возвращает выключенный режим. Встроенное браузерное подключение повторно не запустилось из-за отсутствующего системного пути, поэтому UI закреплён component/workflow-тестами; временные артефакты migration SQL удалены.
