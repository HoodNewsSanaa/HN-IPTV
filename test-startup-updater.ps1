$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('hn-startup-update-test-' + [guid]::NewGuid().ToString('N'))
$recordBefore = $env:HN_IPTV_TEST_RECORD
$failBefore = $env:HN_IPTV_TEST_FAIL
try {
    New-Item -ItemType Directory $temporary | Out-Null
    $source = Get-Content (Join-Path $PSScriptRoot 'HN-IPTV/AppUpdater.cs') -Raw
    $match = [regex]::Match($source, '(?s)private const string InstallerScript="""\r?\n(.*?)\r?\n""";')
    if (-not $match.Success) { throw 'Installer helper source missing.' }
    $script = Join-Path $temporary 'install-update.ps1'
    [IO.File]::WriteAllText($script, $match.Groups[1].Value)
    $fixtureSource = Join-Path $temporary 'fixture.cs'
    [IO.File]::WriteAllText($fixtureSource, @'
using System;
using System.IO;
public static class InstallerFixture {
    public static int Main(string[] args) {
        File.WriteAllLines(Environment.GetEnvironmentVariable("HN_IPTV_TEST_RECORD"), args);
        return Environment.GetEnvironmentVariable("HN_IPTV_TEST_FAIL") == "1" ? 5 : 0;
    }
}
'@)
    $compile = Join-Path $temporary 'compile.ps1'
    [IO.File]::WriteAllText($compile, 'param($source,$output); Add-Type -Path $source -OutputAssembly $output -OutputType ConsoleApplication')
    $package = Join-Path $temporary 'installer-fixture.exe'
    $powershell = Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe'
    & $powershell -NoProfile -ExecutionPolicy Bypass -File $compile $fixtureSource $package
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile isolated installer fixture.' }
    $record = Join-Path $temporary 'arguments.txt'
    $failure = Join-Path $temporary 'failure.json'
    $env:HN_IPTV_TEST_RECORD = $record
    $env:HN_IPTV_TEST_FAIL = '0'
    $job = @{ package=$package; hash=(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash; process=2147483647; destination=$temporary; version='99.0.0'; failure=$failure }
    $job | ConvertTo-Json | Set-Content (Join-Path $temporary 'automatic.json')
    & $powershell -NoProfile -ExecutionPolicy Bypass -File $script -NoRestart
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $record)) { throw 'Verified installer was not run.' }
    $arguments = Get-Content $record -Raw
    if ($arguments -notmatch '/VERYSILENT' -or $arguments -notmatch '/NORESTART' -or $arguments -notmatch '/DIR=') { throw 'Automatic installer arguments are incorrect.' }
    $env:HN_IPTV_TEST_FAIL = '1'
    $ErrorActionPreference = 'Continue'
    & $powershell -NoProfile -ExecutionPolicy Bypass -File $script -NoRestart 2>$null
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -eq 0 -or -not (Test-Path $failure)) { throw 'Installer failure did not create retry cooldown.' }
    Remove-Item -LiteralPath $record
    Add-Content -LiteralPath $package -Value 'modified after download verification'
    $ErrorActionPreference = 'Continue'
    & $powershell -NoProfile -ExecutionPolicy Bypass -File $script -NoRestart 2>$null
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -eq 0 -or (Test-Path $record)) { throw 'Modified installer was executed.' }
    $global:LASTEXITCODE = 0
    Write-Host 'Automatic installer arguments, failure cooldown, and execution-time tamper rejection passed.'
} finally {
    $env:HN_IPTV_TEST_RECORD = $recordBefore
    $env:HN_IPTV_TEST_FAIL = $failBefore
    $target = [IO.Path]::GetFullPath($temporary)
    $allowed = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\hn-startup-update-test-'
    if (-not $target.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
}
