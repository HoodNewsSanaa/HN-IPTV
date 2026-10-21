# 0.14.0

- Replace Nemotron recognition with multilingual Whisper large-v3-turbo Q5 (574 MB), using Whisper.net 1.9.1 and whisper.cpp.
- Keep Azure text translation and the Live / 10-second / 15-second synchronized playback choices.
- Add a pinned 0.9 MB Silero VAD model for adaptive endpoints: pause plus terminal punctuation, bounded by an 8/12-second maximum. Unfinished candidate text stays private until more context or the deadline.
- Decode buffered speech as longer batches with punctuation, language detection, segment timestamps and no-speech filtering. Text cannot convey all vocal tone or emotion.
- Include native Vulkan GPU and CPU runtimes; no Python environment or CUDA toolkit download. Auto validates GPU inference and falls back to CPU if unavailable.
- Keep the model resident between batches, resample complete batches to 16 kHz, and isolate native inference from the player.
- Preserve existing Nemotron downloads for rollback and retain Azure keys, playlists and the FFmpeg media transport.
- Test actual Whisper speech, punctuation, timestamps, silence/reset and buffered A/V playback before publishing.

# 0.13.1 Feature Rich preview

- Offer Live, Generous time (10 seconds), and Generous time (15 seconds, recommended) when enabling Translate. Remember the timing choice.
- Keep Nemotron recognition and Azure text translation. Generous time recognizes ahead of playback and translates longer dialogue groups, preferring completed utterances for more context.
- Preserve source word timestamps and display compact cues against the media playback clock, including while paused.
- Use one upstream stream to produce both recognition audio and a local rolling audio/video buffer. Delay both original audio and video together.
- Expose buffered media only after the chosen lead time and successful translation; refill naturally when processing falls behind.
- Download the checksum-pinned FFmpeg media transport once (115 MB). Bound active temporary media to 512 MB, clean it on stop and clean abandoned owned sessions on next use.
- Clear buffers/captions on channel changes and cancellation; return to live playback when translation is disabled or fails.
- Validate native media timestamps with a generated color/tone fixture, translation gating, paused captions, cancellation and real Nemotron word timestamps.

# 0.13.0 Feature Rich preview

- Replace the local language stack with Nemotron 3.5 ASR Streaming Q8 → Azure Translator → timed captions.
- Use one 742 MB multilingual model and a native Windows recognition host. Remove Python, Whisper, Argos/OPUS and EasyOCR from the build.
- Submit fresh audio about every 320 ms, retain native streaming context, and keep recognition running while Azure translates earlier stable phrases.
- Preserve automatic/manual languages, encrypted Azure credentials, toggle colors, compact captions and fullscreen toolbar avoidance.
- Validate the replacement before deleting old app-owned language downloads and private CUDA libraries. Refuse linked cleanup targets; preserve playlists, settings and keys.
- Retire screen OCR translation; channel CC remains available.
- Checksum-pin the model and CPU/CUDA runtime archives; validate native speech, reset, silence, migration and Windows upgrades.

# 0.12.3 Feature Rich preview

- Buffer app audio while recognition warms up, prepare speech before screen OCR, and begin speech processing as soon as its model is ready.
- Reduce continuous capture intervals from 2.5 to 1.5 seconds and allow confirmed clause boundaries to dispatch earlier while retaining two-pass word confirmation.
- Reuse healthy idle workers and loaded models across active-session channel changes. Reset old captions and language detection; rebuild workers after canceled in-flight requests or settings changes.
- Avoid importing PyTorch solely to detect speech CUDA support. Local RTX 4050/medium startup probes improved from a 4.92-second median to 3.25 seconds (three runs each); this is a workstation measurement, not an end-to-end cloud latency claim.
- Trim already confirmed old audio before recognition while keeping three seconds of acoustic context and all unconfirmed speech.
- Preserve existing models, CUDA libraries, Azure settings and installer upgrade identity.

