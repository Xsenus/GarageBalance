param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,

    [ValidateRange(1, 1000000)]
    [int]$MinimumCount = 1,

    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

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

function Get-NormalizedRoute {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri
    )

    $route = $Uri.Split("?")[0]
    $route = [Regex]::Replace(
        $route,
        "/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?=/|$)",
        "/:id")
    $route = [Regex]::Replace($route, "/[0-9]+(?=/|$)", "/:id")
    return $route
}

function Get-Section {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Route
    )

    if ($Route -eq "/health") { return "health" }
    if ($Route -match "^/api/auth(?:/|$)") { return "auth" }
    if ($Route -match "^/api/(?:users|roles|audit|settings|diagnostics)(?:/|$)") { return "administration" }
    if ($Route -match "^/api/dictionaries(?:/|$)") { return "dictionaries" }
    if ($Route -match "^/api/(?:finance|meter-readings)(?:/|$)") { return "finance" }
    if ($Route -match "^/api/funds(?:/|$)") { return "funds" }
    if ($Route -match "^/api/reports(?:/|$)") { return "reports" }
    if ($Route -match "^/api/import(?:/|$)") { return "import" }
    if ($Route -match "^/api/app-releases(?:/|$)") { return "releases" }
    if ($Route -match "^/api(?:/|$)") { return "other-api" }
    return "frontend"
}

function Convert-TimingLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Line
    )

    $values = @{}
    foreach ($match in [Regex]::Matches($Line, "(?:^|\s)([a-z_]+)=([^\s]*)")) {
        $values[$match.Groups[1].Value] = $match.Groups[2].Value
    }

    $method = $values["method"]
    $uri = $values["uri"]
    $statusText = $values["status"]
    $requestTimeText = $values["request_time"]
    if ([string]::IsNullOrWhiteSpace($method) -or
        [string]::IsNullOrWhiteSpace($uri) -or
        -not $uri.StartsWith("/") -or
        [string]::IsNullOrWhiteSpace($statusText) -or
        [string]::IsNullOrWhiteSpace($requestTimeText)) {
        return $null
    }

    $status = 0
    $requestTime = 0.0
    if (-not [int]::TryParse($statusText, [ref]$status) -or
        -not [double]::TryParse(
            $requestTimeText,
            [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$requestTime)) {
        return $null
    }

    $route = Get-NormalizedRoute -Uri $uri
    return [pscustomobject]@{
        Section = Get-Section -Route $route
        Method = $method.ToUpperInvariant()
        Route = $route
        Status = $status
        RequestTimeMilliseconds = $requestTime * 1000.0
    }
}

if ($InputPath -eq "STDIN") {
    $lines = [Console]::In.ReadToEnd() -split "\r?\n"
}
else {
    if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) {
        throw "Timing log was not found: $InputPath"
    }

    $lines = Get-Content -LiteralPath $InputPath -Encoding UTF8
}

$parsed = New-Object System.Collections.Generic.List[object]
$ignoredRows = 0
foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $entry = Convert-TimingLine -Line $line
    if ($null -eq $entry) {
        $ignoredRows++
        continue
    }

    $parsed.Add($entry)
}

function Get-TimingSummary {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Items
    )

    $times = [double[]]@($Items | ForEach-Object { $_.RequestTimeMilliseconds } | Sort-Object)
    $clientErrors = @($Items | Where-Object { $_.Status -ge 400 -and $_.Status -lt 500 }).Count
    $serverErrors = @($Items | Where-Object { $_.Status -ge 500 }).Count
    $errors = $clientErrors + $serverErrors
    return [ordered]@{
        count = $Items.Count
        p50Milliseconds = [Math]::Round((Get-Percentile -SortedValues $times -Percentile 50), 1)
        p95Milliseconds = [Math]::Round((Get-Percentile -SortedValues $times -Percentile 95), 1)
        maxMilliseconds = [Math]::Round($times[-1], 1)
        clientErrorCount = $clientErrors
        serverErrorCount = $serverErrors
        errorCount = $errors
        errorRatePercent = [Math]::Round(($errors * 100.0) / $Items.Count, 2)
    }
}

$routes = @(
    $parsed |
        Group-Object Section, Method, Route |
        ForEach-Object {
            $items = @($_.Group)
            $summary = Get-TimingSummary -Items $items
            [pscustomobject]([ordered]@{
                section = $items[0].Section
                method = $items[0].Method
                route = $items[0].Route
            } + $summary)
        } |
        Where-Object { $_.count -ge $MinimumCount } |
        Sort-Object @{ Expression = "p95Milliseconds"; Descending = $true },
                    @{ Expression = "count"; Descending = $true },
                    section,
                    route
)

$sections = @(
    $parsed |
        Group-Object Section |
        ForEach-Object {
            $items = @($_.Group)
            $summary = Get-TimingSummary -Items $items
            [pscustomobject]([ordered]@{
                section = $items[0].Section
                routeCount = @($routes | Where-Object { $_.section -eq $items[0].Section }).Count
            } + $summary)
        } |
        Sort-Object @{ Expression = "p95Milliseconds"; Descending = $true },
                    @{ Expression = "count"; Descending = $true },
                    section
)

$result = [pscustomobject]@{
    parsedRows = $parsed.Count
    ignoredRows = $ignoredRows
    sectionCount = $sections.Count
    routeCount = $routes.Count
    sections = $sections
    routes = $routes
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 5 -Compress
}
else {
    Write-Output "parsedRows=$($result.parsedRows); ignoredRows=$($result.ignoredRows); sectionCount=$($result.sectionCount); routeCount=$($result.routeCount)"
    $sections |
        Select-Object section, routeCount, count, p50Milliseconds, p95Milliseconds, maxMilliseconds, clientErrorCount, serverErrorCount, errorRatePercent |
        Format-Table -AutoSize
    $routes |
        Select-Object section, method, route, count, p50Milliseconds, p95Milliseconds, maxMilliseconds, clientErrorCount, serverErrorCount, errorRatePercent |
        Format-Table -AutoSize
}
