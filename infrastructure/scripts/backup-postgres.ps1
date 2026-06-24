[CmdletBinding()]
param(
    [string]$Database = $(if ($env:POSTGRES_DB) { $env:POSTGRES_DB } else { "garagebalance_local" }),
    [string]$HostName = $(if ($env:POSTGRES_HOST) { $env:POSTGRES_HOST } else { "127.0.0.1" }),
    [int]$Port = $(if ($env:POSTGRES_PORT) { [int]$env:POSTGRES_PORT } else { 5432 }),
    [string]$Username = $(if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { "garagebalance_local" }),
    [string]$BackupDirectory = "C:\GarageBalance\Backups"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Database)) {
    throw "Database is required."
}

if ([string]::IsNullOrWhiteSpace($Username)) {
    throw "Username is required."
}

$backupDirectoryFullPath = [System.IO.Path]::GetFullPath($BackupDirectory)
New-Item -ItemType Directory -Force -Path $backupDirectoryFullPath | Out-Null

$pgDump = Get-Command pg_dump -ErrorAction Stop
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupPath = Join-Path $backupDirectoryFullPath "$Database-$timestamp.pgdump"
$temporaryPath = "$backupPath.incomplete"

if (Test-Path -LiteralPath $temporaryPath) {
    Remove-Item -LiteralPath $temporaryPath -Force
}

& $pgDump.Source `
    "--host=$HostName" `
    "--port=$Port" `
    "--username=$Username" `
    "--format=custom" `
    "--file=$temporaryPath" `
    "--no-owner" `
    "--no-privileges" `
    $Database

if ($LASTEXITCODE -ne 0) {
    throw "pg_dump failed with exit code $LASTEXITCODE."
}

$backupFile = Get-Item -LiteralPath $temporaryPath
if ($backupFile.Length -le 0) {
    Remove-Item -LiteralPath $temporaryPath -Force
    throw "Backup file is empty."
}

Move-Item -LiteralPath $temporaryPath -Destination $backupPath -Force
Write-Output "backupPath=$backupPath"
