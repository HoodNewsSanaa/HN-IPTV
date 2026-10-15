param([switch]$PortableOnly, [switch]$FastStartup, [string]$Version = '0.7.1')
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-windows.ps1') -FastStartup:$FastStartup -Version $Version
$publish = Join-Path $PSScriptRoot 'artifacts/publish'
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
    [IO.Compression.ZipFile]::CreateFromDirectory($folder, (Join-Path $dist 'HN-IPTV-Portable.zip'),
        [IO.Compression.CompressionLevel]::SmallestSize, $true)
} finally { if (Test-Path $temp) { Remove-Item -LiteralPath $temp -Recurse -Force } }
if (-not $PortableOnly) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $iscc = if ($command) { $command.Source } elseif (${env:ProgramFiles(x86)}) {
        Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
    } else { '' }
    if (-not $iscc -or -not (Test-Path $iscc)) { throw 'Inno Setup 6 is required for the installer. The portable ZIP is available.' }
    & $iscc "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'installer/HN-IPTV.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
}
$names = @('HN-IPTV-Portable.zip')
if (-not $PortableOnly) { $names = @('HN-IPTV-Setup.exe') + $names }
$lines = foreach ($name in $names) { "{0}  {1}" -f (Get-FileHash (Join-Path $dist $name) -Algorithm SHA256).Hash.ToLowerInvariant(), $name }
Set-Content (Join-Path $dist 'SHA256SUMS.txt') $lines -Encoding ascii
& (Join-Path $PSScriptRoot 'measure-publish.ps1') -PublishDirectory $publish -DistDirectory $dist
