# Third-party components

- Microsoft .NET 10 / WPF. The self-contained publish includes Microsoft's `LICENSE.txt` and `ThirdPartyNotices.txt`.
- LibVLCSharp 3.10.2: the NuGet package declares `LGPL-2.1-or-later`. Source: https://code.videolan.org/videolan/LibVLCSharp ; mirror/tag: https://github.com/videolan/libvlcsharp/tree/3.10.2 .
- VideoLAN.LibVLC.Windows 3.0.24: the NuGet package declares `LGPL-2.1-or-later`. Package build/source information: https://code.videolan.org/videolan/libvlc-nuget ; VLC source releases: https://download.videolan.org/pub/videolan/vlc/3.0.24/ . The complete x64 plugin set is retained. VLC includes components with their own notices; retain source-package disclosures when redistributing modified native libraries.
- LGPL 2.1 text is included in `licenses/LGPL-2.1.txt`. Native libraries remain separate DLLs.
- The vintage television artwork is unchanged from Update 6. No new artwork is supplied by this update.
- The small HLS/MPEG-TS test fixtures are generated color patterns and a sine tone, produced with FFmpeg solely for regression testing. They contain no provider streams or account data and are excluded from application packages.

No project-wide license was added or changed by this update.


Optional mpv comparison source is included, but its binary is not distributed with the normal release. The bootstrap source/digests and distribution considerations are documented in ENGINE-COMPARISON.md.

## mpv evaluation package

The explicitly requested mpv variant includes the unmodified libmpv-2.dll pinned by mpv-build.json. The standard VLC release is unchanged. GPL version 3 text is included in licenses/GPL-3.0.txt.

- mpv source revision: https://github.com/mpv-player/mpv/tree/492bae0768
- Upstream binary: https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261015
- Build scripts and dependency recipes: https://github.com/shinchiro/mpv-winbuild-cmake/tree/1f8d2e7
- HN-IPTV application and embedding source: https://github.com/HoodNewsSanaa/HN-IPTV

The bootstrap manifest records exact archive and DLL hashes. Native libraries remain separate and replaceable. This is a user-requested evaluation package, not a public mpv release.
