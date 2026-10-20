$ErrorActionPreference = 'Stop'
$sourceText = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'HN-IPTV/AppUpdater.cs') -Raw
$match = [regex]::Match($sourceText, '(?s)private const string PortableScript = """\r?\n(.*?)\r?\n""";')
if (-not $match.Success) { throw 'Portable updater script was not found.' }
$root = Join-Path ([IO.Path]::GetTempPath()) ('hn-updater-test-' + [guid]::NewGuid().ToString('N'))
$resolvedRoot = [IO.Path]::GetFullPath($root)
New-Item -ItemType Directory -Path $root | Out-Null
$lock = $null
try {
    $script = Join-Path $root 'apply-update.ps1'
    Set-Content -LiteralPath $script -Value $match.Groups[1].Value
    $source = Join-Path $root 'source'
    $destination = Join-Path $root 'destination'
    $backup = Join-Path $root 'backup'
    New-Item -ItemType Directory -Path $source,$destination,(Join-Path $source 'data'),(Join-Path $destination 'data') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $source 'HN-IPTV.exe') -Value 'new executable fixture'
    Set-Content -LiteralPath (Join-Path $destination 'HN-IPTV.exe') -Value 'old executable fixture'
    Set-Content -LiteralPath (Join-Path $source 'data/settings.json') -Value 'package data must never replace user data'
    Set-Content -LiteralPath (Join-Path $destination 'data/settings.json') -Value 'user settings'
    @{ source=$source; destination=$destination; backup=$backup; process=2147483646 } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'update.json')
    & $script -NoRestart
    if ((Get-Content -LiteralPath (Join-Path $destination 'HN-IPTV.exe') -Raw).Trim() -ne 'new executable fixture') { throw 'Application files were not updated.' }
    if ((Get-Content -LiteralPath (Join-Path $destination 'data/settings.json') -Raw).Trim() -ne 'user settings') { throw 'User data was overwritten.' }
    # The first file is replaced, then the locked second file forces rollback.
    Set-Content -LiteralPath (Join-Path $source 'zz-locked.dll') -Value 'new library fixture'
    Set-Content -LiteralPath (Join-Path $destination 'zz-locked.dll') -Value 'old library fixture'
    Set-Content -LiteralPath (Join-Path $source 'HN-IPTV.exe') -Value 'next executable fixture'
    $lock = [IO.File]::Open((Join-Path $destination 'zz-locked.dll'), [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $failed = $false
    try { & $script -NoRestart } catch { $failed = $true }
    $lock.Dispose(); $lock = $null
    if (-not $failed) { throw 'A locked target did not fail the update.' }
    if ((Get-Content -LiteralPath (Join-Path $destination 'HN-IPTV.exe') -Raw).Trim() -ne 'new executable fixture') { throw 'Rollback did not restore the previous executable.' }
    if ((Get-Content -LiteralPath (Join-Path $destination 'data/settings.json') -Raw).Trim() -ne 'user settings') { throw 'Rollback changed user data.' }
    Write-Host 'Portable updater replacement, user-data preservation and rollback checks passed.'
} finally {
    if ($null -ne $lock) { $lock.Dispose() }
    # Delete only the verified, synthetic test directory.
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $resolvedRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedRoot) -notlike 'hn-updater-test-*') { throw 'Unexpected test cleanup path.' }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
