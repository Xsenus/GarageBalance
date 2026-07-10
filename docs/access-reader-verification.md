# Access Reader Verification

Этот checklist фиксирует, как безопасно проверить готовность фактического чтения старой Access-БД перед переносом данных в PostgreSQL.

## Цель

- подтвердить рабочий способ чтения `.accdb` или согласованную конвертацию;
- не изменять исходную Access-БД;
- не копировать реальные персональные, финансовые и импортные данные в Git, тесты, roadmap или release notes;
- получить только техническое доказательство: provider, путь к приватной копии, количество таблиц/строк и результат smoke-read без содержимого строк.

## Текущий локальный статус

- В окружении проверки доступны только 32-bit ODBC-драйверы для старого `.mdb`.
- ACE/DAO provider для `.accdb` не зарегистрирован.
- `Access.Application` COM сам по себе не считается переносимым backend-reader для ASP.NET Core service.
- Реальный `.accdb/.mdb` файл не должен храниться в репозитории; проверочная копия должна лежать в приватной папке, например `C:\GarageBalance\Imports`.

## Условия разблокировки roadmap-пункта

- [ ] Установлен и проверен подходящий ACE OLE DB/ODBC provider для `.accdb`, Microsoft Access runtime с согласованным способом чтения или утвержденная конвертация.
- [ ] Создана приватная копия исходной Access-БД; оригинал не изменяется.
- [ ] Smoke-read открывает копию и возвращает список таблиц без содержимого персональных/финансовых строк.
- [ ] Зафиксированы только безопасные технические итоги: provider, количество таблиц, счетчики строк по ключевым таблицам и дата проверки.
- [ ] Проверка выполнена на той же разрядности процесса, в которой будет работать backend/import worker.
- [ ] После проверки приватные файлы остаются вне Git и вне публичных артефактов.

## Команды диагностики Windows

```powershell
Get-OdbcDriver | Where-Object { $_.Name -match 'Access|ACE|Microsoft Access|MDB|ACCDB' } |
    Select-Object Name,Platform,Attribute

$types = @('Access.Application','DAO.DBEngine.120','DAO.DBEngine.160','ADOX.Catalog')
foreach ($type in $types) {
    try {
        $obj = New-Object -ComObject $type -ErrorAction Stop
        "${type}=available"
        [void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($obj)
    } catch {
        "${type}=unavailable"
    }
}
```

## Что нельзя делать

- Не коммитить `.accdb`, `.mdb`, exports, screenshots with personal data, dumps или raw import folders.
- Не переносить строки из исходной БД в тестовые fixtures без отдельного решения пользователя.
- Не отмечать roadmap-пункт `[x]`, пока нет live smoke-read на приватной копии и согласованного способа чтения.
