param([switch]$FastStartup, [string]$Version = '0.15.0', [ValidateSet('vlc','mpv','mpc')][string]$Engine = 'mpv', [switch]$NoRestore)
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
# Publish the isolated native recognition host beside the self-contained app runtime.
& dotnet publish (Join-Path $PSScriptRoot 'HN-IPTV.SpeechWorker/HN-IPTV.SpeechWorker.csproj') -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Speech worker publish failed.' }
# Runtime NuGet targets also copy other architectures. Ship only the Windows x64 variants.
$runtimeRoot = Join-Path $publish 'runtimes'
$publishRoot = [IO.Path]::GetFullPath($publish).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (Test-Path $runtimeRoot) {
    Get-ChildItem $runtimeRoot -Directory -Recurse | Where-Object {
        $_.Name -match '^(win-|linux-|osx-|android-|ios|maccatalyst|tvos|browser|freebsd)' -and $_.Name -ne 'win-x64'
    } | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object {
        $target = [IO.Path]::GetFullPath($_.FullName)
        if (-not $target.StartsWith($publishRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime path outside publish directory.' }
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}
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
