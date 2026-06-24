[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupFile,
    [string]$TargetDatabase = "garagebalance_restore_check",
    [string]$HostName = $(if ($env:POSTGRES_HOST) { $env:POSTGRES_HOST } else { "127.0.0.1" }),
    [int]$Port = $(if ($env:POSTGRES_PORT) { [int]$env:POSTGRES_PORT } else { 5432 }),
    [string]$Username = $(if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { "garagebalance_local" }),
    [switch]$DropAndCreate,
    [switch]$AllowProductionTarget
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $BackupFile)) {
    throw "Backup file was not found: $BackupFile"
}

if ($TargetDatabase -notmatch "^[A-Za-z0-9_]+$") {
    throw "TargetDatabase may contain only latin letters, digits and underscore."
}

$protectedDatabases = @("garagebalance", "garagebalance_local", "garagebalance_staging")
if ($protectedDatabases -contains $TargetDatabase -and -not $AllowProductionTarget) {
    throw "Refusing to restore into protected database '$TargetDatabase'. Use a check database or pass -AllowProductionTarget intentionally."
}

$backupFullPath = [System.IO.Path]::GetFullPath($BackupFile)
$backupInfo = Get-Item -LiteralPath $backupFullPath
if ($backupInfo.Length -le 0) {
    throw "Backup file is empty: $backupFullPath"
}

$pgRestore = Get-Command pg_restore -ErrorAction Stop
$psql = Get-Command psql -ErrorAction Stop
$quotedDatabase = '"' + $TargetDatabase.Replace('"', '""') + '"'

if ($DropAndCreate) {
    if ($PSCmdlet.ShouldProcess($TargetDatabase, "drop and recreate PostgreSQL database")) {
        $terminateCommand = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$TargetDatabase';"
        $dropCommand = "DROP DATABASE IF EXISTS $quotedDatabase;"
        $createCommand = "CREATE DATABASE $quotedDatabase OWNER $quotedDatabase;"

        & $psql.Source "--host=$HostName" "--port=$Port" "--username=$Username" "--dbname=postgres" "--command=$terminateCommand"
        if ($LASTEXITCODE -ne 0) { throw "Failed to terminate sessions for $TargetDatabase." }

        & $psql.Source "--host=$HostName" "--port=$Port" "--username=$Username" "--dbname=postgres" "--command=$dropCommand"
        if ($LASTEXITCODE -ne 0) { throw "Failed to drop $TargetDatabase." }

        & $psql.Source "--host=$HostName" "--port=$Port" "--username=$Username" "--dbname=postgres" "--command=$createCommand"
        if ($LASTEXITCODE -ne 0) { throw "Failed to create $TargetDatabase." }
    }
}

& $pgRestore.Source `
    "--host=$HostName" `
    "--port=$Port" `
    "--username=$Username" `
    "--dbname=$TargetDatabase" `
    "--no-owner" `
    "--no-privileges" `
    "--verbose" `
    $backupFullPath

if ($LASTEXITCODE -ne 0) {
    throw "pg_restore failed with exit code $LASTEXITCODE."
}

Write-Output "restoreDatabase=$TargetDatabase"
