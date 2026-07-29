param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^https?://")]
    [string]$BaseUrl,

    [string]$ScenarioPath = "",

    [string[]]$ScenarioName = @(),

    [ValidateRange(1, 1000)]
    [int]$Iterations = 20,

    [ValidateRange(0, 100)]
    [int]$WarmupIterations = 2,

    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 30,

    [switch]$AsJson
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
if ([string]::IsNullOrWhiteSpace($ScenarioPath)) {
    $ScenarioPath = Join-Path $PSScriptRoot "..\performance\api-smoke-scenarios.json"
}

function Get-Percentile {
    param(
        [Parameter(Mandatory = $true)]
        [double[]]$SortedValues,

        [Parameter(Mandatory = $true)]
        [ValidateRange(0, 100)]
        [int]$Percentile
    )

    if ($SortedValues.Count -eq 0) {
        return 0
    }

    $rank = [Math]::Ceiling(($Percentile / 100.0) * $SortedValues.Count)
    $index = [Math]::Max(0, [Math]::Min($SortedValues.Count - 1, $rank - 1))
    return $SortedValues[$index]
}

function Send-ScenarioRequest {
    param(
        [Parameter(Mandatory = $true)]
        [System.Net.Http.HttpClient]$Client,

        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [bool]$Authorized,

        [AllowEmptyString()]
        [string]$Token
    )

    $request = [System.Net.Http.HttpRequestMessage]::new(
        [System.Net.Http.HttpMethod]::Get,
        $Path)
    try {
        if ($Authorized) {
            $request.Headers.Authorization =
                [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $Token)
        }

        return $Client.SendAsync($request).GetAwaiter().GetResult()
    }
    finally {
        $request.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $ScenarioPath -PathType Leaf)) {
    throw "API benchmark scenario file was not found: $ScenarioPath"
}

$scenarioDocument = Get-Content -LiteralPath $ScenarioPath -Raw -Encoding UTF8 | ConvertFrom-Json
$scenarios = @($scenarioDocument.GetEnumerator())
if ($scenarios.Count -eq 0) {
    throw "API benchmark scenario file is empty."
}
if ($ScenarioName.Count -gt 0) {
    $requestedNames = @($ScenarioName | Sort-Object -Unique)
    $scenarios = @($scenarios | Where-Object { $requestedNames -contains [string]$_.name })
    if ($scenarios.Count -ne $requestedNames.Count) {
        throw "One or more requested API benchmark scenarios were not found."
    }
}

$token = $env:GARAGEBALANCE_BENCHMARK_TOKEN
if (@($scenarios | Where-Object { $_.authorized }).Count -gt 0 -and
    [string]::IsNullOrWhiteSpace($token)) {
    throw "Set GARAGEBALANCE_BENCHMARK_TOKEN for authorized API scenarios."
}

$client = [System.Net.Http.HttpClient]::new()
$client.BaseAddress = [Uri]::new($BaseUrl.TrimEnd("/") + "/")
$client.Timeout = [TimeSpan]::FromSeconds($TimeoutSeconds)

try {
    $results = New-Object System.Collections.Generic.List[object]
    foreach ($scenario in $scenarios) {
        $name = [string]$scenario.name
        $path = [string]$scenario.path
        if ([string]::IsNullOrWhiteSpace($name) -or
            [string]::IsNullOrWhiteSpace($path) -or
            -not $path.StartsWith("/")) {
            throw "Every API benchmark scenario requires a name and an absolute path."
        }
        $p50Threshold = [double]$scenario.p50Milliseconds
        $p95Threshold = [double]$scenario.p95Milliseconds
        $errorThreshold = [double]$scenario.maxErrorRatePercent
        if ($p50Threshold -le 0 -or
            $p95Threshold -lt $p50Threshold -or
            $errorThreshold -lt 0 -or
            $errorThreshold -gt 100) {
            throw "API benchmark scenario '$name' has invalid thresholds."
        }

        for ($warmup = 0; $warmup -lt $WarmupIterations; $warmup++) {
            try {
                $warmupResponse = Send-ScenarioRequest `
                    -Client $client `
                    -Path $path `
                    -Authorized ([bool]$scenario.authorized) `
                    -Token $token
                try {
                    $null = $warmupResponse.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                }
                finally {
                    $warmupResponse.Dispose()
                }
            }
            catch {
                # A failed warmup is intentionally excluded from the measured sample.
            }
        }

        $times = New-Object System.Collections.Generic.List[double]
        $errors = 0
        $responseBytes = 0L
        for ($iteration = 0; $iteration -lt $Iterations; $iteration++) {
            $stopwatch = [Diagnostics.Stopwatch]::StartNew()
            try {
                $response = Send-ScenarioRequest `
                    -Client $client `
                    -Path $path `
                    -Authorized ([bool]$scenario.authorized) `
                    -Token $token
                try {
                    $content = $response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
                    $responseBytes += $content.Length
                    if (-not $response.IsSuccessStatusCode) {
                        $errors++
                    }
                }
                finally {
                    $response.Dispose()
                }
            }
            catch {
                $errors++
            }
            finally {
                $stopwatch.Stop()
                $times.Add($stopwatch.Elapsed.TotalMilliseconds)
            }
        }

        $sortedTimes = [double[]]@($times | Sort-Object)
        $p50 = [Math]::Round((Get-Percentile -SortedValues $sortedTimes -Percentile 50), 1)
        $p95 = [Math]::Round((Get-Percentile -SortedValues $sortedTimes -Percentile 95), 1)
        $max = [Math]::Round($sortedTimes[-1], 1)
        $errorRate = [Math]::Round(($errors * 100.0) / $Iterations, 2)
        $passed = $p50 -le $p50Threshold -and
            $p95 -le $p95Threshold -and
            $errorRate -le $errorThreshold

        $results.Add([pscustomobject]@{
            name = $name
            count = $Iterations
            p50Milliseconds = $p50
            p95Milliseconds = $p95
            maxMilliseconds = $max
            errorCount = $errors
            errorRatePercent = $errorRate
            averageResponseBytes = [Math]::Round($responseBytes / [double]$Iterations, 1)
            thresholds = [pscustomobject]@{
                p50Milliseconds = $p50Threshold
                p95Milliseconds = $p95Threshold
                maxErrorRatePercent = $errorThreshold
            }
            passed = $passed
        })
    }

    $failedScenarios = @($results | Where-Object { -not $_.passed })
    $output = [pscustomobject]@{
        scenarioCount = $results.Count
        requestCount = $results.Count * $Iterations
        failedScenarioCount = $failedScenarios.Count
        passed = $failedScenarios.Count -eq 0
        scenarios = $results
    }

    if ($AsJson) {
        $output | ConvertTo-Json -Depth 5 -Compress
    }
    else {
        Write-Output "scenarioCount=$($output.scenarioCount); requestCount=$($output.requestCount); failedScenarioCount=$($output.failedScenarioCount); passed=$($output.passed)"
        $results |
            Select-Object name, count, p50Milliseconds, p95Milliseconds, maxMilliseconds, errorRatePercent, averageResponseBytes, passed |
            Format-Table -AutoSize
    }

    if (-not $output.passed) {
        exit 1
    }
}
finally {
    $client.Dispose()
}
