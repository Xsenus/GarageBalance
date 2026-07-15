param(
    [Parameter(Mandatory = $true)]
    [string]$ResultsDirectory,
    [double]$MinimumLineRate = 85,
    [double]$MinimumBranchRate = 70
)

$ErrorActionPreference = 'Stop'

$coverageFile = Get-ChildItem -LiteralPath $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml' |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if (-not $coverageFile) {
    throw "Файл coverage.cobertura.xml не найден в $ResultsDirectory."
}

[xml]$coverage = Get-Content -LiteralPath $coverageFile.FullName -Raw
$lineRate = [math]::Round(([double]$coverage.coverage.'line-rate') * 100, 2)
$branchRate = [math]::Round(([double]$coverage.coverage.'branch-rate') * 100, 2)

Write-Host "Backend coverage: lines $lineRate% (minimum $MinimumLineRate%), branches $branchRate% (minimum $MinimumBranchRate%)."

if ($lineRate -lt $MinimumLineRate) {
    throw "Покрытие строк backend $lineRate% ниже обязательного порога $MinimumLineRate%."
}

if ($branchRate -lt $MinimumBranchRate) {
    throw "Покрытие ветвей backend $branchRate% ниже обязательного порога $MinimumBranchRate%."
}
