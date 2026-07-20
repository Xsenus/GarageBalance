[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot "GarageBalance.Common.ps1")

try {
    Set-Location $PSScriptRoot
    Assert-DockerReady
    Invoke-GarageBalanceCompose -Arguments @("stop")
    Write-Host "GarageBalance остановлен. База, backup и ключи защиты сохранены." -ForegroundColor Green
}
catch {
    Write-Host "Не удалось остановить GarageBalance." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
