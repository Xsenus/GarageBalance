# Архитектура и разработка GarageBalance

## Общая схема

Пользователь работает с React SPA. Frontend обращается к ASP.NET Core API по JSON/HTTP. API проверяет JWT и permissions, выполняет бизнес-операции через application-сервисы и сохраняет данные в PostgreSQL через Entity Framework Core.

```text
Browser → React/Vite → ASP.NET Core Controllers → Application/Domain → EF Core → PostgreSQL
```

В production frontend и API доступны через один nginx-домен. Запросы `/api/*` проксируются в backend; статические assets кэшируются, а `index.html` всегда запрашивается без долговременного кэша.

## Backend

- `Controllers` — маршруты, авторизация, DTO и HTTP-коды.
- `Application` — сценарии, транзакции и оркестрация.
- `Domain` — расчёты, правила денег, периодов, задолженности и начислений.
- `Infrastructure` — EF Core, PostgreSQL, файлы, внешние сервисы и фоновые задачи.

Контроллеры не обращаются напрямую к `GarageBalanceDbContext` и не возвращают domain entities. Запросы списков ограничиваются до materialization, а сортировка, фильтрация, пагинация и агрегирование выполняются PostgreSQL.

PDF отчёта по гаражам формируется QuestPDF с Unicode-шрифтом Lato, чтобы русские подписи не транслитерировались и таблица одинаково отображалась при локальной и Docker-установке. Проект использует Community-лицензию QuestPDF как приложение некоммерческого гаражно-строительного кооператива; при изменении юридического статуса или условий использования соответствие лицензии нужно проверить до выпуска.

## Frontend

- `src/features` — экраны по функциональным областям.
- `src/services` — API-клиенты и DTO.
- `src/shared` — общие формы, пагинация, форматирование, доступность и навигация.
- `App.tsx` — тонкая точка композиции.

Тяжёлые экраны загружаются отдельными чанками. Таблицы используют общую пагинацию, формы — единые контролы, а состояния загрузки, ошибки и пустые данные — общие компоненты.

Frontend разделять на feature-модули: `frontend/src/features/workspace/AppShell.tsx`, `frontend/src/features/workspace/Workspace.tsx`, `frontend/src/features/finance/FinancePanel.tsx`, `frontend/src/features/contractors/ContractorsPanel.tsx`, `frontend/src/features/tariffs/TariffsAndFeesPanel.tsx`, `frontend/src/features/dictionaries/DictionaryPanel.tsx`, `frontend/src/shared/DictionaryList.tsx`, `frontend/src/features/users/UserManagementPanel.tsx`, `frontend/src/features/reports/ReportPanel.tsx`, `frontend/src/features/audit/AuditPanel.tsx`, `frontend/src/shared/workspaceNavigation.ts`, `frontend/src/features/meterReadings/MeterReadingsPanel.tsx`, `frontend/src/shared/prototypeEditing.ts`, `frontend/src/features/import/ImportPanel.tsx`, `frontend/src/features/funds/FundsPanel.tsx`, `frontend/src/features/settings/PasswordPanel.tsx`, `frontend/src/features/auth/AuthGate.tsx`, `frontend/src/features/releases/ReleasePanel.tsx`; повторяемые контролы оставлять в shared UI.

## Авторизация

Backend является источником истины для прав. Frontend скрывает недоступные разделы только для удобства, но не заменяет серверную проверку.

Основные permissions:

- управление пользователями и ролями;
- чтение и изменение справочников;
- чтение и изменение финансовых операций;
- запуск импорта;
- чтение отчётов;
- чтение истории изменений;
- управление разделом «Что нового».

## Деньги и даты

- Денежные значения хранятся как `decimal`/`numeric`, без `double`.
- Деньги округляются до двух знаков, тарифные ставки — до четырёх.
- Учётный месяц нормализуется к первому дню месяца.
- Бизнес-даты передаются как `DateOnly` и не преобразуются через UTC.
- В интерфейсе даты отображаются как `ДД.ММ.ГГГГ`, месяцы — `ММ.ГГГГ`.

## Изменение схемы

Схема меняется только EF Core migrations. Production-код не создаёт таблицы через `EnsureCreated`, `EnsureDeleted` или произвольный DDL.

Каждая миграция должна:

1. иметь возрастающий timestamp;
2. применяться к чистой и существующей базе;
3. входить в idempotent SQL;
4. сохранять UTF-8 без BOM;
5. иметь PostgreSQL-интеграционную проверку, если использует provider-specific SQL.

## Добавление функции

1. Определить permissions и бизнес-правила.
2. Добавить или изменить domain/application API.
3. Реализовать DTO и контроллер.
4. Реализовать API-клиент и feature UI.
5. Покрыть success, empty, invalid, forbidden, failure и stale/cancelled сценарии.
6. Проверить PostgreSQL-запросы, сборку, lint, bundle и кодировку.
7. Обновить тематический документ и «Что нового», если изменение видно пользователю.

## API

Актуальный список маршрутов определяется контроллерами и OpenAPI backend. Не копируйте полный перечень endpoints в README: при изменении маршрута обновляйте DTO, клиент, тесты и OpenAPI одновременно.

Публичными остаются `GET /health`, первичное создание администратора и вход. Рабочие маршруты требуют JWT и соответствующего permission.
