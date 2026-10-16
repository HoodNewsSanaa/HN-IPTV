param([Parameter(Mandatory)][string]$Directory)
$ErrorActionPreference = 'Stop'
function Median($values) {
    $sorted = @($values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    if ($sorted.Count % 2) { return $sorted[[int][Math]::Floor($sorted.Count / 2)] }
    return ($sorted[$sorted.Count / 2 - 1] + $sorted[$sorted.Count / 2]) / 2
}
$summary = [ordered]@{}
foreach ($engine in @('vlc', 'mpv', 'mpc')) {
    $runs = @(Get-ChildItem $Directory -Filter "$engine-*.json" | Where-Object { $_.BaseName -match '-[123]$' } | ForEach-Object { Get-Content $_.FullName -Raw | ConvertFrom-Json })
    $valid = @($runs | Where-Object { $_.passed -and $_.engine -eq $engine })
    $sizePath = Join-Path $Directory "$engine-size.json"
    $size = if (Test-Path $sizePath) { Get-Content $sizePath -Raw | ConvertFrom-Json } else { $null }
    $summary[$engine] = [ordered]@{
        successful_fixture_trials = $valid.Count
        failed_fixture_trials = @($runs | Where-Object { -not $_.passed }).Count
        startup_median_ms = Median @($valid.window_ready_ms)
        initial_tune_progress_median_ms = Median @($valid.initial_tune_progress_ms)
        tune_progress_median_ms = Median @($valid | ForEach-Object { $_.time_to_progress_ms })
        total_working_set_median_bytes = Median @($valid | ForEach-Object { $_.working_set_bytes + $_.child_working_set_bytes })
        total_private_median_bytes = Median @($valid | ForEach-Object { $_.private_bytes + $_.child_private_bytes })
        total_cpu_median_ms = Median @($valid | ForEach-Object { $_.cpu_time_ms + $_.child_cpu_ms })
        payload_bytes = $size.application_payload_bytes
    }
}
$summary['release_engine'] = 'mpv'
$summary['limits'] = 'Same-runner synthetic trials. Playback progress is not measured first visible frame. MPC includes its child process but lacks stream telemetry and equivalent buffer control. Real provider, physical GPU, audible audio, long sessions, DPI and PiP visual checks remain necessary. Failed trials are retained, not counted as speed wins.'
$summary | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Directory 'comparison-summary.json') -Encoding utf8
$summary | ConvertTo-Json -Depth 5 | Write-Host
if ($summary.vlc.successful_fixture_trials -ne 3) { throw 'VLC baseline failed one or more comparison trials.' }
