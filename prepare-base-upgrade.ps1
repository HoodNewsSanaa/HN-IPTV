$ErrorActionPreference = 'Stop'
$version = '0.10.0'
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/HoodNewsSanaa/HN-IPTV/releases/tags/v$version" -Headers @{ 'User-Agent' = 'HN-IPTV-upgrade-validation' }
$asset = $release.assets | Where-Object name -eq 'HN-IPTV-Setup.exe' | Select-Object -First 1
$match = [regex]::Match($release.body, '(?im)^([a-f0-9]{64})[ \t]+HN-IPTV-Setup\.exe\r?$')
if (-not $asset -or -not $match.Success) { throw 'Stable installer or checksum is missing.' }
$folder = Join-Path $PSScriptRoot 'artifacts/upgrade-fixture'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$path = Join-Path $folder 'HN-IPTV-Previous-Setup.exe'
if (-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $match.Groups[1].Value) {
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $path
}
if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $match.Groups[1].Value) {
    throw 'Stable installer checksum mismatch.'
}
Write-Host "Verified actual stable v$version installer for in-place feature upgrade."
