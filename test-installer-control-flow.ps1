# Simulated process/registry regression checks. Not a real Windows installation test.
$ErrorActionPreference = 'Stop'
$subject = Join-Path $PSScriptRoot 'test-installer.ps1'
$tokens = $null; $parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile($subject, [ref]$tokens, [ref]$parseErrors) | Out-Null
if ($parseErrors.Count) { throw ($parseErrors | Out-String) }

function Test-Path {
    param([Parameter(Position = 0)][Alias('Path')][string]$LiteralPath)
    if ($LiteralPath.StartsWith('HKCU:', [StringComparison]::OrdinalIgnoreCase)) { return $global:HnInstallerRegressionState.Registered }
    if ($LiteralPath -eq $global:HnInstallerRegressionState.Uninstaller -and $global:HnInstallerRegressionState.Uninstalled) {
        $global:HnInstallerRegressionState.Probes++
        if ($global:HnInstallerRegressionState.Scenario -ne 'cleanup-timeout' -and $global:HnInstallerRegressionState.Probes -ge 3) {
            if (Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath) { Remove-Item -LiteralPath $LiteralPath -Force }
        }
    }
    return Microsoft.PowerShell.Management\Test-Path -LiteralPath $LiteralPath
}

function Start-Process {
    param([string]$FilePath, [string[]]$ArgumentList, [switch]$PassThru)
    $global:HnInstallerRegressionState.Starts++
    $code = 0; $timeout = $false
    if ([IO.Path]::GetFileName($FilePath) -eq 'HN-IPTV-Setup.exe') {
        $global:HnInstallerRegressionState.Installs++
        $argument = $ArgumentList | Where-Object { $_.StartsWith('/DIR=') } | Select-Object -First 1
        $folder = $argument.Substring(5).Trim('"')
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $folder 'HN-IPTV.exe') -Value 'fake app'
        $global:HnInstallerRegressionState.Uninstaller = Join-Path $folder 'unins000.exe'
        Set-Content -LiteralPath $global:HnInstallerRegressionState.Uninstaller -Value 'fake uninstaller'
        $global:HnInstallerRegressionState.Registered = $true
        if ($global:HnInstallerRegressionState.Scenario -eq 'reinstall-and-cleanup-fail' -and $global:HnInstallerRegressionState.Installs -eq 2) { $code = 7 }
        if ($global:HnInstallerRegressionState.Scenario -eq 'install-timeout') { $timeout = $true }
    } else {
        $global:HnInstallerRegressionState.Uninstalls++
        if ($global:HnInstallerRegressionState.Uninstalls -gt 1 -or $global:HnInstallerRegressionState.Scenario -in @('uninstall-fail', 'reinstall-and-cleanup-fail')) { $code = 1 }
        else {
            $global:HnInstallerRegressionState.Uninstalled = $true; $global:HnInstallerRegressionState.Registered = $false
            Remove-Item -LiteralPath (Join-Path (Split-Path $FilePath) 'HN-IPTV.exe') -Force
        }
    }
    $p = [pscustomobject]@{ ExitCode = $code; TimesOut = $timeout }
    $p | Add-Member ScriptMethod WaitForExit { param($milliseconds) return -not $this.TimesOut }
    $p | Add-Member ScriptMethod Kill { param($tree) $global:HnInstallerRegressionState.Killed = $true }
    $p | Add-Member ScriptMethod Dispose { }
    return $p
}

