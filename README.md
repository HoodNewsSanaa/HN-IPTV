# HN-IPTV 0.7.0 — Update 7

A Windows x64 IPTV player built with C#/.NET 10, WPF and LibVLCSharp.

Download `HN-IPTV-Setup.exe` or `HN-IPTV-Portable.zip` from this repository's Releases. The portable build includes .NET; extract the entire ZIP into a writable folder, then run `HN-IPTV.exe`. No password, installation, administrator privileges, or separate runtime is required.

Xtream Codes, M3U/M3U8, saved playlists, provider EPG, separate group/channel searches, group visibility, persistent favorites, long-press favorites, cached logos, fullscreen, and manual/scheduled refresh remain available. Stalker, external automatic EPG matching, and in-app updating are absent. The application makes no requests to GitHub for updates.

Update 7 moves VLC commands, catalog preparation/filtering, image decoding, storage and diagnostics away from the WPF dispatcher. A persistent native video child window replaces the unused WPF overlay wrapper. Playback is not stopped or detached when minimized. The package includes only the x64 VLC architecture, with its full plugin set.

See [CHANGELOG.md](CHANGELOG.md), [VALIDATION.md](VALIDATION.md), [PERFORMANCE.md](PERFORMANCE.md), [PORTABILITY.md](PORTABILITY.md), and [GITHUB-PUBLISHING.md](GITHUB-PUBLISHING.md).

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
