# VLC and mpv comparison

VLC remains the release engine. Both backends compile, but this Linux environment cannot establish Windows playback speed, rendering quality, PiP correctness, or long-session stability. No mpv performance or stability win is claimed.

The mpv comparison is pinned in `mpv-build.json`, including both archive and DLL SHA-256 digests. It uses the general x86-64 build, not the more restrictive x86-64-v3 build. `prepare-mpv.ps1` retrieves and verifies it at build time using 7-Zip. The normal application makes no update checks or GitHub requests.

- `./build-windows.ps1 -Engine vlc`: ordinary self-contained Windows publish.
- `./build-windows.ps1 -Engine mpv`: separate comparison publish without VLC dependencies.
- `./test-windows.ps1`: test the exact published assembly on Windows.
- Actions → Compare VLC and mpv on Windows: three trials each on the same runner, recording startup, playback progress latency, memory, PiP and window-state checks.

`compare-engines.ps1` records whether mpv's synthetic tune-progress median improves by at least 10%. It does not treat decode progress as a rendered first frame or turn a short fixture test into a stability claim. A live provider/device comparison is required before changing the default. Failed or unavailable comparison evidence leaves VLC as the default.

The mpv binary is not included in the source ZIP or normal distributions. Any future public mpv distribution must include the applicable licenses and complete corresponding source/build information for the selected binary and its dependencies. Source references: https://mpv.io/installation/ and https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261015 . The manual and C API are at https://mpv.io/manual/stable/ and https://github.com/mpv-player/mpv/blob/master/include/mpv/client.h .

Measured local package sizes and their limitations are recorded in PERFORMANCE-0.8.md and validation/update8-size-comparison.json.
