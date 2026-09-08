param(
    [string]$Solution = (Join-Path $PSScriptRoot '..\..\GarageBalance.slnx')
)

$ErrorActionPreference = 'Stop'

# dotnet list reports vulnerable packages with exit code 0; inspect its JSON result.
$auditOutput = & dotnet list $Solution package --vulnerable --include-transitive --format json --output-version 1
if ($LASTEXITCODE -ne 0) {
    throw "NuGet dependency audit command failed with exit code $LASTEXITCODE."
}

$report = ($auditOutput -join [Environment]::NewLine) | ConvertFrom-Json
$projects = @($report.projects | Where-Object { $null -ne $_ })
if ($null -eq $report -or $report.version -ne 1 -or $projects.Count -eq 0 -or
    @($projects | Where-Object { [string]::IsNullOrWhiteSpace($_.path) }).Count -gt 0) {
    throw 'NuGet dependency audit returned an empty or unsupported report.'
}

$auditErrors = @($report.logs | Where-Object { $_.level -in @('error', 'warning') })
if ($auditErrors.Count -gt 0) {
    throw "NuGet dependency audit could not complete: $(($auditErrors.message) -join '; ')"
}

$vulnerabilities = @(foreach ($project in $projects) {
    foreach ($framework in $project.frameworks) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            foreach ($vulnerability in $package.vulnerabilities) {
                "$($package.id) $($package.resolvedVersion): $($vulnerability.severity) $($vulnerability.advisoryurl)"
            }
        }
    }
})

if ($vulnerabilities.Count -gt 0) {
    throw "Vulnerable NuGet dependencies found:$([Environment]::NewLine)$(($vulnerabilities | Sort-Object -Unique) -join [Environment]::NewLine)"
}

Write-Output "backendDependencyAudit=passed; projects=$($projects.Count)"
