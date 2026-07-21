# Пользовательский Docker-дистрибутив GarageBalance

## Источники

- `docker-compose.yml`, backend/frontend Dockerfile и `frontend/nginx.conf`.
- `.github/workflows/deploy-staging.yml` и текущий VPS release-процесс.
- `docs/docker-install-update-guide.md`, `docs/postgres-backup-restore.md` и `docs/version-update-checklist.md`.
- Требование заказчика: конечный пользователь устанавливает только Docker Desktop, скачивает один архив GarageBalance и запускает установку без Git, SDK, Node.js и ручной настройки секретов.

## Предположения и решения

- Целевая пользовательская ОС первого дистрибутива — Windows 10/11 x64 с Docker Desktop и Linux containers.
- Release-архив содержит готовые backend, frontend и PostgreSQL image-архивы; после скачивания ZIP установка не обращается к registry или Docker Hub.
- Compose project всегда называется `garagebalance`, поэтому обновление из новой папки повторно подключает существующие volumes.
- Рабочая база PostgreSQL, ключи защиты, backup и логи не входят в release ZIP и переживают замену контейнеров; ZIP содержит только чистый PostgreSQL image без пользовательских данных.
- Версия фиксируется в release bundle и в `.env`; `latest` не используется как единственный источник истины.
- Отдельные application packages в GHCR не публикуются: единственный пользовательский артефакт — автономный ZIP с checksum.

## Этап 1. Формат дистрибутива

- [x] Добавить отдельный Compose без `build`, использующий versioned Docker images.
- [x] Добавить шаблон `.env` без реальных секретов.
- [x] Зафиксировать имя Compose project и постоянные volumes.
- [x] Добавить version-файл release bundle.

## Этап 2. Пользовательские команды Windows

- [x] Добавить `start.cmd` / `start.ps1` с проверкой Docker, генерацией секретов, загрузкой images и health-check.
- [x] Добавить `update.cmd` / `update.ps1` с обязательным backup и безопасной заменой контейнеров.
- [x] Добавить `backup.cmd` / `backup.ps1` с проверкой созданного `.pgdump`.
- [x] Добавить `diagnostics.cmd` / `diagnostics.ps1` без выгрузки `.env` и секретов.
- [x] Добавить безопасную остановку без удаления volumes.
- [x] Показывать понятные русские ошибки и не продолжать обновление после неуспешного backup/health-check.

## Этап 3. GitHub release pipeline

- [x] Добавить workflow для tag `v*` и ручной проверки.
- [x] Запускать полный backend/frontend quality gate перед публикацией.
- [x] Локально собирать versioned backend/frontend images без публикации в GHCR.
- [x] Сохранять API, frontend и PostgreSQL images в автономный установочный ZIP.
- [x] Добавлять SHA-256 checksum и GitHub Actions artifact.
- [x] Для tag создавать GitHub Release с ZIP и checksum.

## Этап 4. Тесты и документация

- [x] Добавить тесты release Compose, скриптов, workflow и отсутствия секретов.
- [x] Проверять синтаксис PowerShell-скриптов.
- [x] Проверять release Compose через локальный Docker Compose parser и повторять проверку в release workflow.
- [x] Обновить основной README на сценарий «Docker + один ZIP».
- [x] Обновить Docker guide: установка, обновление, backup, диагностика и rollback.
- [x] Добавить пользовательское описание в «Что нового».

## Definition of done

- [acceptance] На чистом Windows-компьютере с Docker Desktop установка выполняется из одного ZIP без Git/SDK/Node.js.
- [x] Первый запуск генерирует уникальные секреты и не печатает их в консоль или diagnostics.
- [x] Повторный запуск не меняет секреты и не удаляет данные.
- [x] Обновление прерывается, если backup не создан или не проверен.
- [x] После установки и обновления `/health` отвечает успешно.
- [x] Полные backend/frontend тесты, lint, build, bundle, formatting, privacy и migration gates проходят.
- [x] Docker images/ZIP публикуются только после успешного quality gate.

## Риски и открытые вопросы

- [acceptance] Нужна финальная проверка на чистой Windows 10/11 с запущенным Docker Desktop.
- [x] Первый GitHub Release опубликован, ZIP и SHA-256 скачаны и проверены.
- [decision] Поддержка Windows ARM64 может быть добавлена отдельным релизным вариантом после проверки спроса.

## История выполнения

- 2026-07-20: проверены текущие Compose/Dockerfile, документация, CI и публичность репозитория. Выбран автономный ZIP с готовыми application images и дополнительной публикацией в GHCR.
- 2026-07-20: добавлены release Compose, Windows-команды запуска, обновления, backup, диагностики и остановки, workflow сборки versioned images и автономного ZIP, SHA-256, автоматические контракты и пользовательская документация. PowerShell синтаксис, YAML, статические контракты и локальный `docker compose config` прошли; фактический запуск images остается приемкой на чистом Windows-компьютере с запущенным Docker Engine.
- 2026-07-20: реальный первый запуск из временного ZIP выявил несовместимость `pg_dump 16` с PostgreSQL 17: обязательный предмиграционный backup безопасно остановил API. Dockerfile переведен на PostgreSQL client 17, добавлен регрессионный контракт совпадения major-версий; требуется повторная сборка и smoke-test.
- 2026-07-20: повторно собраны production images и автономный пакет. Первый запуск, 65 миграций, frontend/API health 200, автоматические и ручные backups, `pg_restore --list`, update, diagnostics без секретов и безопасный stop прошли на Docker Engine 28.3.3; тестовые контейнеры и volumes удалены. Остается приемка ZIP на отдельном чистом Windows-компьютере и первая tag-публикация.
- 2026-07-21: тег `v0.758.0` опубликован, release workflow успешно создал versioned images, автономный ZIP и SHA-256. Добавлена постоянная пошаговая инструкция для Windows: смена портов, безопасный доступ из локальной сети, правило Windows Firewall только для `Private/LocalSubnet`, состав полного аварийного комплекта, обновление и диагностика. Приемка на отдельном чистом Windows-компьютере остается открытой.
- 2026-07-21: по решению владельца двойная публикация ZIP и GHCR заменена одним полностью автономным ZIP. В архив включен PostgreSQL 17 Alpine image, Compose запрещает сетевое скачивание всех трех образов, release workflow больше не публикует packages и не запрашивает package-write permission. Локальный offline smoke-test с предварительным удалением application images подтвердил импорт 3/3 образов, применение 65 миграций, создание резервных копий и frontend/API health 200; тестовые контейнеры и volumes удалены. Остается приемка на отдельном чистом Windows-компьютере.
