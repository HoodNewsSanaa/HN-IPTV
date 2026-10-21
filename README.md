> Feature Rich 0.14.0 uses local Whisper large-v3-turbo Q5 recognition (574 MB), then Azure translation, with optional synchronized 10–15 second buffered playback. Native Vulkan GPU and CPU runtimes are included. Prepare Whisper once in Translation settings; prior Nemotron downloads are retained for rollback.

# HN-IPTV 0.13.1 — Feature Rich preview

See [FEATURE-RICH.md](FEATURE-RICH.md) for Nemotron/Azure translation setup, fullscreen fixes and installable branch artifacts. The installer upgrades an existing stable copy in place and preserves saved data.

A Windows x64 IPTV player built with C#/.NET 10, WPF and mpv.

**Updating an installed copy:** run the new `HN-IPTV-Setup.exe` over the existing installation. Do not uninstall first. The application identity, installed location and user-data folder are preserved. See [UPGRADING.md](UPGRADING.md).

**Picture in picture:** click PiP below the video. The floating, resizable, always-on-top window keeps the same stream while you browse or minimize the main window. Close it or click Return to move video back. Buffer presets apply on the next channel tune. PiP bounds and buffer choice persist beside the other settings.

mpv is the default playback engine. Click **CC** or press **C** to toggle channel captions; the choice is remembered. Press **P** to toggle PiP. A single channel click starts playback. Drag the dividers between groups, channels and player to resize the panels; their proportions persist and scale with the app window. Optional VLC/MPC comparison builds remain available; each distribution contains one engine. See [ENGINE-COMPARISON.md](ENGINE-COMPARISON.md).

Download `HN-IPTV-Setup.exe` or `HN-IPTV-Portable.zip` from this repository's Releases. The portable build includes .NET; extract the entire ZIP into a writable folder, then run `HN-IPTV.exe`. No password, installation, administrator privileges, or separate runtime is required.

Supports Xtream, local M3U files and HTTP/HTTPS M3U URLs. Use **M3U URL** to subscribe to a playlist and **Guide** to attach an XMLTV file or URL (.xml or .xml.gz), matched by tvg-id. HTTP User-Agent and Referer directives are supported by mpv and VLC.

Cached channel names and icons appear first. Unchanged metadata retains the current list and selection; expired icons remain visible during conditional freshness checks. Streams that fail or stall retry at most three times. Stop and channel changes cancel recovery.

Fullscreen controls appear on pointer movement. **Space** pauses/resumes and **M** mutes/unmutes. Controls hide after inactivity.

The app checks GitHub Releases after startup without refreshing playlists. The footer offers **Update to …** when a newer stable release is available, or **Check for updates** for a manual check. Installation starts only when you choose it. Downloads must match the release SHA-256 checksum. Installed copies run the normal setup; portable copies update in place, preserve the data folder, and restart. Failed portable copies restore backed-up application files. Update staging and recovery logs are retained in the Windows temporary folder. 

## Build and test

On Windows with .NET 10 SDK and Inno Setup 6:

```powershell
./test-core.ps1
./build-windows.ps1
./test-windows.ps1
./build-release.ps1 -Version 0.11.4
```

`-PortableOnly` skips Inno Setup. `-FastStartup` enables ReadyToRun for comparison; the size-oriented default is off. WPF trimming and single-file extraction remain disabled.

GitHub Actions tests each build. A new project version merged into main publishes the tested installer, portable ZIP and checksums once; subsequent pushes at the same version only validate. Consumers do not need the source code or SDK.

Windows playback, installer upgrade and portable updater regressions run before release publication.
