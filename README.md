# HN-IPTV 0.9

A Windows x64 IPTV player built with C#/.NET 10, WPF and LibVLCSharp.

**Updating an installed copy:** run the new `HN-IPTV-Setup.exe` over the existing installation. Do not uninstall first. The application identity, installed location and user-data folder are preserved. See [UPGRADING.md](UPGRADING.md).

**Picture in picture:** click PiP below the video. The floating, resizable, always-on-top window keeps the same stream while you browse or minimize the main window. Close it or click Return to move video back. Buffer presets apply on the next channel tune. PiP bounds and buffer choice persist beside the other settings.

VLC remains the default. A separately compiled mpv comparison and manual Windows comparison workflow are included; no distribution bundles both engines. See [ENGINE-COMPARISON.md](ENGINE-COMPARISON.md).

Download `HN-IPTV-Setup.exe` or `HN-IPTV-Portable.zip` from this repository's Releases. The portable build includes .NET; extract the entire ZIP into a writable folder, then run `HN-IPTV.exe`. No password, installation, administrator privileges, or separate runtime is required.

Xtream and M3U capable. Targetting quick bootup time and general usability. 

## Build and test

On Windows with .NET 10 SDK and Inno Setup 6:

```powershell
./test-core.ps1
dotnet run --project HN-IPTV.Windows.SmokeTests -c Release -r win-x64 --self-contained true -- artifacts/windows-smoke.json
./build-release.ps1 -Version 0.7.0
```

`-PortableOnly` skips Inno Setup. `-FastStartup` enables ReadyToRun for comparison; the size-oriented default is off. WPF trimming and single-file extraction remain disabled.

GitHub Actions runs these checks and attaches the two distributions and checksums to a versioned release. Consumers do not need the source code or SDK.

The installer-test orchestration has been corrected and regression-tested. See [INSTALLER-TEST-FIX.md](INSTALLER-TEST-FIX.md). Native installation validation must still complete in Windows Actions.
