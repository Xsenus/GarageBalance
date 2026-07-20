[CmdletBinding()]
param([switch]$NoBrowser)

. (Join-Path $PSScriptRoot "GarageBalance.Common.ps1")

try {
    Set-Location $PSScriptRoot
    Write-GarageBalanceStep "Проверяем Docker Desktop и текущую установку"
    Assert-DockerReady
    if (-not (Test-Path -LiteralPath $script:GarageBalanceEnvFile -PathType Leaf)) {
        throw "Текущая установка не найдена. Для первой установки используйте start.cmd."
    }
    if (-not (Test-Path -LiteralPath $script:GarageBalanceVersionFile -PathType Leaf)) {
        throw "В новом установочном архиве отсутствует release-version.txt. Скачайте архив повторно."
    }

    $version = ([System.IO.File]::ReadAllText($script:GarageBalanceVersionFile)).Trim()
    Write-GarageBalanceStep "Создаем обязательный backup перед обновлением до $version"
    $powershellExecutable = Join-Path $PSHOME "powershell.exe"
    $backupScript = Join-Path $PSScriptRoot "backup.ps1"
    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & $powershellExecutable -NoLogo -NoProfile -ExecutionPolicy Bypass -File $backupScript -Reason "pre_update_$version"
    $backupExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorPreference
    if ($backupExitCode -ne 0) {
        throw "Обновление остановлено, потому что резервная копия не была создана и проверена."
    }

    Write-GarageBalanceStep "Загружаем новую версию GarageBalance"
    $version = Set-GarageBalanceBundleVersion
    Import-GarageBalanceImages -Version $version
    Invoke-GarageBalanceCompose -Arguments @("config", "--quiet")
    Invoke-GarageBalanceCompose -Arguments @("up", "-d", "--remove-orphans")
    $applicationUrl = Wait-GarageBalanceHealth

    Write-Host "GarageBalance успешно обновлен до версии ${version}: $applicationUrl" -ForegroundColor Green
    if (-not $NoBrowser) {
        Start-Process $applicationUrl
    }
}
catch {
    Write-Host "Обновление GarageBalance не выполнено." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "Существующие данные и backup не удалялись. Запустите diagnostics.cmd."
    exit 1
}
