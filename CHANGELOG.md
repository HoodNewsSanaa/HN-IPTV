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
