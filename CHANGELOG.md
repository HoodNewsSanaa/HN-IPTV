# 0.11.1 — Feature Rich preview

- Keep translated captions working when returning from picture in picture to the main player.
- Include the completed fullscreen focus, local translation and stable-to-feature upgrade checks.

# 0.11.0 — Feature Rich preview

- Restore fullscreen controls and keyboard exit after switching applications; preserve normal Alt-Tab behavior.
- Add independently toggleable local speech and screen-text translation with automatic/manual input and output languages.
- Cache optional private runtime, speech, OCR and translation models in the existing data folder.
- Keep model preparation separate from inference; translation workers have Python networking disabled and process only this app's audio/video.
- Preserve stable installer identity and test upgrading the actual 0.10.0 installer with cached model/settings sentinels.

See FEATURE-RICH.md for setup, language coverage and experimental limitations.

# 0.10.0

- Check GitHub Releases shortly after startup and offer an in-app update action. Download the matching installer or portable package, verify its SHA-256 checksum, save settings before closing, and preserve user data. Portable updates back up replaced files and roll back on copy failure.
- Retain the existing catalog, selection and row state when refreshed channel metadata is unchanged. Display disk-cached logos before freshness checks and use conditional HTTP requests for expired icons.
- Retry failed or stalled streams up to three times with increasing delays. Stop, channel changes and shutdown cancel pending recovery.
- Add fullscreen controls over native video with mouse-driven visibility; Space pauses and M mutes.
- Add HTTP/HTTPS M3U playlist import and per-channel User-Agent/Referer support in mpv and VLC. MPC comparison builds report unsupported headers explicitly.
- Add optional XMLTV file/URL (.xml or .xml.gz) guides for M3U playlists, matched by tvg-id. Retain cached guides when refresh fails.
- Use the project version for automatic releases. A deliberate version bump on main publishes one tested release; later pushes for that version validate without replacing it. Attach SHA256SUMS.txt alongside both packages.

# 0.9.0 — release candidate

- Borderless PiP with title/controls in owned hover overlays over the native video. Preserve the stream's display aspect when resizing; no reserved heading/footer or app-added padding.
- Add a toggled information panel under the stream for resolution, frame rate, received bitrate and audio format.
- Keep Buffering status until playback advances; add native queue/stop/progress timing without URLs or credentials.
- Reduce M3U parser allocation, cancel obsolete logo work, serve cached EPG without network queue waits, coalesce volume changes and batch diagnostic writes.
- Add pinned mpv and MPC-HC comparison builds with same-runner Windows trials; VLC remains the default. MPC child memory/CPU is included, and missing telemetry/equivalent buffering is documented.
- Preserve in-place upgrades and existing data. Carry the three PowerShell argument fixes into all builds.

See PERFORMANCE-0.9-REVIEW.md for scope, measurements and manual validation limits.

# Update 8 / 0.8.0 — release candidate

- Add a resizable, always-on-top picture-in-picture window with pause, mute, volume and Return controls. Reparent one persistent native video target so PiP does not reconnect the stream. Keep the main browser usable and PiP visible when the main window is minimized.
- Save PiP bounds and playback buffer presets asynchronously and atomically in the existing installed/portable data folder.
- Retain one channel view, skip unchanged result resets, avoid catalog work for favorites outside the Favorites group, and retain category scroll positions.
- Cancel obsolete category preview/logo waits. Promote selected-channel EPG work ahead of queued previews without duplicating requests.
- Add a brief cooldown for failed tune requests; explicit Play retries immediately. Existing obsolete-tune cancellation remains.
- Record sanitized exception types, HRESULTs and application stack frames. Write a bounded fatal log when an asynchronous log flush may not complete.
- Include a checksum-pinned mpv comparison backend. VLC remains the release default because Windows speed and stability are unverified here.
- Publish the application once, compile the UI harness against that exact assembly, and package the same publish. Keep dependency caching and avoid recompressing package artifacts.
- Use quicker compression for development builds and strong compression for release builds. Remove stale symbols and reject truncated PE files.
- Keep the existing installer AppId/scope/data location, remember the old install directory, and test a previous-version installer registration fixture followed by upgrade/reinstall/uninstall.
- Release downloads contain only the installer EXE and complete portable ZIP; SHA-256 checksums are generated and printed in release notes.

