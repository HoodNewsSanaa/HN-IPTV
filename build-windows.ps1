param([switch]$FastStartup, [string]$Version = '0.9.0', [ValidateSet('vlc','mpv','mpc')][string]$Engine = 'vlc', [switch]$NoRestore)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch.' }
if ($Engine -eq 'mpv') { & (Join-Path $PSScriptRoot 'prepare-mpv.ps1') }
if ($Engine -eq 'mpc') { & (Join-Path $PSScriptRoot 'prepare-mpc.ps1') }
$publish = Join-Path $PSScriptRoot 'artifacts/publish'
if (Test-Path $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
$readyToRun = if ($FastStartup) { 'true' } else { 'false' }
[string[]]$restoreArguments = @()
if ($NoRestore) { $restoreArguments += '--no-restore' }
& dotnet publish (Join-Path $PSScriptRoot 'HN-IPTV/HN-IPTV.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false "-p:PublishReadyToRun=$readyToRun" `
    "-p:Version=$Version" "-p:PlaybackEngine=$Engine" @restoreArguments -p:DebugType=none -p:DebugSymbols=false -p:CopyOutputSymbolsToPublishDirectory=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Windows x64 publish failed.' }
# Incremental referenced-project builds may leave symbols from an earlier test
# configuration. Symbols are retained in build outputs, never release payloads.
Get-ChildItem $publish -Recurse -File -Filter '*.pdb' | Remove-Item -Force
Copy-Item (Join-Path $PSScriptRoot 'PORTABILITY.md'),(Join-Path $PSScriptRoot 'THIRD-PARTY.md') $publish
$licenseDirectory = Join-Path $publish 'licenses'
New-Item -ItemType Directory -Force $licenseDirectory | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'licenses/*') $licenseDirectory -Recurse -Force
$manifest = [ordered]@{ version=$Version; engine=$Engine; source_commit=$env:GITHUB_SHA;
    application_sha256=(Get-FileHash (Join-Path $publish 'HN-IPTV.dll') -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifest | ConvertTo-Json | Set-Content (Join-Path $publish 'build-manifest.json') -Encoding utf8
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PublishDirectory $publish -Engine $Engine
& (Join-Path $PSScriptRoot 'measure-publish.ps1') -PublishDirectory $publish
