[CmdletBinding()]
param(
    [string]$OutputPath = "artifacts\deploy-migrations.sql",
    [string]$Project = "backend\GarageBalance.Api\GarageBalance.Api.csproj",
    [string]$StartupProject = "backend\GarageBalance.Api\GarageBalance.Api.csproj",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$projectPath = if ([System.IO.Path]::IsPathRooted($Project)) {
    [System.IO.Path]::GetFullPath($Project)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Project))
}
$startupProjectPath = if ([System.IO.Path]::IsPathRooted($StartupProject)) {
    [System.IO.Path]::GetFullPath($StartupProject)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $StartupProject))
}
$outputFullPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}
$outputDirectory = Split-Path -Parent $outputFullPath

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Project file was not found: $projectPath"
}

if (-not (Test-Path -LiteralPath $startupProjectPath -PathType Leaf)) {
    throw "Startup project file was not found: $startupProjectPath"
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$dotnet = Get-Command dotnet -ErrorAction Stop
$arguments = @(
    "tool",
    "run",
    "dotnet-ef",
    "migrations",
    "script",
    "--idempotent",
    "--project",
    $projectPath,
    "--startup-project",
    $startupProjectPath,
    "--output",
    $outputFullPath
)

if ($NoBuild) {
    $arguments += "--no-build"
}

& $dotnet.Source @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet-ef migrations script failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $outputFullPath -PathType Leaf)) {
    throw "Migration script was not created: $outputFullPath"
}

$outputFile = Get-Item -LiteralPath $outputFullPath
if ($outputFile.Length -le 0) {
    throw "Migration script is empty: $outputFullPath"
}

Write-Output "migrationScriptPath=$outputFullPath"
Write-Output "migrationScriptBytes=$($outputFile.Length)"
