# GarageBalance

GarageBalance — веб-приложение для финансового учёта гаражно-строительного кооператива. Система ведёт гаражи и владельцев, поставщиков и персонал, тарифы и сборы, начисления, платежи, показания счётчиков, фонды, отчёты, импорт из Access и историю изменений.

## Содержание

- [Быстрый запуск через Docker](#быстрый-запуск-через-docker)
- [Локальная разработка](#локальная-разработка)
- [Сборка и проверки](#сборка-и-проверки)
- [Миграции базы данных](#миграции-базы-данных)
- [Структура проекта](#структура-проекта)
- [Документация](#документация)

## Технологии

- Backend: .NET 10, ASP.NET Core, Entity Framework Core.
- Frontend: React 19, TypeScript 6, Vite 8.
- Database: PostgreSQL 17.
- Deployment: Docker Compose или systemd/nginx на VPS.
- Tests: xUnit, Vitest, Testing Library.

## Быстрый запуск через Docker

### Требования

- Git.
- Docker Desktop или Docker Engine с Compose.
- Свободные локальные порты `5173`, `5080` и `5432`.

### 1. Подготовьте конфигурацию

```powershell
git clone https://github.com/Xsenus/GarageBalance.git
Set-Location .\GarageBalance
Copy-Item .env.example .env
```

Откройте `.env` и обязательно замените:

- `POSTGRES_PASSWORD` — пароль PostgreSQL;
- `JWT_SIGNING_KEY` — случайный секрет длиной не менее 32 байт.

Файл `.env` содержит секреты и не должен попадать в Git.

### 2. Запустите приложение

```powershell
docker compose config
docker compose up --build -d
docker compose ps
```

После успешного запуска:

- интерфейс: <http://127.0.0.1:5173>;
- проверка API: <http://127.0.0.1:5080/health>.

При первом открытии создайте первого администратора, затем войдите под его учётной записью.

### 3. Остановка и повторный запуск

```powershell
docker compose stop
docker compose start
```

Команда `docker compose down -v` удаляет постоянные тома с базой и ключами защиты. Не используйте её для рабочей установки.

Полная инструкция по установке, обновлению и сохранности данных: [docs/docker-install-update-guide.md](docs/docker-install-update-guide.md).

## Локальная разработка

### Требования

- .NET SDK 10.
- Node.js 24 или новее и npm.
- PostgreSQL 17.
- EF Core CLI из локального manifest проекта.

### 1. Backend и база данных

В корне репозитория:

```powershell
dotnet tool restore
dotnet restore .\GarageBalance.slnx
dotnet user-secrets set "Jwt:SigningKey" "REPLACE_WITH_AT_LEAST_32_BYTES_SECRET" --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=127.0.0.1;Port=5432;Database=garagebalance;Username=garagebalance;Password=REPLACE_WITH_SECRET" --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
dotnet user-secrets set "DataProtection:KeysPath" "C:\GarageBalance\Config\DataProtectionKeys" --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
dotnet tool run dotnet-ef database update --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj --startup-project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
dotnet run --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
```

API по умолчанию доступен на адресе из launch settings. Проверить его можно запросом `GET /health`.

### 2. Frontend

В отдельном терминале:

```powershell
Set-Location .\frontend
npm ci
$env:VITE_API_BASE_URL = "http://127.0.0.1:5080"
npm run dev
```

Vite покажет адрес интерфейса, обычно <http://127.0.0.1:5173>.

## Сборка и проверки

### Backend

```powershell
dotnet build .\GarageBalance.slnx --configuration Release --no-restore
dotnet test .\GarageBalance.slnx --configuration Release --no-restore
dotnet format .\GarageBalance.slnx --no-restore --verify-no-changes
```

### Frontend

```powershell
Set-Location .\frontend
npm ci
npm run test:coverage
npm run lint
npm run build
npm run check:bundle
```

### Общие проверки перед коммитом

```powershell
.\infrastructure\scripts\verify-package-privacy.ps1
git diff --check
```

Подробное описание тестовых уровней и быстрых команд: [docs/testing-guide.md](docs/testing-guide.md).

## Миграции базы данных

Схема PostgreSQL меняется только через EF Core migrations в `backend/GarageBalance.Api/Infrastructure/Data/Migrations`.

Создание миграции:

```powershell
dotnet tool run dotnet-ef migrations add MigrationName --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj --startup-project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
```

Формирование idempotent SQL для публикации:

```powershell
.\infrastructure\scripts\generate-migration-script.ps1
```

Порядок проверки чистой и существующей базы: [docs/migration-verification-checklist.md](docs/migration-verification-checklist.md).

## Структура проекта

```text
backend/
  GarageBalance.Api/          ASP.NET Core API и миграции
  GarageBalance.Api.Tests/    backend-тесты
frontend/                     React-приложение
infrastructure/scripts/       backup, restore, deploy и проверки
docs/                         актуальная тематическая документация
.github/workflows/            GitHub Actions
docker-compose.yml            локальная контейнерная установка
```

Backend разделён на HTTP-контроллеры, application-сервисы, доменную логику и инфраструктуру. Frontend разделён на feature-модули и общие компоненты. Подробнее: [docs/development-guide.md](docs/development-guide.md).

## Документация

Полный индекс находится в [docs/README.md](docs/README.md).

Основные документы:

- [Руководство пользователя](docs/user-guide.md)
- [Руководство администратора](docs/admin-operations-guide.md)
- [Разработка и архитектура](docs/development-guide.md)
- [Развёртывание на VPS](docs/vps-deployment-checklist.md)
- [Backup и восстановление](docs/postgres-backup-restore.md)
- [Диагностика проблем](docs/troubleshooting-guide.md)
- [Защита данных](docs/security-data-protection.md)

## Безопасность

Не добавляйте в Git реальные `.env`, `appsettings.Local.json`, базы Access, дампы PostgreSQL, backup-файлы, приватные импорты и диагностические архивы. Секреты задаются через environment variables, .NET user secrets или GitHub/VPS secrets.

Перед публикацией выполняйте:

```powershell
.\infrastructure\scripts\verify-package-privacy.ps1
```

## Публикация

Push в `master` запускает `.github/workflows/deploy-staging.yml`: workflow проверяет проект, собирает backend и frontend, создаёт idempotent SQL, делает backup базы, применяет миграции и публикует релиз на staging VPS.

Публикация выполняется только после успешных локальных проверок и явного разрешения на push.
