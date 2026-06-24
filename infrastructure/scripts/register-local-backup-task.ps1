[CmdletBinding()]
param(
    [string]$TaskName = "GarageBalance Local PostgreSQL Backup",
    [string]$Database = "garagebalance_local",
    [string]$HostName = "127.0.0.1",
    [int]$Port = 5432,
    [string]$Username = "garagebalance_local",
    [string]$BackupDirectory = "C:\GarageBalance\Backups",
    [string]$At = "23:00"
)

$ErrorActionPreference = "Stop"

$backupScriptPath = Join-Path $PSScriptRoot "backup-postgres.ps1"
if (-not (Test-Path -LiteralPath $backupScriptPath)) {
    throw "Backup script was not found: $backupScriptPath"
}

New-Item -ItemType Directory -Force -Path $BackupDirectory | Out-Null

$powershellCommand = Get-Command pwsh -ErrorAction SilentlyContinue
if ($null -eq $powershellCommand) {
    $powershellCommand = Get-Command powershell -ErrorAction Stop
}

$arguments = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", "`"$backupScriptPath`"",
    "-Database", "`"$Database`"",
    "-HostName", "`"$HostName`"",
    "-Port", "$Port",
    "-Username", "`"$Username`"",
    "-BackupDirectory", "`"$BackupDirectory`""
) -join " "

$triggerTime = [datetime]::ParseExact($At, "HH:mm", [System.Globalization.CultureInfo]::InvariantCulture)
$action = New-ScheduledTaskAction -Execute $powershellCommand.Source -Argument $arguments
$trigger = New-ScheduledTaskTrigger -Daily -At $triggerTime
$description = "Creates GarageBalance PostgreSQL custom-format backup in $BackupDirectory."

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Description $description -Force | Out-Null
Write-Output "scheduledTask=$TaskName"
