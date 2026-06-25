param(
    [string]$RepositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'

$repositoryRootFullPath = (Resolve-Path -LiteralPath $RepositoryRoot).Path

Push-Location -LiteralPath $repositoryRootFullPath
try {
    $gitRoot = (& git rev-parse --show-toplevel).Trim()
    if ([string]::IsNullOrWhiteSpace($gitRoot)) {
        throw 'Git repository root was not detected.'
    }

    $gitRootFullPath = (Resolve-Path -LiteralPath $gitRoot).Path
    if ($gitRootFullPath -ne $repositoryRootFullPath) {
        throw "Unexpected Git root: $gitRootFullPath"
    }

    $trackedFiles = @(& git ls-files)
    $untrackedFiles = @(& git ls-files --others --exclude-standard)
    $candidateFiles = @($trackedFiles + $untrackedFiles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)

    $sensitivePathPatterns = @(
        '(^|/)\.env($|\.)',
        '(^|/)appsettings\.Local\.json$',
        '\.(accdb|mdb|pgdump|dump|backup|bak|db|sqlite|sqlite3)$',
        '\.sql\.gz$',
        '(^|/)(artifacts|backups|dumps|local-db-backups|private-imports)(/|$)',
        '(^|/)imports/(private|raw)(/|$)'
    )

    $allowedPatterns = @(
        '(^|/)\.env\.example$'
    )

    $violations = foreach ($file in $candidateFiles) {
        $normalized = $file.Replace('\', '/')
        $isAllowed = $allowedPatterns | Where-Object { $normalized -match $_ } | Select-Object -First 1
        if ($isAllowed) {
            continue
        }

        $matchedPattern = $sensitivePathPatterns | Where-Object { $normalized -match $_ } | Select-Object -First 1
        if ($matchedPattern) {
            $normalized
        }
    }

    if ($violations.Count -gt 0) {
        $list = ($violations | Sort-Object -Unique) -join [Environment]::NewLine
        throw "Privacy check failed. Sensitive files are tracked or visible to Git:$([Environment]::NewLine)$list"
    }

    Write-Output "privacyCheck=passed; scannedFiles=$($candidateFiles.Count)"
}
finally {
    Pop-Location
}
