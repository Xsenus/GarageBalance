$ErrorActionPreference = "Stop"

$script:GarageBalanceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:GarageBalanceComposeFile = Join-Path $script:GarageBalanceRoot "docker-compose.yml"
$script:GarageBalanceEnvFile = Join-Path $script:GarageBalanceRoot ".env"
$script:GarageBalanceVersionFile = Join-Path $script:GarageBalanceRoot "release-version.txt"
$script:GarageBalanceUtf8 = New-Object System.Text.UTF8Encoding($false)

function Write-GarageBalanceStep {
    param([Parameter(Mandatory = $true)][string]$Message)

    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Test-GarageBalanceDockerCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$Arguments,
        [int]$TimeoutSeconds = 10
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $Arguments
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            return $false
        }
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            $process.WaitForExit()
            return $false
        }

        return $process.ExitCode -eq 0
    }
    finally {
        $process.Dispose()
    }
}

function Assert-DockerReady {
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -eq $docker) {
        throw "Docker не найден. Установите Docker Desktop и повторите запуск GarageBalance."
    }

    if (-not (Test-GarageBalanceDockerCommand -FilePath $docker.Source -Arguments 'info --format "{{.ServerVersion}}"')) {
        throw "Docker Desktop установлен, но Docker Engine не запущен. Запустите Docker Desktop и дождитесь готовности Engine."
    }

    if (-not (Test-GarageBalanceDockerCommand -FilePath $docker.Source -Arguments "compose version")) {
        throw "Docker Compose недоступен. Обновите Docker Desktop до версии с Compose v2."
    }
}

function Invoke-Docker {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & docker @Arguments
    $dockerExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorPreference
    if ($dockerExitCode -ne 0) {
        throw "Команда Docker завершилась с ошибкой: docker $($Arguments -join ' ')"
    }
}

function Invoke-DockerQuiet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & docker @Arguments *> $null
    $dockerExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorPreference
    if ($dockerExitCode -ne 0) {
        throw "Команда Docker завершилась с ошибкой: docker $($Arguments -join ' ')"
    }
}

function Invoke-GarageBalanceCompose {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $composeArguments = @(
        "compose",
        "--project-name", "garagebalance",
        "--env-file", $script:GarageBalanceEnvFile,
        "-f", $script:GarageBalanceComposeFile
    ) + $Arguments

    Invoke-Docker -Arguments $composeArguments
}

function Invoke-GarageBalanceComposeQuiet {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $composeArguments = @(
        "compose",
        "--project-name", "garagebalance",
        "--env-file", $script:GarageBalanceEnvFile,
        "-f", $script:GarageBalanceComposeFile
    ) + $Arguments

    Invoke-DockerQuiet -Arguments $composeArguments
}

function New-GarageBalanceSecret {
    param([int]$ByteCount = 48)

    $bytes = New-Object byte[] $ByteCount
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }

    return [Convert]::ToBase64String($bytes)
}

function Get-GarageBalanceEnvironment {
    if (-not (Test-Path -LiteralPath $script:GarageBalanceEnvFile -PathType Leaf)) {
        throw "Файл .env не найден. Сначала запустите start.cmd."
    }

    $settings = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($script:GarageBalanceEnvFile)) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) {
            continue
        }

        $separator = $trimmed.IndexOf("=")
        if ($separator -le 0) {
            continue
        }

        $name = $trimmed.Substring(0, $separator).Trim()
        $value = $trimmed.Substring($separator + 1).Trim()
        $settings[$name] = $value
    }

    return $settings
}

function Set-GarageBalanceEnvironmentValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $found = $false
    foreach ($line in [System.IO.File]::ReadAllLines($script:GarageBalanceEnvFile)) {
        if ($line -match "^$([regex]::Escape($Name))=") {
            $lines.Add("$Name=$Value")
            $found = $true
        }
        else {
            $lines.Add($line)
        }
    }

    if (-not $found) {
        $lines.Add("$Name=$Value")
    }

    [System.IO.File]::WriteAllLines($script:GarageBalanceEnvFile, $lines, $script:GarageBalanceUtf8)
}

