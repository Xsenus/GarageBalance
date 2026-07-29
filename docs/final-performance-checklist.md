# Производительность и стабильность

## Цели

- Рабочие разделы не блокируют интерфейс длительными запросами.
- Большие списки используют серверные фильтры, сортировку и пагинацию.
- Финансовые итоги вычисляются в PostgreSQL без загрузки всей истории в память.
- Независимые блоки экрана загружаются и показывают ошибки независимо.
- Отмена или устаревший ответ не перезаписывает более новые данные.

## Backend

- До `ToListAsync` применяются ограничение, фильтр и сортировка.
- Отчётные итоги и количество строк возвращаются агрегированным запросом.
- Связанные названия проецируются в SQL без N+1.
- Поиск по подстроке использует PostgreSQL GIN trigram indexes.
- Финансовые запросы проверяются через `EXPLAIN (ANALYZE, BUFFERS)` на реалистичном объёме.
- Производительные контракты закрепляются в `BackendPerformanceGuardTests` и PostgreSQL integration tests.

## Frontend

- Feature-разделы загружаются отдельными чанками.
- Поисковые запросы используют debounce и отмену устаревшего запроса.
- Большие таблицы не рендерят всю выборку.
- Skeleton соответствует форме итогового содержимого.
- Падение одного справочника или виджета не скрывает уже загруженные данные.

## Автоматические проверки

```powershell
dotnet test GarageBalance.slnx --no-restore --configuration Release
Set-Location frontend
npm run test:coverage
npm run lint
npm run build
npm run check:bundle
```

Текущие лимиты production bundle:

- основной JavaScript gzip — не более `180 KiB`;
- основной CSS gzip — не более `40 KiB`;
- общий JavaScript/CSS gzip — не более `260 KiB`.

## Ранжирование HTTP-маршрутов

Timing-журнал nginx можно агрегировать без копирования исходных строк и без вывода IP,
request ID или query-параметров:

```powershell
Get-Content .\garagebalance-staging-timing.log -Raw |
  .\infrastructure\scripts\analyze-nginx-timing.ps1 -InputPath STDIN
```

Для автоматической обработки добавляется `-AsJson`, для отсечения единичных маршрутов —
`-MinimumCount 5`. Результат содержит по разделам и нормализованным маршрутам `count`,
`p50`, `p95`, `max`, количество `4xx`, `5xx` и общий error rate. Числовые и UUID-сегменты
заменяются на `:id`, а query string удаляется до группировки. Ошибки внешних сканеров и
ожидаемые бизнес-ответы `4xx` анализируются отдельно от `5xx`; они не должны скрывать
серверные сбои и не включаются в пользовательский performance-SLA без проверки источника.

Воспроизводимый smoke/benchmark основных API запускается после получения временного
администраторского bearer token:

```powershell
$env:GARAGEBALANCE_BENCHMARK_TOKEN = "..."
.\infrastructure\scripts\benchmark-api.ps1 `
  -BaseUrl "https://sgk.blagodaty.ru" `
  -Iterations 20 `
  -WarmupIterations 2
Remove-Item Env:GARAGEBALANCE_BENCHMARK_TOKEN
```

Сценарии и фиксированные пороги хранятся в
`infrastructure/performance/api-smoke-scenarios.json`. Они покрывают health,
текущего пользователя, гаражи, финансы, показания, фонды, отчёт, импорт, аудит и
пользователей. Для одиночной проверки используется `-ScenarioName health`; для CI —
`-AsJson`. Команда загружает тело ответа, но выводит только размер и агрегаты,
никогда не печатает bearer token, query-данные ответа или содержимое финансовых строк.
Любое превышение p50/p95/error-rate завершает процесс ненулевым кодом.

## Проверка PostgreSQL

Перед релизом performance-sensitive изменения проверяются на локальной PostgreSQL или в изолированном CI/VPS-контуре:

1. применить все миграции;
2. загрузить безопасный синтетический объём;
3. выполнить целевой запрос с `EXPLAIN (ANALYZE, BUFFERS)`;
4. убедиться в использовании индексов и отсутствии полного повторного сканирования;
5. запустить соответствующие integration tests;
6. проверить сохранение итогов, порядка и границ страниц.

Для воспроизводимого объёма создаётся отдельная пустая БД с именем
`garagebalance_performance` или префиксом `garagebalance_performance_`. После этого
connection string передаётся только через переменную окружения:

```powershell
$env:GARAGEBALANCE_PERFORMANCE_CONNECTION = `
  "Host=127.0.0.1;Database=garagebalance_performance;Username=...;Password=..."
dotnet run --project .\backend\GarageBalance.PerformanceSeed\GarageBalance.PerformanceSeed.csproj `
  --configuration Release -- --garages 500 --months 60
```

Команда сама применяет EF Core migrations и создаёт фиксированный обезличенный набор:
500 владельцев и гаражей, по 60 месяцев начислений, платежей и показаний
электросчётчиков (`30 000` строк каждого исторического вида). Повторный запуск
идемпотентен. База с любым другим именем, включая staging/production, отклоняется до
подключения; тестовое исключение разрешается только автоматической интеграционной
проверкой через отдельный флаг окружения.

После реалистичной нагрузки выполнить read-only снимок обслуживания:

```powershell
$env:PGDATABASE = "garagebalance_staging"
.\infrastructure\scripts\check-postgres-health.ps1
```

Подключение задаётся стандартными переменными `PGHOST`, `PGPORT`, `PGUSER` и `PGPASSWORD`;
секреты в аргументы или отчёт не записываются. В результате проверяются cache hit, временные
файлы, deadlock, доля dead tuples, пороги autovacuum/analyze, долгие транзакции и ожидания
блокировок. Также снимок показывает `shared_buffers`, `effective_cache_size`, `work_mem`,
глобальный предел соединений и его фактическое использование по базам. Ручной `VACUUM`,
изменение памяти или порогов допускаются только после подтверждённого превышения, а не по
одному высокому проценту у маленькой таблицы. На общем PostgreSQL сначала учитывается сумма
пулов всех сервисов; глобальная настройка не меняется только под GarageBalance.

После накопления не менее суток статистики `pg_stat_statements` безопасный top SQL снимается
без текста запросов и без пользовательских параметров:

```powershell
psql -X --set ON_ERROR_STOP=1 --dbname garagebalance_staging `
  --file .\infrastructure\postgres\postgres-top-statements.sql
```

Снимок ранжирует `queryid` по total/mean/max execution time, calls, rows, shared/temp blocks.
Если расширение не установлено, команда сообщает об этом и не пытается читать отсутствующее
представление. На общем VPS включение расширения требует согласованного окна: необходимо
добавить `pg_stat_statements` в `shared_preload_libraries`, ограничить `pg_stat_statements.max`,
значением `5000`, оставить `track = top`, `track_planning = off`, `save = on`, перезапустить
общий кластер и только затем создать extension в `garagebalance_staging`. Перезапуск нельзя
выполнять как часть обычного deployment GarageBalance, поскольку тот же кластер обслуживает
стороннюю базу.

## Приёмка

- Поиск не зависает при быстром наборе и удалении текста.
- Переключение страниц не показывает данные предыдущего запроса.
- Открытие должников, отчётов, фондов, платежей и показаний завершается либо данными, либо понятной ошибкой.
- В консоли браузера отсутствуют необработанные ошибки.
- API не возвращает неограниченную финансовую историю.
- Оптимизация не меняет формулы, округление, права и audit.
