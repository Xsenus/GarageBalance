[CmdletBinding()]
param(
    [string]$Database = $(if ($env:POSTGRES_DB) { $env:POSTGRES_DB } else { "garagebalance_local" }),
    [string]$HostName = $(if ($env:POSTGRES_HOST) { $env:POSTGRES_HOST } else { "127.0.0.1" }),
    [int]$Port = $(if ($env:POSTGRES_PORT) { [int]$env:POSTGRES_PORT } else { 5432 }),
    [string]$Username = $(if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { "garagebalance_local" }),
    [string]$ConnectionString = $(if ($env:ConnectionStrings__DefaultConnection) { $env:ConnectionStrings__DefaultConnection } else { "" }),
    [switch]$RequirePsql,
    [switch]$SkipPsqlConnection
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Database)) {
    throw "Database is required."
}

if ([string]::IsNullOrWhiteSpace($HostName)) {
    throw "HostName is required."
}

if ($Port -le 0 -or $Port -gt 65535) {
    throw "Port must be between 1 and 65535."
}

if ([string]::IsNullOrWhiteSpace($Username)) {
    throw "Username is required."
}

Write-Output "postgresHost=$HostName"
Write-Output "postgresPort=$Port"
Write-Output "postgresDatabase=$Database"
Write-Output "postgresUser=$Username"
Write-Output ("connectionStringProvided=" + (-not [string]::IsNullOrWhiteSpace($ConnectionString)))

$tcpAvailable = Test-NetConnection -ComputerName $HostName -Port $Port -InformationLevel Quiet
Write-Output "postgresTcp=$tcpAvailable"
if (-not $tcpAvailable) {
    throw "PostgreSQL TCP check failed: ${HostName}:$Port is not reachable."
}

$psql = Get-Command psql -ErrorAction SilentlyContinue
Write-Output ("psql=" + ($null -ne $psql))
if ($RequirePsql -and $null -eq $psql) {
    throw "psql was not found in PATH. Install PostgreSQL client tools or run without -RequirePsql for a TCP-only check."
}

if ($null -ne $psql -and -not $SkipPsqlConnection) {
    & $psql.Source `
        "--host=$HostName" `
        "--port=$Port" `
        "--username=$Username" `
        "--dbname=$Database" `
        "--tuples-only" `
        "--no-align" `
        "--command=SELECT 1;"

    if ($LASTEXITCODE -ne 0) {
        throw "psql connection check failed with exit code $LASTEXITCODE."
    }

    Write-Output "psqlConnection=True"
}
elseif ($null -eq $psql) {
    Write-Output "psqlConnection=Skipped"
}
else {
    Write-Output "psqlConnection=SkippedByRequest"
}

Write-Output "localPostgresPreflight=OK"
