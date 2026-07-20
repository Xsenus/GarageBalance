[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot "GarageBalance.Common.ps1")

function Invoke-DiagnosticDockerCommand {
    param(
        [Parameter(Mandatory = $true)][string]$DockerPath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [int]$TimeoutSeconds = 20
    )

    $quotedArguments = $Arguments | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $DockerPath
    $startInfo.Arguments = $quotedArguments -join " "
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            return "Не удалось запустить Docker CLI."
        }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill()
            $process.WaitForExit()
            return "Команда Docker прервана после тайм-аута $TimeoutSeconds секунд."
        }

        $output = ($standardOutput.Result + [Environment]::NewLine + $standardError.Result).Trim()
        return "Exit code: $($process.ExitCode)`r`n$output"
    }
    finally {
        $process.Dispose()
    }
}

try {
    Set-Location $PSScriptRoot
    [System.IO.Directory]::CreateDirectory((Join-Path $PSScriptRoot "diagnostics")) | Out-Null
    $outputPath = Join-Path $PSScriptRoot ("diagnostics\garagebalance_{0}.txt" -f (Get-Date -Format "yyyyMMdd_HHmmss"))
    $writer = New-Object System.IO.StreamWriter($outputPath, $false, $script:GarageBalanceUtf8)
    try {
        $writer.WriteLine("GarageBalance diagnostics")
        $writer.WriteLine("Created: {0:O}" -f (Get-Date))
        if (Test-Path -LiteralPath $script:GarageBalanceVersionFile) {
            $writer.WriteLine("Bundle version: {0}" -f ([System.IO.File]::ReadAllText($script:GarageBalanceVersionFile).Trim()))
        }
        $writer.WriteLine("")
        $writer.Flush()
    }
    finally {
        $writer.Dispose()
    }

    $commands = @(@{ Title = "Docker version"; Arguments = @("version") })
    if (Test-Path -LiteralPath $script:GarageBalanceEnvFile -PathType Leaf) {
        $composePrefix = @(
            "compose", "--project-name", "garagebalance",
            "--env-file", $script:GarageBalanceEnvFile,
            "-f", $script:GarageBalanceComposeFile
        )
        $commands += @(
            @{ Title = "Compose services"; Arguments = $composePrefix + @("ps", "--all") },
            @{ Title = "PostgreSQL logs"; Arguments = $composePrefix + @("logs", "--tail", "200", "postgres") },
            @{ Title = "API logs"; Arguments = $composePrefix + @("logs", "--tail", "200", "api") },
            @{ Title = "Frontend logs"; Arguments = $composePrefix + @("logs", "--tail", "200", "frontend") }
        )
    }
    else {
        Add-Content -LiteralPath $outputPath -Encoding UTF8 -Value "`r`n.env еще не создан: диагностика ограничена состоянием Docker."
    }

    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($null -eq $docker) {
        Add-Content -LiteralPath $outputPath -Encoding UTF8 -Value "`r`nDocker CLI не найден."
    }
    else {
        foreach ($command in $commands) {
            Add-Content -LiteralPath $outputPath -Encoding UTF8 -Value ("`r`n=== {0} ===" -f $command.Title)
            $commandOutput = Invoke-DiagnosticDockerCommand -DockerPath $docker.Source -Arguments $command.Arguments
            Add-Content -LiteralPath $outputPath -Encoding UTF8 -Value $commandOutput
        }
    }

    Write-Host "Диагностический отчет создан: $outputPath" -ForegroundColor Green
    Write-Host "Файл .env и секреты в отчет не добавляются."
}
catch {
    Write-Host "Не удалось создать диагностический отчет." -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
