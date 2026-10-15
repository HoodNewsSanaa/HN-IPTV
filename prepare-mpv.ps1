$ErrorActionPreference = 'Stop'
$manifest = Get-Content (Join-Path $PSScriptRoot 'mpv-build.json') -Raw | ConvertFrom-Json
$destination = Join-Path $PSScriptRoot 'native/mpv'
$dll = Join-Path $destination 'libmpv-2.dll'
if ((Test-Path $dll) -and (Get-FileHash $dll -Algorithm SHA256).Hash -eq $manifest.dll_sha256) { return }
$sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
if (-not $sevenZip) { $sevenZip = Get-Command 7zz -ErrorAction SilentlyContinue }
if (-not $sevenZip) { throw '7-Zip is required to prepare the optional mpv comparison build.' }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('hn-mpv-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $archive = Join-Path $temporary 'mpv.7z'
    Invoke-WebRequest -Uri $manifest.url -OutFile $archive
    if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne $manifest.archive_sha256) { throw 'mpv download checksum mismatch.' }
    & $sevenZip.Source x -y "-o$temporary" $archive 'libmpv-2.dll' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'mpv extraction failed.' }
    $extracted = Join-Path $temporary 'libmpv-2.dll'
    if ((Get-FileHash $extracted -Algorithm SHA256).Hash -ne $manifest.dll_sha256) { throw 'mpv DLL checksum mismatch.' }
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item -LiteralPath $extracted -Destination $dll -Force
} finally { Remove-Item -LiteralPath $temporary -Recurse -Force }