function Initialize-GarageBalanceEnvironment {
    $template = Join-Path $script:GarageBalanceRoot ".env.example"
    if (-not (Test-Path -LiteralPath $template -PathType Leaf)) {
        throw "В установочном архиве отсутствует .env.example. Скачайте архив повторно."
    }

    if (-not (Test-Path -LiteralPath $script:GarageBalanceEnvFile -PathType Leaf)) {
        [System.IO.File]::Copy($template, $script:GarageBalanceEnvFile)
        Set-GarageBalanceEnvironmentValue -Name "POSTGRES_PASSWORD" -Value (New-GarageBalanceSecret)
        Set-GarageBalanceEnvironmentValue -Name "JWT_SIGNING_KEY" -Value (New-GarageBalanceSecret)
        Write-Host "Создана защищенная локальная конфигурация .env." -ForegroundColor Green
    }

    $settings = Get-GarageBalanceEnvironment
    if ($settings["POSTGRES_PASSWORD"] -eq "__GENERATE__" -or $settings["JWT_SIGNING_KEY"] -eq "__GENERATE__") {
        throw "Секреты GarageBalance не были сгенерированы. Удалите незаполненный .env и повторите start.cmd."
    }

    foreach ($directoryName in @("backups", "logs", "diagnostics")) {
        $directory = Join-Path $script:GarageBalanceRoot $directoryName
        [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    }
}

function Set-GarageBalanceBundleVersion {
    if (-not (Test-Path -LiteralPath $script:GarageBalanceVersionFile -PathType Leaf)) {
        throw "В установочном архиве отсутствует release-version.txt. Скачайте архив повторно."
    }

    $version = ([System.IO.File]::ReadAllText($script:GarageBalanceVersionFile)).Trim()
    if ($version -notmatch "^[0-9]+\.[0-9]+\.[0-9]+([-.][0-9A-Za-z.-]+)?$") {
        throw "Некорректная версия установочного архива GarageBalance: $version"
    }

    Set-GarageBalanceEnvironmentValue -Name "GARAGEBALANCE_VERSION" -Value $version
    return $version
}

function Import-GarageBalanceImages {
    param([Parameter(Mandatory = $true)][string]$Version)

    $imageDirectory = Join-Path $script:GarageBalanceRoot "images"
    $archives = @(
        (Join-Path $imageDirectory "garagebalance-api-$Version.tar.gz"),
        (Join-Path $imageDirectory "garagebalance-frontend-$Version.tar.gz")
    )

    foreach ($archive in $archives) {
        if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
            throw "В установочном архиве отсутствует Docker image: $(Split-Path -Leaf $archive). Скачайте полный ZIP повторно."
        }

        Write-Host "Загружаем $(Split-Path -Leaf $archive)..."
        Invoke-Docker -Arguments @("load", "--input", $archive)
    }
}

function Wait-GarageBalanceHealth {
    param([int]$Attempts = 90, [int]$DelaySeconds = 2)

    $settings = Get-GarageBalanceEnvironment
    $port = if ($settings.ContainsKey("FRONTEND_PORT")) { $settings["FRONTEND_PORT"] } else { "5173" }
    if ($port -notmatch "^[0-9]{1,5}$") {
        throw "В .env указано некорректное значение FRONTEND_PORT."
    }

    $healthUri = "http://127.0.0.1:$port/health"
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri $healthUri -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                return "http://127.0.0.1:$port"
            }
        }
        catch {
            if ($attempt -eq $Attempts) {
                break
            }
        }

        Start-Sleep -Seconds $DelaySeconds
    }

    throw "GarageBalance не прошел health-check. Запустите diagnostics.cmd и передайте созданный отчет администратору."
}
