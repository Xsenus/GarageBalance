param(
    [string]$ComposeFile = "docker-compose.yml",
    [switch]$RequireBuild
)

$ErrorActionPreference = "Stop"

$resolvedComposeFile = Resolve-Path -LiteralPath $ComposeFile -ErrorAction Stop
Write-Output "composeFile=$($resolvedComposeFile.Path)"

$docker = Get-Command docker -ErrorAction SilentlyContinue
$dockerAvailable = $null -ne $docker
Write-Output "docker=$dockerAvailable"

if (-not $dockerAvailable) {
    Write-Output "dockerComposeConfig=Skipped"
    Write-Output "dockerComposeBuild=Skipped"

    if ($RequireBuild) {
        throw "Docker CLI was not found in PATH. Install Docker Desktop or run without -RequireBuild for a preflight-only check."
    }

    return
}

& $docker.Source --version
& $docker.Source compose version
& $docker.Source compose -f $resolvedComposeFile.Path config --quiet
if ($LASTEXITCODE -ne 0) {
    throw "docker compose config failed with exit code $LASTEXITCODE."
}

Write-Output "dockerComposeConfig=True"

if ($RequireBuild) {
    & $docker.Source compose -f $resolvedComposeFile.Path build
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose build failed with exit code $LASTEXITCODE."
    }

    Write-Output "dockerComposeBuild=True"
}
else {
    Write-Output "dockerComposeBuild=Skipped"
}
