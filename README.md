# GarageBalance

Веб-система финансового учета для гаражно-строительного кооператива.

## Стек

- Backend: C# / ASP.NET Core.
- Frontend: React + TypeScript + Vite.
- Database: PostgreSQL.
- Packaging: Docker Compose подготовлен заранее, финальная упаковка планируется ближе к сдаче.

## Структура

- `backend/GarageBalance.Api` - API, конфигурация, будущая бизнес-логика и `AppReleases`.
- `backend/GarageBalance.Api.Tests` - тесты backend.
- `frontend` - React-приложение.
- `docs` - roadmap, анализ исходных материалов, решения и история работ.
- `docker-compose.yml` - локальный запуск Postgres/API/frontend через Docker.

## Локальная разработка

Backend:

```powershell
dotnet tool restore
dotnet restore .\GarageBalance.slnx
dotnet tool run dotnet-ef database update --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj --startup-project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
dotnet run --project .\backend\GarageBalance.Api\GarageBalance.Api.csproj
```

Frontend:

```powershell
cd .\frontend
npm install
npm run dev
```

Первые рабочие API:

- `GET /health` - проверка, что API отвечает.
- `POST /api/auth/bootstrap-admin` - создание первого администратора.
- `POST /api/auth/login` - вход по email и паролю.
- `GET /api/auth/me` - текущий пользователь.
- `GET /api/users/roles` - доступные системные роли.
- `GET/POST /api/users` - список и создание пользователей.
- `PUT /api/users/{id}` - изменение пользователя, ролей, активности и пароля.
- `GET/POST /api/dictionaries/owners` - владельцы.
- `GET/POST /api/dictionaries/garages` - гаражи.
- `GET/POST /api/dictionaries/supplier-groups` - группы поставщиков.
- `GET/POST /api/dictionaries/suppliers` - поставщики.
- `GET/POST /api/dictionaries/income-types` - виды поступлений.
- `GET/POST /api/dictionaries/expense-types` - виды выплат.
- `GET/POST /api/dictionaries/tariffs` - тарифы с датой действия.
- `GET /api/finance/operations` - последние финансовые операции с фильтрами.
- `GET/POST /api/finance/accruals` - начисления по гаражам.
- `GET /api/finance/summary` - итоги поступлений, выплат и баланса.
- `POST /api/finance/income` - внесение поступления по гаражу.
- `POST /api/finance/expense` - внесение выплаты поставщику.

Docker-заготовка:

```powershell
copy .env.example .env
docker compose up --build
```

## Правила проекта

Перед разработкой читать `AGENTS.md` и актуальный roadmap в `docs/`. Все пользовательские изменения должны отражаться в `backend/GarageBalance.Api/AppReleases/releases.json`, если они видны пользователю или меняют правила работы.

Главный roadmap: `docs/project-roadmap.md`.
