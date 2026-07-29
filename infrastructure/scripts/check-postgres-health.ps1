param(
    [string]$Database = $env:PGDATABASE,
    [string]$PsqlPath = "psql"
)

$ErrorActionPreference = "Stop"

if (Get-Command $PsqlPath -ErrorAction SilentlyContinue | Select-Object -First 1) {
    $resolvedPsqlPath = (Get-Command $PsqlPath -ErrorAction Stop | Select-Object -First 1).Source
}
elseif (Test-Path -LiteralPath $PsqlPath -PathType Leaf) {
    $resolvedPsqlPath = (Resolve-Path -LiteralPath $PsqlPath).Path
}
else {
    throw "psql was not found. Install PostgreSQL client or pass -PsqlPath."
}

if ([string]::IsNullOrWhiteSpace($Database)) {
    throw "Pass -Database or set the PGDATABASE environment variable."
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$sqlPath = Join-Path $repositoryRoot "infrastructure\postgres\postgres-health.sql"
if (-not (Test-Path -LiteralPath $sqlPath -PathType Leaf)) {
    throw "PostgreSQL diagnostics SQL was not found: $sqlPath"
}

$arguments = @(
    "-X",
    "--set", "ON_ERROR_STOP=1",
    "--dbname", $Database,
    "--file", $sqlPath
)

& $resolvedPsqlPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw "PostgreSQL diagnostics failed with exit code $LASTEXITCODE."
}
