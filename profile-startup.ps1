param([Parameter(Mandatory)][string]$Executable, [int]$Iterations = 5,
      [string]$ReportPath = (Join-Path $PSScriptRoot 'artifacts/startup-report.json'))
$ErrorActionPreference = 'Stop'
$rows = @()
for ($run = 0; $run -lt $Iterations; $run++) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath (Resolve-Path $Executable) -PassThru
    while ($true) {
        Start-Sleep -Milliseconds 25
        $process.Refresh()
        if ($process.HasExited) { throw 'Application exited before showing its main window.' }
        if ($process.MainWindowHandle -ne 0 -and $process.MainWindowTitle -eq 'HN-IPTV' -and $process.Responding) { break }
        if ($clock.Elapsed.TotalSeconds -gt 60) { throw 'No responsive HN-IPTV main window within 60 seconds. Process left running for inspection.' }
    }
    $windowMs = $clock.Elapsed.TotalMilliseconds
    Start-Sleep -Seconds 3
    $process.Refresh()
    $rows += [pscustomobject]@{ run=$run; window_ms=$windowMs; working_set_bytes=$process.WorkingSet64; private_bytes=$process.PrivateMemorySize64 }
    $process.CloseMainWindow() | Out-Null
    if (-not $process.WaitForExit(30000)) { throw 'Application did not finish saving/closing. Process left running for inspection.' }
}
New-Item -ItemType Directory -Force (Split-Path $ReportPath) | Out-Null
$rows | ConvertTo-Json | Set-Content $ReportPath -Encoding utf8
$rows | Format-Table
Write-Host 'Use a disposable Windows test account and identical synthetic data for both versions. Separate cold launch from later warm samples. This measures window availability, not provider download completion.'
