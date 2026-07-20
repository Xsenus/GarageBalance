# Тестирование GarageBalance

## Быстрая проверка во время разработки

Frontend-тесты, связанные с изменёнными файлами:

```powershell
Set-Location .\frontend
npm run test:dev:related -- src/features/example/Example.tsx
```

Выбранный backend-тест:

```powershell
dotnet test .\GarageBalance.slnx --no-restore --filter "FullyQualifiedName~TestClassName"
```

## Полная локальная проверка

Backend:

```powershell
dotnet build .\GarageBalance.slnx --configuration Release --no-restore
dotnet test .\GarageBalance.slnx --configuration Release --no-restore
dotnet format .\GarageBalance.slnx --no-restore --verify-no-changes
```

Frontend:

```powershell
Set-Location .\frontend
npm run test:coverage
npm run lint
npm run build
npm run check:bundle
```

Общие проверки:

```powershell
.\infrastructure\scripts\verify-package-privacy.ps1
.\infrastructure\scripts\generate-migration-script.ps1
git diff --check
```

## Что покрывать

- Domain: расчёты, округления, периоды и пограничные значения.
- Application: транзакции, права и последовательность операций.
- Controllers: успешный ответ, validation, forbidden и not found.
- PostgreSQL: mappings, migrations, индексы и сложные запросы.
- Frontend: loading, empty, success, error, validation, permissions и клавиатура.
- API clients: URL, метод, параметры, сериализация и обработка ошибок.
- Reports: фильтры, сортировка, пагинация, итоги и экспорт.

## Полный и быстрый режим Vitest

- `npm run test:dev` — быстрый набор для разработки.
- `npm run test:dev:related` — тесты затронутых модулей.
- `npm run test:coverage` — полный CI-набор с покрытием.
- `npm run test:watch` — интерактивный режим без большого `App.test.tsx`.

Нельзя исправлять падение удалением проверки, увеличением timeout без причины или исключением бизнес-ветки из coverage.

## GitHub Actions

Workflow `Deploy staging` выполняет полный backend/frontend gate до упаковки релиза. Деплой не начинается, если падают тесты, форматирование, lint, build, privacy check или генерация SQL миграций.
