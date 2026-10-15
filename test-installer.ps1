# Run only on an expendable Windows test account. Existing installs/data are refused.
param([ValidateRange(1, 120)][int]$CleanupTimeoutSeconds = 30)
$ErrorActionPreference = 'Stop'
$setup = Join-Path $PSScriptRoot 'dist/HN-IPTV-Setup.exe'
$data = Join-Path $env:LOCALAPPDATA 'HN-IPTV'
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{CD93EEDF-5518-4324-92BD-06600458F64C}_is1'
if (Test-Path -LiteralPath $data) { throw 'Installer test requires an account without existing HN-IPTV data.' }
if (Test-Path -LiteralPath $registration) { throw 'Installer test requires an account without an existing HN-IPTV installation.' }
if (-not (Test-Path -LiteralPath $setup)) { throw 'Build dist/HN-IPTV-Setup.exe before running the installer test.' }
$install = Join-Path $env:TEMP ('HN-IPTV-installer-test-' + [guid]::NewGuid().ToString('N'))
$uninstaller = Join-Path $install 'unins000.exe'
$logs = Join-Path $PSScriptRoot 'artifacts/installer-test'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$uninstallAttempted = $false
$uninstallCompleted = $false
$ownsTestData = $false
$testFailure = $null
$cleanupFailures = @()

function Run-Installer([string]$exe, [string[]]$arguments, [string]$stage) {
    $log = Join-Path $logs ($stage + '.log')
    Write-Host "Installer test: $stage"
    $process = Start-Process -FilePath $exe -ArgumentList ($arguments + "/LOG=`"$log`"") -PassThru
    try {
        if (-not $process.WaitForExit(120000)) {
            $process.Kill($true)
            throw "$stage timed out. See artifacts/installer-test/$stage.log."
        }
        if ($process.ExitCode -ne 0) {
            throw "$stage returned exit code $($process.ExitCode). See artifacts/installer-test/$stage.log."
        }
    } finally { $process.Dispose() }
}

function Wait-UninstallerCleanup {
    # Inno's temporary clone can still be deleting files after the original exits.
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (Test-Path -LiteralPath $uninstaller) {
        if ($clock.Elapsed.TotalSeconds -ge $CleanupTimeoutSeconds) {
            throw "Uninstaller cleanup did not finish within $CleanupTimeoutSeconds seconds."
        }
        Start-Sleep -Milliseconds 200
    }
}

function Remove-TestPath([string]$path) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while (Test-Path -LiteralPath $path) {
        try { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop; return }
        catch {
            if ($clock.Elapsed.TotalSeconds -ge $CleanupTimeoutSeconds) { throw }
            Start-Sleep -Milliseconds 200
        }
    }
}

try {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$install`"")
    Run-Installer $setup $arguments 'initial-install'
    if (-not (Test-Path -LiteralPath (Join-Path $install 'HN-IPTV.exe'))) { throw 'Installed EXE missing.' }
    if (-not (Test-Path -LiteralPath $registration)) { throw 'Installed Apps registration missing.' }
    New-Item -ItemType Directory -Path $data | Out-Null
    $ownsTestData = $true
    $sentinel = Join-Path $data 'upgrade-test-sentinel.txt'
    Set-Content -LiteralPath $sentinel -Value 'synthetic test data'
    $hash = (Get-FileHash -LiteralPath $sentinel).Hash
    Run-Installer $setup $arguments 'reinstall'
    if ((Get-FileHash -LiteralPath $sentinel).Hash -ne $hash) { throw 'Reinstall changed user data.' }

    # Record the attempt before launching: even a failed uninstall must not be rerun.
    $uninstallAttempted = $true
    Run-Installer $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') 'silent-uninstall'
    $uninstallCompleted = $true
    Wait-UninstallerCleanup
    if ((Get-FileHash -LiteralPath $sentinel).Hash -ne $hash) { throw 'Silent uninstall did not preserve data.' }
    if (Test-Path -LiteralPath (Join-Path $install 'HN-IPTV.exe')) { throw 'Uninstaller left the application behind.' }
    if (Test-Path -LiteralPath $registration) { throw 'Uninstaller left Installed Apps registration behind.' }
} catch { $testFailure = $_ }
finally {
    try {
        if (-not $uninstallAttempted -and (Test-Path -LiteralPath $uninstaller)) {
            $uninstallAttempted = $true
            Run-Installer $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') 'cleanup-uninstall'
            $uninstallCompleted = $true
        }
        if ($uninstallCompleted) {
            Wait-UninstallerCleanup
            Remove-TestPath $install
        }
    } catch { $cleanupFailures += $_ }
    # Remove only the synthetic data this invocation successfully created.
    try { if ($ownsTestData) { Remove-TestPath $data } }
    catch { $cleanupFailures += $_ }
}

# Cleanup must not hide the original validation error or print a false success.
if ($testFailure) {
    foreach ($failure in $cleanupFailures) { Write-Warning "Cleanup also failed: $failure" }
    throw $testFailure
}
if ($cleanupFailures.Count -gt 0) { throw $cleanupFailures[0] }
Write-Host 'Install, registration, same-version reinstall, silent uninstall, and cleanup passed. Interactive data deletion remains a manual check.'
