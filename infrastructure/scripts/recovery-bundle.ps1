[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Protect", "Unprotect")]
    [string]$Mode,
    [Parameter(Mandatory = $true)]
    [string]$EncryptionKeyFile,
    [string]$KeyRingDirectory,
    [string[]]$NonSecretConfigFiles = @(),
    [string]$BundleFile,
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$magic = [System.Text.Encoding]::ASCII.GetBytes("GBRB1")

function Read-RecoveryKey {
    if (-not (Test-Path -LiteralPath $EncryptionKeyFile -PathType Leaf)) {
        throw "Encryption key file was not found."
    }
    $text = (Get-Content -LiteralPath $EncryptionKeyFile -Raw).Trim()
    try { $key = [Convert]::FromBase64String($text) } catch { throw "Encryption key must be a base64-encoded 32-byte random value." }
    if ($key.Length -ne 32) { throw "Encryption key must contain exactly 32 bytes." }
    return $key
}

function Assert-SafeConfigFile([string]$Path) {
    $name = [System.IO.Path]::GetFileName($Path)
    if ($name -match "(?i)^\.env" -or $name -match "(?i)\.(pfx|p12|key|pem|pgpass)$") {
        throw "Secret-bearing files cannot be included in a recovery bundle."
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Config file was not found: $name" }
    $content = Get-Content -LiteralPath $Path -Raw
    if ($content -match '(?im)"(?:password|secret|signingkey|accesskey|secretkey|connectionstring[^\"]*)"\s*:\s*"[^\"]+"') {
        throw "Config file contains an inline sensitive value; include only secret references."
    }
}

$key = Read-RecoveryKey
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("garagebalance-recovery-" + [Guid]::NewGuid().ToString("N"))
$temporaryZip = "$temporaryRoot.zip"

try {
    if ($Mode -eq "Protect") {
        if (-not $KeyRingDirectory -or -not (Test-Path -LiteralPath $KeyRingDirectory -PathType Container)) {
            throw "KeyRingDirectory is required and must exist in Protect mode."
        }
        if (-not $BundleFile) { throw "BundleFile is required in Protect mode." }
        if ([System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($BundleFile)) -eq [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($EncryptionKeyFile))) {
            throw "Store the encryption key independently from the encrypted recovery bundle."
        }
        New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
        $keyRingTarget = Join-Path $temporaryRoot "key-ring"
        New-Item -ItemType Directory -Path $keyRingTarget | Out-Null
        Get-ChildItem -LiteralPath $KeyRingDirectory -Force | Copy-Item -Destination $keyRingTarget -Recurse -Force
        $configTarget = Join-Path $temporaryRoot "config"
        New-Item -ItemType Directory -Path $configTarget | Out-Null
        foreach ($configFile in $NonSecretConfigFiles) {
            Assert-SafeConfigFile $configFile
            Copy-Item -LiteralPath $configFile -Destination (Join-Path $configTarget ([System.IO.Path]::GetFileName($configFile))) -Force
        }
        $entries = Get-ChildItem -LiteralPath $temporaryRoot -File -Recurse | ForEach-Object {
            [ordered]@{
                path = [System.IO.Path]::GetRelativePath($temporaryRoot, $_.FullName).Replace("\", "/")
                sizeBytes = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
        [ordered]@{ schemaVersion = 1; createdAtUtc = [DateTimeOffset]::UtcNow; entries = @($entries) } |
            ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $temporaryRoot "manifest.json") -Encoding utf8NoBOM
        Compress-Archive -Path (Join-Path $temporaryRoot "*") -DestinationPath $temporaryZip -CompressionLevel Optimal
        $plain = [System.IO.File]::ReadAllBytes($temporaryZip)
        $nonce = [byte[]]::new(12)
        [System.Security.Cryptography.RandomNumberGenerator]::Fill($nonce)
        $tag = [byte[]]::new(16)
        $cipher = [byte[]]::new($plain.Length)
        $aes = [System.Security.Cryptography.AesGcm]::new($key, 16)
        try { $aes.Encrypt($nonce, $plain, $cipher, $tag, $magic) } finally { $aes.Dispose() }
        $parent = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($BundleFile))
        [System.IO.Directory]::CreateDirectory($parent) | Out-Null
        $stream = [System.IO.File]::Create([System.IO.Path]::GetFullPath($BundleFile))
        try { $stream.Write($magic); $stream.Write($nonce); $stream.Write($tag); $stream.Write($cipher) } finally { $stream.Dispose() }
        Write-Output "recoveryBundleProtected=True"
        Write-Output "bundleSha256=$((Get-FileHash -LiteralPath $BundleFile -Algorithm SHA256).Hash.ToLowerInvariant())"
    } else {
        if (-not $BundleFile -or -not (Test-Path -LiteralPath $BundleFile -PathType Leaf)) { throw "BundleFile is required and must exist in Unprotect mode." }
        if (-not $OutputDirectory) { throw "OutputDirectory is required in Unprotect mode." }
        if (Test-Path -LiteralPath $OutputDirectory) { throw "OutputDirectory must not already exist." }
        $bytes = [System.IO.File]::ReadAllBytes([System.IO.Path]::GetFullPath($BundleFile))
        if ($bytes.Length -lt 34 -or [System.Text.Encoding]::ASCII.GetString($bytes, 0, 5) -ne "GBRB1") { throw "Recovery bundle header is invalid." }
        $nonce = $bytes[5..16]
        $tag = $bytes[17..32]
        $cipher = $bytes[33..($bytes.Length - 1)]
        $plain = [byte[]]::new($cipher.Length)
        $aes = [System.Security.Cryptography.AesGcm]::new($key, 16)
        try { $aes.Decrypt($nonce, $cipher, $tag, $plain, $magic) } finally { $aes.Dispose() }
        [System.IO.File]::WriteAllBytes($temporaryZip, $plain)
        Expand-Archive -LiteralPath $temporaryZip -DestinationPath $OutputDirectory
        Write-Output "recoveryBundleRestored=True"
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
    if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip -Force }
    if ($key) { [System.Array]::Clear($key, 0, $key.Length) }
}
