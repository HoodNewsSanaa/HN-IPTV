$ErrorActionPreference = 'Stop'
$manifest = Get-Content (Join-Path $PSScriptRoot 'mpc-build.json') -Raw | ConvertFrom-Json
$destination = Join-Path $PSScriptRoot 'native/mpc-hc'
$stamp = Join-Path $destination 'archive.sha256'
if ((Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq $manifest.archive_sha256) -and (Test-Path (Join-Path $destination 'mpc-hc64.exe'))) { return }
$temp = Join-Path ([IO.Path]::GetTempPath()) ('hn-mpc-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $zip = Join-Path $temp 'mpc.zip'
    Invoke-WebRequest -Uri $manifest.url -OutFile $zip
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $manifest.archive_sha256) { throw 'MPC-HC download checksum mismatch.' }
    Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $temp 'payload')
    if (Test-Path $destination) { Remove-Item $destination -Recurse -Force }
    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
    Move-Item (Join-Path $temp 'payload') $destination
    # Existence selects portable settings rather than the user's registry profile.
    Set-Content (Join-Path $destination 'mpc-hc64.ini') '[Settings]' -Encoding unicode
    Set-Content $stamp $manifest.archive_sha256 -Encoding ascii
} finally { Remove-Item $temp -Recurse -Force }
