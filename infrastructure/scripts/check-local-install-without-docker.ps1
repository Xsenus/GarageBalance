param(
    [string]$ApiProject = "backend/GarageBalance.Api/GarageBalance.Api.csproj",
    [string]$FrontendDirectory = "frontend",
    [string]$Database = "garagebalance_local",
    [string]$HostName = "127.0.0.1",
    [int]$Port = 5432,
    [string]$Username = "garagebalance_local",
    [switch]$RequirePostgres
)

$ErrorActionPreference = "Stop"

Write-Output "localInstallWithoutDocker=True"
Write-Output "requirePostgres=$RequirePostgres"

$dotnet = Get-Command dotnet -ErrorAction Stop
& $dotnet.Source --version

if (-not (Test-Path -LiteralPath $ApiProject)) {
    throw "API project was not found: $ApiProject"
}

& $dotnet.Source publish $ApiProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Write-Output "apiPublish=True"

$frontendPath = Resolve-Path -LiteralPath $FrontendDirectory -ErrorAction Stop
Push-Location $frontendPath.Path
try {
    $env:VITE_API_BASE_URL = "http://127.0.0.1:5080"
    npm run build
    if ($LASTEXITCODE -ne 0) {
        throw "npm run build failed with exit code $LASTEXITCODE."
    }

    Write-Output "frontendBuild=True"
}
finally {
    Pop-Location
}

$postgresCheckScript = "infrastructure/scripts/check-local-postgres.ps1"
if (-not (Test-Path -LiteralPath $postgresCheckScript)) {
    throw "PostgreSQL preflight script was not found: $postgresCheckScript"
}

$tcpAvailable = Test-NetConnection -ComputerName $HostName -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue
Write-Output "postgresTcp=$tcpAvailable"
$psql = Get-Command psql -ErrorAction SilentlyContinue
Write-Output ("psql=" + ($null -ne $psql))

if ($RequirePostgres) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $postgresCheckScript `
        -Database $Database `
        -HostName $HostName `
        -Port $Port `
        -Username $Username `
        -RequirePsql

    if ($LASTEXITCODE -ne 0) {
        throw "Local PostgreSQL preflight failed with exit code $LASTEXITCODE."
    }

    Write-Output "localPostgresPreflight=Checked"
}
elseif (-not $tcpAvailable -or $null -eq $psql) {
    Write-Output "localPostgresPreflight=Blocked"
}
else {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $postgresCheckScript `
        -Database $Database `
        -HostName $HostName `
        -Port $Port `
        -Username $Username

    if ($LASTEXITCODE -ne 0) {
        Write-Output "localPostgresPreflight=Blocked"
    }
    else {
        Write-Output "localPostgresPreflight=Checked"
    }
}

& $dotnet.Source tool run dotnet-ef migrations script `
    --project $ApiProject `
    --startup-project $ApiProject `
    --context GarageBalanceDbContext `
    --idempotent `
    --no-build `
    --output artifacts/local-install-migrations.sql

if ($LASTEXITCODE -ne 0) {
    throw "EF idempotent migration script generation failed with exit code $LASTEXITCODE."
}

Write-Output "migrationScript=artifacts/local-install-migrations.sql"
