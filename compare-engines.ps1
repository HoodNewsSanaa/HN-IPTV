param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
function Median($values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { throw 'Missing comparison samples.' }
    if ($sorted.Count % 2) { return $sorted[[int][Math]::Floor($sorted.Count / 2)] }
    return ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2
}
$summary = [ordered]@{}
foreach ($engine in @('vlc', 'mpv')) {
    $runs = @(1..3 | ForEach-Object { Get-Content (Join-Path $Directory "$engine-$_.json") -Raw | ConvertFrom-Json })
    if (@($runs | Where-Object { -not $_.passed -or $_.engine -ne $engine }).Count) { throw "$engine runtime comparison did not pass." }
    $size = Get-Content (Join-Path $Directory "$engine-size.json") -Raw | ConvertFrom-Json
    $summary[$engine] = [ordered]@{
        successful_fixture_trials = $runs.Count
        startup_median_ms = Median @($runs.window_ready_ms)
        tune_progress_median_ms = Median @($runs | ForEach-Object { $_.time_to_progress_ms } )
        working_set_median_bytes = Median @($runs.working_set_bytes)
        private_median_bytes = Median @($runs.private_bytes)
        payload_bytes = $size.application_payload_bytes
    }
}
$summary['mpv_faster_in_fixture'] = $summary.mpv.tune_progress_median_ms -lt ($summary.vlc.tune_progress_median_ms * 0.9)
$summary['release_engine'] = 'vlc'
$summary['limits'] = 'Same-runner synthetic trials. Decode progress is not rendered first-frame latency. Real provider, GPU, long-session, audio and PiP visual checks remain necessary before promoting mpv. No automatic release-engine switch.'
$summary | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Directory 'comparison-summary.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 5 | Write-Host
