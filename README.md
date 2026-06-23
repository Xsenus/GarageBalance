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

- Публичные endpoints: `GET /health`, `POST /api/auth/bootstrap-admin`, `POST /api/auth/login`; остальные рабочие endpoints требуют JWT и проверку прав через policies.
- `GET /health` - проверка, что API отвечает.
- `POST /api/auth/bootstrap-admin` - создание первого администратора.
- `POST /api/auth/login` - вход по email и паролю; после 5 неуспешных попыток за 15 минут возвращает `429 Too Many Requests`.
- `GET /api/auth/me` - текущий пользователь.
- `GET /api/users/roles` - доступные системные роли.
- `GET/POST /api/users` - список и создание пользователей.
- `PUT /api/users/{id}` - изменение пользователя, ролей, активности и пароля.
- `GET/POST /api/dictionaries/owners`, `DELETE /api/dictionaries/owners/{id}` - владельцы и архивирование владельца.
- `GET/POST /api/dictionaries/garages`, `DELETE /api/dictionaries/garages/{id}` - гаражи и архивирование гаража.
- `GET/POST /api/dictionaries/supplier-groups`, `DELETE /api/dictionaries/supplier-groups/{id}` - группы поставщиков и архивирование группы.
- `GET/POST /api/dictionaries/suppliers`, `DELETE /api/dictionaries/suppliers/{id}` - поставщики и архивирование поставщика.
- `GET/POST /api/dictionaries/income-types`, `DELETE /api/dictionaries/income-types/{id}` - виды поступлений и архивирование вида.
- `GET/POST /api/dictionaries/expense-types`, `DELETE /api/dictionaries/expense-types/{id}` - виды выплат и архивирование вида.
- `GET/POST /api/dictionaries/tariffs`, `DELETE /api/dictionaries/tariffs/{id}` - тарифы с датой действия и архивирование тарифа.
- `GET /api/finance/operations` - последние финансовые операции с фильтрами.
- `GET/POST /api/finance/accruals` - начисления по гаражам.
- `GET/POST /api/finance/supplier-accruals` - начисления поставщикам для сверки обязательств и выплат.
- `POST /api/finance/accruals/generate-regular` - создание регулярных начислений за месяц по выбранному тарифу.
- `GET/POST /api/finance/meter-readings` - показания счетчиков воды и электричества.
- `GET /api/finance/summary` - итоги поступлений, выплат и баланса.
- `POST /api/finance/income` - внесение поступления по гаражу.
- `POST /api/finance/expense` - внесение выплаты поставщику.
- `GET /api/import/access/runs` - журнал dry-run проверок Access-БД.
- `GET /api/import/access/runs/{id}/report` - скачивание JSON-отчета конкретного dry-run импорта.
- `POST /api/import/access/dry-run` - загрузка `.accdb`/`.mdb` и первичная проверка перед импортом.
- `GET /api/audit/events` - audit-журнал действий пользователей и системных операций.
- `GET /api/reports/consolidated` - консолидированный отчет за период с итогами по месяцам и гаражам.
- `GET /api/reports/consolidated/export/xlsx` - XLSX-выгрузка консолидированного отчета за период.
- `GET /api/reports/consolidated/export/pdf` - PDF-выгрузка консолидированного отчета за период.
- `GET /api/reports/income` - отчет по поступлениям: начисления, оплаты, фильтры по датам, поиску, гаражам, владельцам и видам поступлений.
- `GET /api/reports/income/export/xlsx` - XLSX-выгрузка отчета по поступлениям с теми же фильтрами, что экранный отчет.
- `GET /api/reports/income/export/pdf` - PDF-выгрузка отчета по поступлениям с теми же фильтрами, что экранный отчет.
- `GET /api/reports/expense` - отчет по выплатам: поставщики, виды выплат, документы, период и режим строк.
- `GET /api/reports/expense/export/xlsx` - XLSX-выгрузка отчета по выплатам с теми же фильтрами, что экранный отчет.
- `GET /api/reports/expense/export/pdf` - PDF-выгрузка отчета по выплатам с теми же фильтрами, что экранный отчет.
- `GET /api/app-releases` - история обновлений для раздела "Что нового" после входа в систему.

Стартовый баланс гаража учитывается как начальная задолженность в отчетах по поступлениям и сводном отчете; стартовый баланс поставщика учитывается как начальное обязательство в отчете по выплатам. Для гаражей добавлена migration `GarageStartingBalance`.

Для показаний электричества система сохраняет предупреждение, если предыдущее показание отсутствует или относится не к предыдущему месяцу. Предупреждение не блокирует внесение показания, но помогает оператору проверить пропущенный период до начислений.

В платежном разделе последние начисления по гаражам и поставщикам открывают разбивку: объект учета, вид, источник, сумма, документ поставщика и комментарий.

Для поступлений владельцев последние платежи показывают задолженность гаража до и после оплаты. Расчет строится от стартового баланса гаража, начислений до учетного месяца включительно и предыдущих поступлений по этому гаражу.

Для выплат поставщикам последние платежи показывают обязательство до и после выплаты. Расчет строится от стартового баланса поставщика, начислений поставщику до учетного месяца включительно и предыдущих выплат этому поставщику.

Docker-заготовка:

```powershell
copy .env.example .env
docker compose up --build
```

## Правила проекта

Перед разработкой читать `AGENTS.md` и актуальный roadmap в `docs/`. Все пользовательские изменения должны отражаться в `backend/GarageBalance.Api/AppReleases/releases.json`, если они видны пользователю или меняют правила работы.

Главный roadmap: `docs/project-roadmap.md`.
