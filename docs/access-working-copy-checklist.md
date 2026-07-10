# Access Working Copy Checklist

Этот checklist нужен перед любыми экспериментами со старой Access-БД.

## Правило

Оригинальный `.accdb` или `.mdb` файл не изменяется. Все проверки, repair/compact, конвертация, dry-run и будущий фактический импорт выполняются только на рабочей копии в приватной папке вне Git.

## Где хранить копию

- Рекомендуемый локальный путь: `C:\GarageBalance\Imports`.
- Допустимые repo-adjacent приватные папки, если они нужны только локально: `private-imports/`, `imports/private/`, `imports/raw/`.
- Эти папки и файлы `.accdb`/`.mdb` запрещены для Git через `.gitignore` и privacy-check.

## Как подготовить копию

- [ ] Получить исходный файл от заказчика в приватный каталог, не в репозиторий.
- [ ] Создать рабочую копию с датой в имени, например `gsk-access-working-YYYYMMDD.accdb`.
- [ ] Считать SHA-256 оригинала и рабочей копии.
- [ ] Сверить размер и checksum: после копирования они должны совпадать.
- [ ] Сделать копию read-only или сохранить отдельный untouched original backup.
- [ ] Записать в рабочий журнал только безопасные metadata: имя без персональных деталей, размер, SHA-256, дата и место хранения.
- [ ] Проверить `git status --short` и `infrastructure/scripts/verify-package-privacy.ps1` перед любым commit.

## Пример команд PowerShell

```powershell
$source = "D:\Private\GarageBalance\original.accdb"
$targetDir = "C:\GarageBalance\Imports"
$target = Join-Path $targetDir ("gsk-access-working-{0}.accdb" -f (Get-Date -Format "yyyyMMdd"))

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
Copy-Item -LiteralPath $source -Destination $target -Force

Get-FileHash -Algorithm SHA256 -LiteralPath $source
Get-FileHash -Algorithm SHA256 -LiteralPath $target
Get-Item -LiteralPath $source, $target | Select-Object FullName, Length, LastWriteTime
```

## Когда roadmap-пункт можно закрыть

- [ ] Рабочая копия реально создана в приватной папке.
- [ ] Оригинал не изменялся.
- [ ] SHA-256/размер оригинала и копии сверены.
- [ ] Privacy-check подтверждает, что Access-файлы и приватные папки не попадут в Git.
- [ ] В roadmap history записаны только безопасные технические итоги без персональных, финансовых и импортных строк.
