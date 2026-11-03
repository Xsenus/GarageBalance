# Изолированный тест координатора на VPS

Артефакты пересобраны из текущих source-файлов; полный список source/build SHA-256 находится в `build-evidence.sha256`. Перед запуском сверяйте все хеши, а после изменений пересобирайте и обновляйте свидетельство. Полный Linux integration fixture отдельно прошёл success, backup/restore и аварийное восстановление; он не заменяет этот изолированный тест либо проверку рабочих units.

Это тест временных объектов, а не очистка GarageBalance. Запускайте его только на согласованном VPS с компьютера Xsenus из PowerShell пользователя. Потребуются собственный SSH-доступ с правами root, известный и проверенный SSH host key, OpenSSH (`ssh` и `scp`), а на сервере — systemd, `flock`, `sha256sum`, `bash` и .NET Runtime 10. Ничего не отключайте в проверке host key. Если появится предупреждение об отсутствующем или изменившемся ключе, остановитесь и проверьте его отдельно. Пароль вводится только в обычном скрытом приглашении SSH, не в команды и не в чат.

Каждый блок ниже вводится в одно и то же окно PowerShell на Xsenus. После каждой команды SSH или SCP проверяйте, что `$LASTEXITCODE` равен 0; при иной ситуации сразу остановитесь и сообщите результат. Подключений будет несколько. Все пути и хеши ниже относятся к текущей локальной сборке; при изменении файлов инструкцию нужно перепроверить.

## 1. Проверка локальных файлов

```powershell
$fixtureBase = 'C:\Users\ilel\Documents\Codex\2026-10-30\task\GarageBalance\infrastructure\fixtures\maintenance-dotnet'
$bin = Join-Path $fixtureBase 'bin\Release\net10.0'
$items = @(
  @{ Path = (Join-Path $fixtureBase 'run-isolated-fixture.sh'); Hash = '31d0f3966dd891c6a6c6a06597e882129a090fe5e8b6e6357e18937280715142' },
  @{ Path = (Join-Path $bin 'MaintenanceFixture.dll'); Hash = '2367b30a27a9f311ae80a4111daf16bc643c30621e50bc7e0988f1b6ea0ac163' },
  @{ Path = (Join-Path $bin 'MaintenanceFixture.deps.json'); Hash = 'd14c9bfdea72b16a4c1e385def6d892b071adaf36a5a2b42f635d20ffc91025f' },
  @{ Path = (Join-Path $bin 'MaintenanceFixture.runtimeconfig.json'); Hash = 'c230a317a54dd960bcbeb5f347f52e18dc665a26f7efda2159fced9a5ac7e097' }
)
$ok = $true
foreach ($item in $items) {
  if (!(Test-Path -LiteralPath $item.Path -PathType Leaf)) { $ok = $false; Write-Host "НЕТ ФАЙЛА: $($item.Path)"; continue }
  $actual = (Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actual -ne $item.Hash) { $ok = $false; Write-Host "ХЕШ НЕ СОВПАЛ: $($item.Path)" }
}
if (!$ok) { throw 'Остановлено: файлы не соответствуют проверенному снимку.' }
Write-Host 'Локальные хеши совпадают.'
```

## 2. Проверка сервера и создание собственного каталога

```powershell
ssh -o StrictHostKeyChecking=yes root@31.192.110.221 'id -u; command -v systemd-run systemctl flock sha256sum bash base64; /usr/bin/dotnet --list-runtimes'
if ($LASTEXITCODE -ne 0) { throw 'Проверка сервера не выполнена.' }
```

Убедитесь, что `id -u` вывел `0`, команды перечислены и список содержит `Microsoft.NETCore.App 10.0.x`. Если чего-то нет, остановитесь; не устанавливайте компоненты самостоятельно.

```powershell
$id = [guid]::NewGuid().ToString('N').Substring(0,8)
$stage = "/run/gb-dotnet-stage.$id"
$driver = "/run/gb-dotnet-driver.$id.sh"
ssh -o StrictHostKeyChecking=yes root@31.192.110.221 "umask 077; mkdir -- $stage && (set -C; : > $driver)"
if ($LASTEXITCODE -ne 0) { throw 'Подготовка не завершена: остановитесь и сообщите оба пути.' }
Write-Host "Каталог: $stage; сценарий: $driver"
```

Запишите оба выведенных пути: они нужны для проверки и удаления только созданных объектов. `mkdir` откажет при существующем каталоге, а `set -C` не позволит перезаписать существующий файл сценария. Если вторая операция не удалась, созданный каталог может остаться; не повторяйте подготовку вслепую.

