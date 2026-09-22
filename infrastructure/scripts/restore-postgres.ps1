[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "High")]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupFile,
    [string]$TargetDatabase = "garagebalance_restore_check",
    [string]$HostName = $(if ($env:POSTGRES_HOST) { $env:POSTGRES_HOST } else { "127.0.0.1" }),
    [int]$Port = $(if ($env:POSTGRES_PORT) { [int]$env:POSTGRES_PORT } else { 5432 }),
    [string]$Username = $(if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { "garagebalance_local" }),
    [switch]$DropAndCreate,
    [switch]$VerifyAndDrop,
    [string]$ManifestFile,
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
if ($protectedDatabases -contains $TargetDatabase -and $VerifyAndDrop) {
    throw "VerifyAndDrop is forbidden for protected database names."
}

$backupFullPath = [System.IO.Path]::GetFullPath($BackupFile)
$backupInfo = Get-Item -LiteralPath $backupFullPath
if ($backupInfo.Length -le 0) {
    throw "Backup file is empty: $backupFullPath"
}

$restoreStartedAt = [DateTimeOffset]::UtcNow
$effectiveManifest = if ($ManifestFile) { $ManifestFile } else { "$backupFullPath.manifest.json" }
if (Test-Path -LiteralPath $effectiveManifest) {
    $manifest = Get-Content -LiteralPath $effectiveManifest -Raw | ConvertFrom-Json
    if (-not $manifest.sha256 -or -not $manifest.sizeBytes) {
        throw "Backup manifest does not contain sizeBytes and sha256."
    }
    if ([int64]$manifest.sizeBytes -ne $backupInfo.Length) {
        throw "Backup size does not match the manifest."
    }
    $actualSha256 = (Get-FileHash -LiteralPath $backupFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualSha256 -ne ([string]$manifest.sha256).ToLowerInvariant()) {
        throw "Backup SHA-256 does not match the manifest."
    }
    Write-Output "manifestVerified=True"
} else {
    Write-Warning "Manifest was not found; restore structure will be checked, but SHA-256 cannot be compared with the source manifest."
    Write-Output "manifestVerified=False"
}

$pgRestore = Get-Command pg_restore -ErrorAction Stop
$psql = Get-Command psql -ErrorAction Stop
$quotedDatabase = '"' + $TargetDatabase.Replace('"', '""') + '"'
$quotedOwner = '"' + $Username.Replace('"', '""') + '"'

& $pgRestore.Source "--list" $backupFullPath | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "pg_restore could not read the backup table of contents."
}

function Remove-CheckDatabase {
    & $psql.Source "--host=$HostName" "--port=$Port" "--username=$Username" "--dbname=postgres" "--command=SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$TargetDatabase';" | Out-Null
    & $psql.Source "--host=$HostName" "--port=$Port" "--username=$Username" "--dbname=postgres" "--command=DROP DATABASE IF EXISTS $quotedDatabase;" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to remove disposable restore database $TargetDatabase." }
}

if ($DropAndCreate) {
    if ($PSCmdlet.ShouldProcess($TargetDatabase, "drop and recreate PostgreSQL database")) {
        $terminateCommand = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$TargetDatabase';"
        $dropCommand = "DROP DATABASE IF EXISTS $quotedDatabase;"
        $createCommand = "CREATE DATABASE $quotedDatabase OWNER $quotedOwner;"

        & $psql.Source "--host=$HostName" "--port=$Port" "--username=$Username" "--dbname=postgres" "--command=$terminateCommand"
        if ($LASTEXITCODE -ne 0) { throw "Failed to terminate sessions for $TargetDatabase." }

        & $psql.Source "--host=$HostName" "--port=$Port" "--username=$Username" "--dbname=postgres" "--command=$dropCommand"
        if ($LASTEXITCODE -ne 0) { throw "Failed to drop $TargetDatabase." }

        & $psql.Source "--host=$HostName" "--port=$Port" "--username=$Username" "--dbname=postgres" "--command=$createCommand"
        if ($LASTEXITCODE -ne 0) { throw "Failed to create $TargetDatabase." }
    }
}

try {
    & $pgRestore.Source `
        "--host=$HostName" `
        "--port=$Port" `
        "--username=$Username" `
        "--dbname=$TargetDatabase" `
        "--no-owner" `
        "--no-privileges" `
        "--exit-on-error" `
        "--verbose" `
        $backupFullPath

    if ($LASTEXITCODE -ne 0) {
        throw "pg_restore failed with exit code $LASTEXITCODE."
    }

    $tableCount = & $psql.Source "--host=$HostName" "--port=$Port" "--username=$Username" "--dbname=$TargetDatabase" "--tuples-only" "--no-align" "--command=SELECT count(*) FROM pg_tables WHERE schemaname = 'public';"
    if ($LASTEXITCODE -ne 0 -or [int]$tableCount -le 0) {
        throw "Restored database does not contain application tables."
    }
    $migrationTable = & $psql.Source "--host=$HostName" "--port=$Port" "--username=$Username" "--dbname=$TargetDatabase" "--tuples-only" "--no-align" "--command=SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '__EFMigrationsHistory';"
    if ($LASTEXITCODE -ne 0 -or ([string]$migrationTable).Trim() -ne "1") {
        throw "Restored database does not contain EF migration history."
    }

    Write-Output "restoreDatabase=$TargetDatabase"
    Write-Output "restoredTableCount=$([int]$tableCount)"
    Write-Output "restoreDurationSeconds=$([math]::Round(([DateTimeOffset]::UtcNow - $restoreStartedAt).TotalSeconds, 3))"
    Write-Output "restoreCheckStatus=completed"
}
finally {
    if ($VerifyAndDrop) {
        Remove-CheckDatabase
        Write-Output "restoreDatabaseRemoved=True"
    }
}
