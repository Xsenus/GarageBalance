[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$StorageToolDll,
    [Parameter(Mandatory = $true)][string]$ApiDll,
    [Parameter(Mandatory = $true)][string]$BootstrapFile,
    [Parameter(Mandatory = $true)][string]$EncryptionKeyFile,
    [Parameter(Mandatory = $true)][string]$PrimaryFailureDomain
)

$ErrorActionPreference = 'Stop'
foreach ($taskFile in @($StorageToolDll, $ApiDll, $BootstrapFile, $EncryptionKeyFile)) {
    if (-not (Test-Path -LiteralPath $taskFile -PathType Leaf)) { throw 'A required recovery input file was not found.' }
}
if (-not $env:Recovery__DrillConnectionString -or -not $env:Storage__Recovery__VerificationReportPath) {
    throw 'Configure a separate loopback drill PostgreSQL connection and durable verification report path in the scheduler account environment.'
}

# The tool creates its own generated disposable database, blocks business workers and all mutation
# routes except login, and always cleans its process, scratch files and database. Never invoke a
# production restore script from a scheduled job. Parameters contain paths/IDs, never credentials.
& dotnet $StorageToolDll restore-drill --execute --api-dll $ApiDll --bootstrap $BootstrapFile `
    --key-file $EncryptionKeyFile --exclude-failure-domain $PrimaryFailureDomain
if ($LASTEXITCODE -ne 0) { throw "Isolated restore verification failed with safe exit code $LASTEXITCODE. Check the durable verification report." }
