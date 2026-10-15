param([string]$PublishDirectory = (Join-Path $PSScriptRoot 'artifacts/publish'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PublishDirectory $PublishDirectory
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('hn-package-check-' + [guid]::NewGuid().ToString('N'))
try {
    Copy-Item -LiteralPath $PublishDirectory -Destination $temporary -Recurse
    $file = [IO.File]::OpenWrite((Join-Path $temporary 'System.Private.CoreLib.dll'))
    try { $file.SetLength(64) } finally { $file.Dispose() }
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'verify-package.ps1') -PublishDirectory $temporary }
    catch { if ("$_" -notmatch 'Truncated PE') { throw }; $rejected = $true }
    if (-not $rejected) { throw 'Truncated runtime DLL was accepted.' }
    Write-Host 'Complete publish accepted; deliberately truncated runtime DLL rejected.'
} finally { if (Test-Path $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force } }
