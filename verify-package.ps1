param([Parameter(Mandatory)][string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
$required = @('HN-IPTV.exe','HN-IPTV.dll','HN-IPTV.Core.dll','HN-IPTV.deps.json','HN-IPTV.runtimeconfig.json',
    'coreclr.dll','hostfxr.dll','PresentationFramework.dll','LibVLCSharp.dll',
    'libvlc/win-x64/libvlc.dll','libvlc/win-x64/libvlccore.dll',
    'libvlc/win-x64/plugins/demux/libadaptive_plugin.dll','libvlc/win-x64/plugins/demux/libts_plugin.dll',
    'libvlc/win-x64/plugins/codec/libavcodec_plugin.dll')
foreach ($relative in $required) {
    if (-not (Test-Path (Join-Path $PublishDirectory $relative))) { throw "Incomplete publish: missing $relative" }
}
$bad = @(Get-ChildItem $PublishDirectory -Recurse -File | Where-Object {
    $relative = [IO.Path]::GetRelativePath($PublishDirectory, $_.FullName).Replace('\','/')
    $relative -match '(^|/)(win-x86|win-arm64|obj|bin|data|\.git)/' -or
    $_.Name -match '\.(pdb|cs|xaml|vault|dpapi|log|nupkg)$' -or
    $_.Name -like 'saved-playlists*' -or $_.Name -like 'channel-preferences*'
})
if ($bad.Count) { throw ('Publish contains non-release files: ' + (($bad | ForEach-Object Name) -join ', ')) }
Write-Host 'Publish dependencies, architectures, and absence of user data verified.'
