param(
    [Parameter(Mandatory = $true)] [string] $PayloadDirectory,
    [Parameter(Mandatory = $true)] [string] $LauncherExecutable,
    [Parameter(Mandatory = $true)] [string] $OutputFile
)

$ErrorActionPreference = "Stop"
$allowedLanguages = @("zh-CN", "en-US")

Get-ChildItem -Path $PayloadDirectory -Directory |
    Where-Object {
        try {
            [void][Globalization.CultureInfo]::GetCultureInfo($_.Name)
            $_.Name -notin $allowedLanguages
        }
        catch {
            $false
        }
    } |
    Remove-Item -Recurse -Force

$workDirectory = Join-Path ([IO.Path]::GetTempPath()) ("SteamLoginLite-" + [Guid]::NewGuid().ToString("N"))
$payloadZip = Join-Path $workDirectory "payload.zip"
New-Item -ItemType Directory -Force -Path $workDirectory | Out-Null

try {
    Compress-Archive -Path (Join-Path $PayloadDirectory "*") -DestinationPath $payloadZip -CompressionLevel Optimal
    $hash = Get-FileHash -Path $payloadZip -Algorithm SHA256
    $hashBytes = [Convert]::FromHexString($hash.Hash)
    $lengthBytes = [BitConverter]::GetBytes([Int64](Get-Item $payloadZip).Length)
    $magicBytes = [Text.Encoding]::ASCII.GetBytes("SLPAY001")

    $outputDirectory = Split-Path -Parent $OutputFile
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

    $destination = [IO.File]::Open($OutputFile, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        foreach ($sourcePath in @($LauncherExecutable, $payloadZip)) {
            $source = [IO.File]::OpenRead($sourcePath)
            try { $source.CopyTo($destination) } finally { $source.Dispose() }
        }
        $destination.Write($hashBytes, 0, $hashBytes.Length)
        $destination.Write($lengthBytes, 0, $lengthBytes.Length)
        $destination.Write($magicBytes, 0, $magicBytes.Length)
    }
    finally {
        $destination.Dispose()
    }
}
finally {
    if (Test-Path $workDirectory) { Remove-Item $workDirectory -Recurse -Force }
}
