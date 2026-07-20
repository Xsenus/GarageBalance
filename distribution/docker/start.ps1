[CmdletBinding()]
param([switch]$NoBrowser)

. (Join-Path $PSScriptRoot "GarageBalance.Common.ps1")

try {
    Set-Location $PSScriptRoot
    Write-GarageBalanceStep "Проверяем Docker Desktop"
    Assert-DockerReady

    Write-GarageBalanceStep "Подготавливаем GarageBalance"
    Initialize-GarageBalanceEnvironment
    $version = Set-GarageBalanceBundleVersion
    Import-GarageBalanceImages -Version $version

    Write-GarageBalanceStep "Запускаем GarageBalance $version"
    Invoke-GarageBalanceCompose -Arguments @("config", "--quiet")
    Invoke-GarageBalanceCompose -Arguments @("up", "-d", "--remove-orphans")
    $applicationUrl = Wait-GarageBalanceHealth

    Write-Host ""
    Write-Host "GarageBalance $version успешно запущен: $applicationUrl" -ForegroundColor Green
    if (-not $NoBrowser) {
        Start-Process $applicationUrl
    }
}
catch {
    Write-Host ""
    Write-Host "Не удалось запустить GarageBalance." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "Запустите diagnostics.cmd, если ошибка повторяется."
    exit 1
}
