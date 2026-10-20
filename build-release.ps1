param([switch]$PortableOnly, [switch]$FastStartup, [string]$Version = '0.10.0', [switch]$UseExistingPublish, [switch]$QuickCompression, [ValidateSet('vlc','mpv')][string]$Engine = 'mpv')
$ErrorActionPreference = 'Stop'
if (-not $UseExistingPublish) {
    & (Join-Path $PSScriptRoot 'build-windows.ps1') -FastStartup:$FastStartup -Version $Version -Engine $Engine
}
$publish = Join-Path $PSScriptRoot 'artifacts/publish'
$manifest = Get-Content (Join-Path $publish 'build-manifest.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne $Version -or $manifest.engine -ne $Engine) { throw 'Expected the requested playback engine and release version.' }
if ((Get-FileHash (Join-Path $publish 'HN-IPTV.dll') -Algorithm SHA256).Hash -ne $manifest.application_sha256) { throw 'Published assembly differs from its build manifest.' }
if ($env:GITHUB_SHA -and $manifest.source_commit -ne $env:GITHUB_SHA) { throw 'Published source commit differs from this workflow.' }
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PublishDirectory $publish -Engine $Engine
$dist = Join-Path $PSScriptRoot 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
# Never include stale setup/checksums from a previous or portable-only build.
foreach ($name in @('HN-IPTV-Setup.exe','HN-IPTV-Portable.zip','SHA256SUMS.txt')) {
    $path = Join-Path $dist $name
    if (Test-Path $path) { Remove-Item -LiteralPath $path -Force }
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ('hn-iptv-package-' + [guid]::NewGuid().ToString('N'))
try {
    $folder = Join-Path $temp 'HN-IPTV-Portable'
    New-Item -ItemType Directory -Force $temp | Out-Null
    Copy-Item -LiteralPath $publish -Destination $folder -Recurse
    New-Item -ItemType File -Path (Join-Path $folder 'portable.mode') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $folder 'data') | Out-Null
    $compression = if ($QuickCompression) { [IO.Compression.CompressionLevel]::Fastest } else { [IO.Compression.CompressionLevel]::SmallestSize }
    $stagedZip = Join-Path $dist ('.portable-' + [guid]::NewGuid().ToString('N') + '.zip')
    try {
        [IO.Compression.ZipFile]::CreateFromDirectory($folder, $stagedZip, $compression, $true)
        [IO.File]::Move($stagedZip, (Join-Path $dist 'HN-IPTV-Portable.zip'), $true)
    } finally { if (Test-Path $stagedZip) { Remove-Item -LiteralPath $stagedZip -Force } }
} finally { if (Test-Path $temp) { Remove-Item -LiteralPath $temp -Recurse -Force } }
if (-not $PortableOnly) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $iscc = if ($command) { $command.Source } elseif (${env:ProgramFiles(x86)}) {
        Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
    } else { '' }
    if (-not $iscc -or -not (Test-Path $iscc)) { throw 'Inno Setup 6 is required for the installer. The portable ZIP is available.' }
    [string[]]$compressionArguments = @()
    if ($QuickCompression) { $compressionArguments += '/DQuickCompression' }
    & $iscc @compressionArguments "/DAppVersion=$Version" "/DPlaybackEngine=$Engine" (Join-Path $PSScriptRoot 'installer/HN-IPTV.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
}
$names = @('HN-IPTV-Portable.zip')
if (-not $PortableOnly) { $names = @('HN-IPTV-Setup.exe') + $names }
$lines = foreach ($name in $names) { "{0}  {1}" -f (Get-FileHash (Join-Path $dist $name) -Algorithm SHA256).Hash.ToLowerInvariant(), $name }
Set-Content (Join-Path $dist 'SHA256SUMS.txt') $lines -Encoding ascii
& (Join-Path $PSScriptRoot 'measure-publish.ps1') -PublishDirectory $publish -DistDirectory $dist
