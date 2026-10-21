# Feature Rich 0.15.0 — on-device Whisper translation

Choose **Whisper on device (English)** under Translation settings → Speech → Translation engine, then click Download / prepare Whisper and save. The multilingual Medium Q5 model downloads once (539,212,467 bytes), with the existing 0.9 MB Silero speech detector reused. Whisper recognizes foreign speech and translates it directly into English on this PC. No Azure key, text quota, or cloud translation request is used in this mode. Media streams and first-time component downloads still need their normal network connections.

New settings default to on-device translation. Existing saved Azure settings retain their selected provider; switch the engine explicitly to use the new local mode. Local output is always English. Choose Azure for other output languages; that mode keeps the existing Turbo Q5 recognition model, Azure key and region controls. Medium is used for local translation because Turbo is not trained for speech translation.

Vulkan GPU and CPU runtimes remain bundled; no Python or CUDA toolkit download is needed. Auto validates the GPU runtime and can fall back to CPU. Local Medium translation may take longer than Turbo recognition plus Azure, particularly on CPU. The selected buffer is a target, not a guaranteed maximum latency; playback refills if the model falls behind.

## Buffered captions

Click Translate speech and choose Live, Generous time — 10 seconds, or Generous time — 15 seconds (recommended). A local speech detector proposes pauses after four seconds; Whisper punctuation helps finalize complete sentences. Unfinished text waits for more context, bounded by an eight-second batch in 10-second mode or a twelve-second batch in 15-second mode. This is heuristic sentence grouping, not guaranteed paragraph or idea detection. Text does not fully preserve emotion or tone.

The mpv build takes recognition audio ahead of playback from one incoming feed and delays original audio/video together. Compact captions follow media time, pause with the video, and rise above fullscreen controls. Translated-word alignment is approximate within the source speech interval.

The FFmpeg 9.0.2 media transport is reused or downloaded once (115 MB compressed). Temporary media is limited to 512 MB per session and cleaned on stop, channel changes and next use after a crash. A long pause can exceed the rolling window and require rebuilding the buffer. Live mode remains available.

## Installation

Install HN-IPTV-Setup.exe over the existing installation. Saved playlists, preferences, Azure credentials and existing model downloads are retained. Medium and Turbo have separate readiness markers, so preparing one mode does not falsely mark the other model ready. The feature-rich-v* prerelease update channel is unchanged.

Real Spanish-to-English local translation, English recognition, adaptive speech boundaries, native playback, pause/fullscreen/PiP, core checks and installer upgrades are checked before publication. GPU performance still needs validation on the user's hardware. Screen OCR remains retired; channel-provided CC is available.
