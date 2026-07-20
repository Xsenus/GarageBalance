[CmdletBinding()]
param([string]$Reason = "manual")

. (Join-Path $PSScriptRoot "GarageBalance.Common.ps1")

try {
    Set-Location $PSScriptRoot
    Assert-DockerReady
    $settings = Get-GarageBalanceEnvironment
    $database = $settings["POSTGRES_DB"]
    $databaseUser = $settings["POSTGRES_USER"]
    if ($database -notmatch "^[A-Za-z0-9_-]+$" -or $databaseUser -notmatch "^[A-Za-z0-9_-]+$") {
        throw "POSTGRES_DB или POSTGRES_USER содержит недопустимые символы."
    }

    $safeReason = ($Reason -replace "[^A-Za-z0-9_-]", "_").Trim("_")
    if ([string]::IsNullOrWhiteSpace($safeReason)) {
        $safeReason = "manual"
    }

    $fileName = "garagebalance_{0}_{1}.pgdump" -f $safeReason, (Get-Date -Format "yyyyMMdd_HHmmss")
    Write-GarageBalanceStep "Создаем резервную копию $fileName"
    Invoke-GarageBalanceCompose -Arguments @(
        "exec", "-T", "postgres",
        "pg_dump", "--format=custom", "--no-owner", "--no-privileges",
        "-U", $databaseUser, "-d", $database, "-f", "/backups/$fileName"
    )
    Invoke-GarageBalanceComposeQuiet -Arguments @(
        "exec", "-T", "postgres",
        "pg_restore", "--list", "/backups/$fileName"
    )

    $backupHostPath = $settings["BACKUP_HOST_PATH"]
    if ([string]::IsNullOrWhiteSpace($backupHostPath)) {
        $backupHostPath = ".\backups"
    }
    if (-not [System.IO.Path]::IsPathRooted($backupHostPath)) {
        $backupHostPath = Join-Path $PSScriptRoot $backupHostPath
    }
    $backupFile = Join-Path ([System.IO.Path]::GetFullPath($backupHostPath)) $fileName
    if (-not (Test-Path -LiteralPath $backupFile -PathType Leaf) -or (Get-Item -LiteralPath $backupFile).Length -le 0) {
        throw "PostgreSQL сообщил об успехе, но готовый backup-файл не найден или пуст."
    }

    Write-Host "Резервная копия создана и проверена: backups\$fileName" -ForegroundColor Green
}
catch {
    Write-Host "Не удалось создать или проверить резервную копию." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
