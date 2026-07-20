[CmdletBinding()]
param(
    [string]$DistributionPath = (Join-Path $PSScriptRoot "..\..\distribution\docker"),
    [switch]$RequireDocker
)

$ErrorActionPreference = "Stop"
$distribution = [System.IO.Path]::GetFullPath($DistributionPath)
$requiredFiles = @(
    ".env.example",
    "docker-compose.yml",
    "release-version.txt",
    "README.txt",
    "GarageBalance.Common.ps1",
    "start.ps1", "start.cmd",
    "update.ps1", "update.cmd",
    "backup.ps1", "backup.cmd",
    "diagnostics.ps1", "diagnostics.cmd",
    "stop.ps1", "stop.cmd"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $distribution $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Docker distribution file is missing: $relativePath"
    }
}

if (Test-Path -LiteralPath (Join-Path $distribution ".env")) {
    throw "A real .env file must never be included in the Docker distribution."
}

$environmentTemplate = [System.IO.File]::ReadAllText((Join-Path $distribution ".env.example"))
$versionTemplate = [System.IO.File]::ReadAllText((Join-Path $distribution "release-version.txt"))
foreach ($placeholder in @("__GARAGEBALANCE_VERSION__", "__GENERATE__")) {
    if (-not $environmentTemplate.Contains($placeholder)) {
        throw "Expected placeholder is missing from .env.example: $placeholder"
    }
}
if ($versionTemplate.Trim() -ne "__GARAGEBALANCE_VERSION__") {
    throw "release-version.txt must contain the release placeholder."
}

foreach ($scriptPath in Get-ChildItem -LiteralPath $distribution -Filter "*.ps1") {
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseInput(
        [System.IO.File]::ReadAllText($scriptPath.FullName),
        [ref]$tokens,
        [ref]$errors
    ) | Out-Null
    if ($errors.Count -gt 0) {
        throw "PowerShell syntax error in $($scriptPath.Name): $($errors[0].Message)"
    }
}

$composeText = [System.IO.File]::ReadAllText((Join-Path $distribution "docker-compose.yml"))
foreach ($requiredFragment in @(
    "name: garagebalance",
    "pull_policy: never",
    "postgres-data:",
    "data-protection-keys:"
)) {
    if (-not $composeText.Contains($requiredFragment)) {
        throw "Docker Compose distribution contract is missing: $requiredFragment"
    }
}

$docker = Get-Command docker -ErrorAction SilentlyContinue
if ($null -ne $docker) {
    $temporaryEnvironment = Join-Path $distribution ".env.validation"
    try {
        $validationEnvironment = $environmentTemplate.Replace("__GARAGEBALANCE_VERSION__", "0.0.0-validation")
        $validationEnvironment = $validationEnvironment.Replace("__GENERATE__", "validation-only-secret")
        [System.IO.File]::WriteAllText($temporaryEnvironment, $validationEnvironment, [System.Text.UTF8Encoding]::new($false))
        $previousErrorPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        & $docker.Source compose --project-name garagebalance-validation --env-file $temporaryEnvironment -f (Join-Path $distribution "docker-compose.yml") config --quiet
        $composeExitCode = $LASTEXITCODE
        $ErrorActionPreference = $previousErrorPreference
        if ($composeExitCode -ne 0) {
            throw "docker compose config rejected the Docker distribution."
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryEnvironment) {
            Remove-Item -LiteralPath $temporaryEnvironment -Force
        }
    }
}
elseif ($RequireDocker) {
    throw "Docker CLI is unavailable; the required Compose validation could not run."
}
else {
    Write-Warning "Docker CLI is unavailable; static distribution checks passed, Compose validation was skipped."
}

$global:LASTEXITCODE = 0
Write-Host "Docker distribution checks passed."
