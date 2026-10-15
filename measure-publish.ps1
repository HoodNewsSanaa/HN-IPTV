param([string]$PublishDirectory = (Join-Path $PSScriptRoot 'artifacts/publish'),
      [string]$DistDirectory = (Join-Path $PSScriptRoot 'dist'),
      [string]$OutputPath = (Join-Path $PSScriptRoot 'artifacts/size-report.json'))
$ErrorActionPreference = 'Stop'
$totals = [ordered]@{ app_and_notices = [long]0; dotnet_runtime = [long]0; vlc_x64 = [long]0; vlc_other_architectures = [long]0 }
$files = @(Get-ChildItem $PublishDirectory -File -Recurse)
foreach ($file in $files) {
    $path = [IO.Path]::GetRelativePath($PublishDirectory, $file.FullName).Replace('\','/')
    $key = if ($path -like 'libvlc/win-x64/*') { 'vlc_x64' }
        elseif ($path -like 'libvlc/*') { 'vlc_other_architectures' }
        elseif ($path -match '^(HN-IPTV|LibVLCSharp|PORTABILITY|THIRD-PARTY|licenses/)') { 'app_and_notices' }
        else { 'dotnet_runtime' }
    $totals[$key] += $file.Length
}
$installed = [long](($files | Measure-Object Length -Sum).Sum)
$zipPath = Join-Path $DistDirectory 'HN-IPTV-Portable.zip'
$setupPath = Join-Path $DistDirectory 'HN-IPTV-Setup.exe'
$zipSize = if (Test-Path $zipPath) { (Get-Item $zipPath).Length } else { $null }
$setupSize = if (Test-Path $setupPath) { (Get-Item $setupPath).Length } else { $null }
$report = [ordered]@{ units='bytes'; component_bytes=$totals; application_payload_bytes=$installed;
    portable_zip_bytes=$zipSize; installer_bytes=$setupSize;
    combined_package_payload_bytes=($(if ($zipSize -and $setupSize) { $zipSize + $setupSize } else { $null }));
    notes='Application payload excludes generated uninstaller and user data. Combined payload excludes the small GitHub artifact wrapper and checksums.' }
New-Item -ItemType Directory -Force (Split-Path $OutputPath) | Out-Null
$report | ConvertTo-Json -Depth 5 | Set-Content $OutputPath -Encoding utf8
$report | ConvertTo-Json -Depth 5 | Write-Host