No built-in updater, Android package, Stalker support, external EPG matching, or unrelated interface redesign is introduced. Earlier history follows.

# Update 7 hotfix / 0.7.1

- Fix the confirmed unhandled ObjectDisposedException in channel/category selection: clear cancellation-source fields before cancelling and disposing them.
- Apply the same ownership fix to catalog replacement, repeated Stop, subsequent tuning, and window shutdown.
- Ignore channel-selection events during shutdown.
- Add Windows UI regressions for selection through an empty list/no-guide channel, catalog replacement and repeated Stop. Existing playback and installer checks remain enabled.
- Keep existing saved data, IPTV features, and Windows-only packaging.

# Update 7 / 0.7.0

- Serialize VLC initialization, stop, tune, pause, volume and shutdown on workers. Cancel obsolete tune requests; retain one native video HWND across minimize/restore and fullscreen layout changes.
- Replace LibVLCSharp.WPF with LibVLCSharp and a small WPF HwndHost. The interface and layout are unchanged; the application did not use overlay content.
- Prepare catalogs off the dispatcher, debounce/cancel search, and index channel groups. Retain recycling virtualization.
- Load preferences asynchronously; save favorites off the dispatcher. Coalesce playlist saves into one active and one newest pending snapshot, write atomically, retain a last-good `.bak`, and flush on close.
- Reconnect cached Xtream accounts without downloading a fresh catalog on every launch when metadata is still current. Parse M3U files through a streaming reader, support commas within quoted attributes, handle duplicate attributes, and honor cancellation.
- Load/decode/resize/freeze logos on workers. Deduplicate downloads, cap in-memory cache/queues, cache failures briefly, and bound disk cache to 128 MiB during startup maintenance.
- Limit EPG requests to selected/realized rows, retain timestamped provider cache, cap queued requests and persisted entries, and correct playlist identity capture before asynchronous waits. Pause row work while minimized.
- Remove all portable password dialogs and vault encryption code. Portable playlists are stored as JSON beside the EXE in `data`; installed data remains Windows-user-protected. Keep legacy files intact and show a migration notice. See PORTABILITY.md for the encrypted-vault limitation.
- Add rotating asynchronous diagnostics, dispatcher-delay monitoring, operation durations and startup-to-window timing. No credentials, stream URLs, exception messages, or provider names are logged.
- Publish only x64 VLC; remove obsolete x86/ARM64 copies during installed upgrades. Preserve the full x64 plugin set and the untrimmed .NET runtime.
- Clean publish/package staging; preserve data on upgrades and by default in silent uninstall; generate checksums and separate size categories. Do not double-compress release packages in GitHub artifacts.
- Add 50,000-channel M3U/Xtream, persistence/cancellation/save-queue tests; add a Windows HLS/TS playback/minimize/restore/fullscreen fixture harness and installer-preservation tests.
- Retain manual updates. No update checker, updater service, GitHub API calls or update notifications in the app.

Build verification and runtime verification are distinct; see VALIDATION.md before calling this a tested Windows release.

## Installer-test correction — 2026-10-15

- Track an uninstall attempt so cleanup never launches the uninstaller a second time.
- Wait for Inno Setup's temporary clone and retry transient cleanup locks with a bounded timeout.
- Print success only after cleanup; preserve the original validation error if cleanup also fails.
- Refuse an existing installed application as well as existing data; delete only test-owned synthetic data.
- Write separate initial-install, reinstall and uninstall logs and retain them in Actions reports on failure.
- Add seven cross-platform orchestration regressions using simulated processes/registry, plus parser checks. Real Windows installer tests remain enabled.
- Package Windows source only for the repository restart; Android is set aside.