## 3. Передача файлов

```powershell
scp -o StrictHostKeyChecking=yes (Join-Path $bin 'MaintenanceFixture.dll') ("root@31.192.110.221:${stage}/MaintenanceFixture.dll")
if ($LASTEXITCODE -ne 0) { throw 'Передача DLL не завершена; остановитесь.' }
scp -o StrictHostKeyChecking=yes (Join-Path $bin 'MaintenanceFixture.deps.json') ("root@31.192.110.221:${stage}/MaintenanceFixture.deps.json")
if ($LASTEXITCODE -ne 0) { throw 'Передача deps не завершена; остановитесь.' }
scp -o StrictHostKeyChecking=yes (Join-Path $bin 'MaintenanceFixture.runtimeconfig.json') ("root@31.192.110.221:${stage}/MaintenanceFixture.runtimeconfig.json")
if ($LASTEXITCODE -ne 0) { throw 'Передача runtimeconfig не завершена; остановитесь.' }
scp -o StrictHostKeyChecking=yes (Join-Path $fixtureBase 'run-isolated-fixture.sh') ("root@31.192.110.221:$driver")
if ($LASTEXITCODE -ne 0) { throw 'Передача сценария не завершена; остановитесь.' }
```

## 4. Проверка хешей на сервере до запуска

```powershell
ssh -o StrictHostKeyChecking=yes root@31.192.110.221 "sha256sum -- $driver $stage/MaintenanceFixture.dll $stage/MaintenanceFixture.deps.json $stage/MaintenanceFixture.runtimeconfig.json"
if ($LASTEXITCODE -ne 0) { throw 'Серверные хеши не получены; остановитесь.' }
```

Сравните четыре строки по порядку с хешами в разделе 1: сценарий `31d0f396…`, DLL `2367b30a…`, deps `d14c9bfd…`, runtimeconfig `c230a317…`. Сравнивать нужно **все 64 знака**, а не только начало. При несовпадении остановитесь и не запускайте тест.

## 5. Один запуск теста

Только после успешных предыдущих проверок:

```powershell
ssh -o StrictHostKeyChecking=yes root@31.192.110.221 "bash $driver $stage 2367b30a27a9f311ae80a4111daf16bc643c30621e50bc7e0988f1b6ea0ac163 d14c9bfdea72b16a4c1e385def6d892b071adaf36a5a2b42f635d20ffc91025f c230a317a54dd960bcbeb5f347f52e18dc665a26f7efda2159fced9a5ac7e097"
$fixtureExit = $LASTEXITCODE
Write-Host "Код завершения: $fixtureExit"
```

Успех — строка `PASS: linked host/coordinator gates and synthetic process-crash phases` **и** код завершения `0`. Сценарий показывает `fixturePrefix` и `fixtureDirectory`, запускает только собственные временные службы и по окончании останавливает их и удаляет свой внутренний каталог. Три аварийных завершения тестовых процессов преднамеренны.

Если связь прервалась, код не получен, код отличен от 0, есть `fixtureUnitCleanupUncertain`, `fixtureDirectoryRetained` или `fixtureDirectoryCleanupFailed`, **не запускайте тест повторно и не удаляйте объекты вручную**. Передайте специалисту полный вывод без пароля, записанные пути, `fixturePrefix` и `fixtureDirectory`. Состояние нужно сначала проверить; сценарий ограничивает время жизни своих служб, но это не доказательство завершённой уборки.

## 6. Удаление загруженных файлов только после подтверждённого успеха

Если есть PASS и код 0, а сообщений о неопределённой уборке нет, удалите только четыре загруженных файла и созданный вами пустой каталог:

```powershell
ssh -o StrictHostKeyChecking=yes root@31.192.110.221 "rm -f -- $driver $stage/MaintenanceFixture.dll $stage/MaintenanceFixture.deps.json $stage/MaintenanceFixture.runtimeconfig.json && rmdir -- $stage"
if ($LASTEXITCODE -ne 0) { throw 'Удаление загруженных файлов не подтверждено; сообщите сохранённые пути.' }
```

Передайте специалисту вывод теста, код завершения и результат этого последнего шага. Тест не проверяет рабочую базу, настоящий deploy или работающую синхронизацию и не разрешает начинать очистку без остальных обязательных проверок.

Текущая сборка дополнительно проверяет реальное файловое усвоение подготовленного marker и журнал preparing → prepared → executing. После аварийного завершения журнал executing сохраняется. Это не подтверждает SQL COMMIT, rollback или рабочие пути запуска.
