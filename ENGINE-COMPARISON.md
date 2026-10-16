# VLC, mpv and MPC-HC comparison

VLC remains the release default. Run the **Compare VLC, mpv and MPC-HC on Windows** workflow (also triggered on pull requests) to compare three fixture trials per engine on the same Windows runner. It records payload bytes, playback-progress latency, startup, and total process memory/CPU. MPC-HC's separate child process is included. Failed tests remain failures and do not enter speed medians. Short trials cannot establish real-device or long-session stability.

- `./build-windows.ps1 -Engine vlc`: normal self-contained release app.
- `./build-windows.ps1 -Engine mpv`: libmpv comparison, pinned by `mpv-build.json` archive and DLL checksums.
- `./build-windows.ps1 -Engine mpc`: experimental embedded MPC-HC 2.8.3 slave process, pinned by `mpc-build.json` archive SHA-256. A portable INI prevents access to an installed MPC-HC settings profile.
- `./test-windows.ps1`: verifies the exact published assembly. Uses only loopback HLS/TS/logo fixtures.

MPC-HC is smaller in the upstream binary package, but its remote-control API lacks the resolution/FPS/bitrate/codec telemetry needed by the Info panel and equivalent buffer controls. Those fields explicitly remain unavailable. Reparenting a separate player's window also requires physical-device validation. The comparison does not treat it as feature-equivalent.

References: [mpv embedding API](https://mpv.io/manual/stable/#embedding-into-other-programs-libmpv), [pinned mpv build](https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261015), [MPC-HC 2.8.3](https://github.com/clsid2/mpc-hc/releases/tag/2.8.3), [MPC-HC slave API](https://github.com/clsid2/mpc-hc/blob/2.8.3/src/mpc-hc/MpcApi.h).

Only VLC is packaged for users. Future public distributions of alternate engines must provide their applicable licenses and corresponding source/build information for the chosen binary and dependencies. Comparison dependencies are retrieved for local/CI evaluation and not uploaded as release assets.

See PERFORMANCE-0.9-REVIEW.md for review scope and validation limits.
