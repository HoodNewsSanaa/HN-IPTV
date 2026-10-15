param([string]$PublishDirectory = (Join-Path $PSScriptRoot 'artifacts/publish'),
      [string]$Report = (Join-Path $PSScriptRoot 'artifacts/windows-smoke.json'))
$ErrorActionPreference = 'Stop'
$publish = [IO.Path]::GetFullPath($PublishDirectory)
$harness = Join-Path $PSScriptRoot 'artifacts/windows-harness'
if (Test-Path $harness) { Remove-Item $harness -Recurse -Force }
# Reference the published app, not a new project-reference build of the app.
& dotnet publish (Join-Path $PSScriptRoot 'HN-IPTV.Windows.SmokeTests/HN-IPTV.Windows.SmokeTests.csproj') `
    -c Release -r win-x64 --self-contained true "-p:PublishedAppPath=$publish" -o $harness -m:1
if ($LASTEXITCODE -ne 0) { throw 'Windows test harness compilation failed.' }
$expected = (Get-FileHash (Join-Path $publish 'HN-IPTV.dll') -Algorithm SHA256).Hash
$actual = (Get-FileHash (Join-Path $harness 'HN-IPTV.dll') -Algorithm SHA256).Hash
if ($expected -ne $actual) { throw 'Windows harness is not testing the published assembly.' }
& (Join-Path $harness 'HN-IPTV.Windows.SmokeTests.exe') ([IO.Path]::GetFullPath($Report))
if ($LASTEXITCODE -ne 0) { throw 'Windows playback/UI checks failed; inspect the test report.' }
$result = Get-Content $Report -Raw | ConvertFrom-Json
if (-not $result.passed -or $result.app_sha256 -ne $expected.ToLowerInvariant()) { throw 'Windows report does not validate the published assembly.' }
