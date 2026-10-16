param([Parameter(Mandatory)][string]$PublishDirectory, [ValidateSet('vlc','mpv','mpc')][string]$Engine = 'mpv')
$ErrorActionPreference = 'Stop'
$required = @('HN-IPTV.exe','HN-IPTV.dll','HN-IPTV.Core.dll','HN-IPTV.deps.json','HN-IPTV.runtimeconfig.json',
    'coreclr.dll','hostfxr.dll','PresentationFramework.dll','build-manifest.json')
if ($Engine -eq 'vlc') {
    $required += @('LibVLCSharp.dll',
    'libvlc/win-x64/libvlc.dll','libvlc/win-x64/libvlccore.dll',
    'libvlc/win-x64/plugins/demux/libadaptive_plugin.dll','libvlc/win-x64/plugins/demux/libts_plugin.dll',
    'libvlc/win-x64/plugins/codec/libavcodec_plugin.dll')
    if ((Test-Path (Join-Path $PublishDirectory 'libmpv-2.dll')) -or (Test-Path (Join-Path $PublishDirectory 'mpc-hc'))) { throw 'VLC package contains mpv.' }
} elseif ($Engine -eq 'mpc') {
    $required += @('mpc-hc/mpc-hc64.exe','mpc-hc/LAVFilters64/LAVVideo.ax','mpc-hc/COPYING.txt')
    if ((Test-Path (Join-Path $PublishDirectory 'libvlc')) -or (Test-Path (Join-Path $PublishDirectory 'libmpv-2.dll'))) { throw 'MPC-HC package contains another engine.' }
} else {
    $required += 'libmpv-2.dll'
    if ((Test-Path (Join-Path $PublishDirectory 'libvlc')) -or (Test-Path (Join-Path $PublishDirectory 'LibVLCSharp.dll')) -or (Test-Path (Join-Path $PublishDirectory 'mpc-hc'))) { throw 'mpv package contains VLC.' }
}
foreach ($relative in $required) {
    if (-not (Test-Path (Join-Path $PublishDirectory $relative))) { throw "Incomplete publish: missing $relative" }
}
$bad = @(Get-ChildItem $PublishDirectory -Recurse -File | Where-Object {
    $relative = [IO.Path]::GetRelativePath($PublishDirectory, $_.FullName).Replace('\','/')
    $relative -match '(^|/)(win-x86|win-arm64|obj|bin|data|\.git)/' -or
    $_.Name -match '\.(pdb|cs|xaml|vault|dpapi|log|nupkg)$' -or
    $_.Name -like 'player-settings*' -or $_.Name -like 'saved-playlists*' -or $_.Name -like 'channel-preferences*'
})
if ($bad.Count) { throw ('Publish contains non-release files: ' + (($bad | ForEach-Object Name) -join ', ')) }
# A successful copy/build is not proof that a native/runtime file is complete.
# Read PE section bounds only; do not load or execute these binaries.
foreach ($file in Get-ChildItem $PublishDirectory -Recurse -File | Where-Object { $_.Extension -in @('.dll', '.exe') }) {
    $stream = [IO.File]::OpenRead($file.FullName)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { throw "Invalid PE image: $($file.Name)" }
        $stream.Position = 0x3C; $offset = $reader.ReadUInt32()
        if ([long]$offset + 24 -gt $stream.Length) { throw "Truncated PE header: $($file.Name)" }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $($file.Name)" }
        $stream.Position = $offset + 6; $sections = $reader.ReadUInt16()
        $stream.Position = $offset + 20; $optionalSize = $reader.ReadUInt16()
        for ($i = 0; $i -lt $sections; $i++) {
            $section = [long]$offset + 24 + $optionalSize + $i * 40
            if ($section + 40 -gt $stream.Length) { throw "Truncated PE sections: $($file.Name)" }
            $stream.Position = $section + 16
            $length = $reader.ReadUInt32(); $start = $reader.ReadUInt32()
            if ($length -gt 0 -and [long]$start + $length -gt $stream.Length) { throw "Truncated PE image: $($file.Name)" }
        }
    } finally { $reader.Dispose(); $stream.Dispose() }
}
Write-Host 'Publish dependencies, architectures, and absence of user data verified.'