# 0.12.2 Feature Rich preview

- Add an Azure settings tab with a masked auth-key field, Global/regional resource support, connection test and explicit Local/Azure provider selection.
- Encrypt saved Azure keys for the current Windows account; exclude keys from normal settings, worker arguments and diagnostics.
- Keep speech recognition and OCR local with worker network access blocked. Only confirmed speech text and recognized screen text pass through the app to Azure Translator.
- Cache translated text and report sanitized authentication, quota, rate-limit and connection errors. Local translation remains selectable; existing users stay Local until selecting Azure.
- Reuse existing runtime, models and CUDA libraries. Azure model setup skips unnecessary local translation-pack downloads.
- Validate HTTP routing/errors/cache with simulated responses, actual offline OCR through the C# cloud bridge, and Windows key-storage/settings regressions.

# 0.12.1 Feature Rich preview

- Move translated speech captions and the separate text-caption bar above the visible fullscreen toolbar, using its measured bounds with DPI-aware coordinate conversion; restore the normal position when controls hide.
- Replace provisional rolling paragraphs with compact timed speech cues. Confirm source words across recognition passes, retain sentence context, translate confirmed phrases together, then split for display.
- Expire each cue after its reading interval, suppress duplicate committed words, and bound the caption queue.
- Add fullscreen toolbar-clearance and caption-expiry regressions plus real offline timed-phrase confirmation checks.
- Reuse the existing runtime, models and CUDA downloads. Stable main remains unchanged.

# 0.12.0 Feature Rich preview

- Translate visible text at its original position, with fitted lettering, sampled background masks, tracked regions and separate speech captions. Keep the caption-bar option.
- Add the benchmark-selected compact OPUS-MT Arabic-to-English text pack with pinned checksum and standalone tokenizers. Keep Argos for other pairs and as a manual option.
- Add multilingual turbo recognition with a separate local text translator, automatic/manual speech model and pipeline selection, and existing CUDA/CPU fallback.
- Gate speech decoding with Silero VAD, reject unreliable segments, and offer optional conservative noise suppression. Capture remains restricted to this app.
- Retain only the newest queued speech segment, discard stale audio, and preserve rolling context for recognition.
- Preserve existing manual settings, cached models, CUDA libraries and the stable installation identity.
- Include reproducible Arabic recognition, noisy-audio and text-translation comparisons in TRANSLATION-BENCHMARKS.md.

# 0.11.4 Feature Rich preview

- Fix Arabic and other right-to-left OCR reading order; preserve small text at up to 1080p and handle bottom-up video frames.
- Cache unchanged text regions while the video background moves; retry frame capture errors and translate paused frames.
- Share and prewarm one GPU worker before starting loopback capture, reducing duplicate GPU memory use and startup speech loss.
- Segment speech at short pauses or a 3.5-second limit and retain readable caption context across empty results.
- Guard fullscreen and translation overlays against reentrant activation and late callbacks after closing.
- Show darker active colors for CC, translation, pause, mute, Info, fullscreen, PiP, favorites and search panels.
- Keep existing models, CUDA libraries, playlists and settings during installation.

# 0.11.3 Feature Rich preview

- Add Auto / NVIDIA CUDA / CPU translation acceleration with private optional CUDA 12.8 libraries.
- Accelerate speech, screen recognition and text translation; fall back to CPU on GPU failure or insufficient memory.
- Add the medium multilingual speech model and improve speech/text beam search quality.
- Show the active inference backend and CPU fallback in the app status.
- Include the BPE Spanish-to-English language pack setup fix from 0.11.2.

# 0.11.2 Feature Rich preview

- Fix setup and local translation for Spanish-to-English and other BPE language packs.
- Reuse previously downloaded BPE packs when retrying setup.
- Install tokenizer dependencies during upgrade and suppress private Python PATH warnings.
- Validate both translation directions with real models and networking disabled.

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
