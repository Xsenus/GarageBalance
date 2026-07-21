GARAGEBALANCE — УСТАНОВКА ДЛЯ WINDOWS

Требование: установленный и запущенный Docker Desktop в режиме Linux containers.

ПЕРВАЯ УСТАНОВКА
1. Установите и запустите Docker Desktop в режиме Linux containers.
2. Полностью распакуйте ZIP в постоянную папку, например C:\GarageBalance.
3. Запустите start.cmd.
4. Дождитесь сообщения об успешном запуске.
5. Браузер откроет http://127.0.0.1:5173.
6. При первом открытии создайте администратора.

При первом запуске нужен интернет: Docker загрузит официальный PostgreSQL image.
Git, .NET SDK, Node.js и отдельная установка PostgreSQL не нужны.

ДРУГОЙ ПОРТ
1. После первого запуска выполните stop.cmd.
2. Откройте .env в Блокноте.
3. Для порта 8080 задайте FRONTEND_PORT=8080 и FRONTEND_ORIGIN=http://127.0.0.1:8080.
4. Сохраните .env и запустите start.cmd.

ДОСТУП С ДРУГИХ ПК ЛОКАЛЬНОЙ СЕТИ
1. Узнайте IPv4 основного компьютера командой ipconfig, например 192.168.1.50.
2. В .env задайте FRONTEND_BIND_ADDRESS=0.0.0.0, FRONTEND_PORT=8080 и FRONTEND_ORIGIN=http://192.168.1.50:8080.
3. Оставьте API_BIND_ADDRESS=127.0.0.1 и POSTGRES_BIND_ADDRESS=127.0.0.1.
4. Разрешите TCP-порт 8080 в Windows Firewall только для Private/LocalSubnet.
5. Запустите start.cmd и откройте http://192.168.1.50:8080 с другого ПК.
6. Не пробрасывайте порт на роутере для доступа из интернета без HTTPS или VPN.

Полная инструкция по портам, локальной сети, firewall и backup:
https://github.com/Xsenus/GarageBalance/blob/master/docs/docker-windows-lan-guide.md

ОБНОВЛЕНИЕ
1. Не удаляйте старую папку, .env, backups и Docker volumes.
2. Распакуйте новый ZIP поверх существующей папки с заменой файлов.
3. Запустите update.cmd.
4. Скрипт сначала создаст и проверит backup, затем загрузит новые images.

КОМАНДЫ
- start.cmd — первая установка или повторный запуск текущей версии.
- update.cmd — безопасное обновление из нового ZIP.
- backup.cmd — ручная проверенная копия PostgreSQL.
- diagnostics.cmd — технический отчет без .env и секретов.
- stop.cmd — остановка без удаления данных.

ВАЖНО
- Не удаляйте файл .env, папку backups и Docker volumes.
- Не выполняйте docker compose down -v: эта команда удаляет базу и ключи защиты.
- Не отправляйте .env, pgdump и содержимое рабочей базы посторонним.
- backup.cmd не копирует .env и volume ключей защиты: храните их отдельные защищенные копии.
- Для переноса на другой компьютер нужны backup PostgreSQL и отдельная копия volume ключей защиты.