$previousState = Get-Variable -Name HnInstallerRegressionState -Scope Global -ErrorAction SilentlyContinue
$previousLocal = $env:LOCALAPPDATA; $previousTemp = $env:TEMP
$root = Join-Path ([IO.Path]::GetTempPath()) ('hn-installer-regression-' + [guid]::NewGuid().ToString('N'))
try {
    foreach ($scenario in @('success-delayed-cleanup', 'uninstall-fail', 'reinstall-and-cleanup-fail', 'cleanup-timeout', 'install-timeout', 'existing-data', 'existing-install')) {
        $fixture = Join-Path $root $scenario
        New-Item -ItemType Directory -Path (Join-Path $fixture 'dist') -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $fixture 'temp') -Force | Out-Null
        Copy-Item -LiteralPath $subject -Destination (Join-Path $fixture 'test-installer.ps1')
        Set-Content -LiteralPath (Join-Path $fixture 'dist/HN-IPTV-Setup.exe') -Value 'fake setup'
        $env:LOCALAPPDATA = Join-Path $fixture 'local'; $env:TEMP = Join-Path $fixture 'temp'
        $global:HnInstallerRegressionState = @{ Scenario = $scenario; Registered = ($scenario -eq 'existing-install'); Starts = 0; Installs = 0; Uninstalls = 0; Uninstalled = $false; Uninstaller = ''; Probes = 0; Killed = $false }
        $existing = Join-Path $env:LOCALAPPDATA 'HN-IPTV/preserve.txt'
        if ($scenario -eq 'existing-data') {
            New-Item -ItemType Directory -Path (Split-Path $existing) -Force | Out-Null
            Set-Content -LiteralPath $existing -Value 'must survive'
        }
        $output = [Collections.Generic.List[string]]::new(); $failure = $null
        try { & (Join-Path $fixture 'test-installer.ps1') -CleanupTimeoutSeconds 1 *>&1 | ForEach-Object { $output.Add($_.ToString()) } }
        catch { $failure = $_ }
        $successPrinted = ($output -join "`n") -match 'and cleanup passed'
        if ($scenario -eq 'success-delayed-cleanup') {
            if ($failure -or -not $successPrinted -or $global:HnInstallerRegressionState.Uninstalls -ne 1 -or $global:HnInstallerRegressionState.Probes -lt 3) { throw "Successful delayed cleanup regression failed: $failure" }
            if (Microsoft.PowerShell.Management\Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'HN-IPTV')) { throw 'Synthetic data was not cleaned.' }
        } else {
            if (-not $failure -or $successPrinted) { throw "Failure was incorrectly accepted: $scenario" }
            switch ($scenario) {
                'uninstall-fail' { if ($global:HnInstallerRegressionState.Uninstalls -ne 1 -or "$failure" -notmatch 'silent-uninstall returned exit code 1') { throw 'Uninstall failure was retried or hidden.' } }
                'reinstall-and-cleanup-fail' { if ($global:HnInstallerRegressionState.Uninstalls -ne 1 -or "$failure" -notmatch 'reinstall returned exit code 7') { throw 'Cleanup masked the original reinstall failure.' } }
                'cleanup-timeout' { if ($global:HnInstallerRegressionState.Uninstalls -ne 1 -or "$failure" -notmatch 'cleanup did not finish') { throw 'Residual uninstaller timeout was not enforced.' } }
                'install-timeout' { if (-not $global:HnInstallerRegressionState.Killed -or "$failure" -notmatch 'initial-install timed out') { throw 'Installer timeout was not preserved.' } }
                'existing-data' { if ($global:HnInstallerRegressionState.Starts -ne 0 -or (Get-Content -LiteralPath $existing) -ne 'must survive') { throw 'Existing user data was touched.' } }
                'existing-install' { if ($global:HnInstallerRegressionState.Starts -ne 0 -or "$failure" -notmatch 'existing HN-IPTV installation') { throw 'Existing installation was not protected.' } }
            }
        }
        Write-Host "Passed: $scenario"
    }
} finally {
    $env:LOCALAPPDATA = $previousLocal; $env:TEMP = $previousTemp
    if ($previousState) { $global:HnInstallerRegressionState = $previousState.Value }
    else { Remove-Variable -Name HnInstallerRegressionState -Scope Global -ErrorAction SilentlyContinue }
    if (Microsoft.PowerShell.Management\Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
Write-Host 'Installer orchestration regressions passed (simulated processes/registry; not a Windows installation test).'
