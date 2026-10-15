param([switch]$FastStartup, [string]$Version = '0.7.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch.' }
$publish = Join-Path $PSScriptRoot 'artifacts/publish'
if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
$readyToRun = if ($FastStartup) { 'true' } else { 'false' }
& dotnet publish (Join-Path $PSScriptRoot 'HN-IPTV/HN-IPTV.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false "-p:PublishReadyToRun=$readyToRun" `
    "-p:Version=$Version" -p:DebugType=none -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Windows x64 publish failed.' }
Copy-Item (Join-Path $PSScriptRoot 'PORTABILITY.md'),(Join-Path $PSScriptRoot 'THIRD-PARTY.md') $publish
Copy-Item (Join-Path $PSScriptRoot 'licenses') $publish -Recurse
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PublishDirectory $publish
& (Join-Path $PSScriptRoot 'measure-publish.ps1') -PublishDirectory $publish
