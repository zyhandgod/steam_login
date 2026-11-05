$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Push-Location $repo
try {
    dotnet build SteamLoginLite.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw '原生程序构建失败。' }
    [xml]$project = Get-Content -LiteralPath 'SteamLoginLite.csproj' -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号无效。' }
    $output = Join-Path $repo 'bin/Release/net48'
    $release = Join-Path $repo 'release'
    [IO.Directory]::CreateDirectory($release) | Out-Null
    $stage = Join-Path $release ('.native-stage-' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    try {
        $files = @('SteamLoginLite.exe', 'SteamLoginLite.exe.config', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll')
        foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $output $file) -Destination $stage }
        Copy-Item -LiteralPath (Join-Path $repo '使用说明.txt') -Destination $stage
        $zip = Join-Path $release "SteamLoginLite-$version-win-x64.zip"
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            if ($archive.Entries.Count -ne 6) { throw '绿色包必须只包含 6 个文件。' }
            if ($archive.Entries | Where-Object { $_.FullName -match '[/\\]|node_modules|asar|chrome|electron|accounts\.json' }) { throw '包内包含非交付内容。' }
            $expanded = ($archive.Entries | Measure-Object -Property Length -Sum).Sum
            if ($expanded -gt 5MB) { throw '轻量包解压体积异常，超过 5MB。' }
            Write-Host "PASS: 6 files, $expanded expanded bytes. $zip"
        } finally { $archive.Dispose() }
    } finally { if ([IO.Directory]::Exists($stage)) { [IO.Directory]::Delete($stage, $true) } }
} finally { Pop-Location }
