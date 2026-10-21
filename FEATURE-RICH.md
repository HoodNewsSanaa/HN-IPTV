# Whisper buffered captions (0.14.0)

The current recognition stack is **Whisper large-v3-turbo Q5 → Azure Translator → timed captions**. Open Translation settings and prepare the new 574 MB multilingual model once. Choose Auto acceleration (Vulkan GPU, with validated CPU fallback) or CPU / Vulkan explicitly. Native runtimes are included; Python and CUDA toolkit installs are unnecessary.

The existing Generous time mode delays original audio and video together by a target 10 or 15 seconds, giving Whisper an 8- or 12-second audio batch before translation. Captions follow media time and pause with the video. If processing cannot keep up, playback waits for translated media; the selected delay is not a hard latency guarantee. CPU inference can be substantially slower.

Whisper generates punctuation using audio context. Azure receives the recognized text only; neither vocal emotion nor every nuance of intonation is preserved. Caption timing is phrase-level, not exact translated-word alignment. Automatic input language detection and manual input/output language settings remain available.

Existing Nemotron files are retained so installing the previous release can reuse them. New model setup happens from Translation settings and does not automatically download at boot. Existing Azure credentials, playlists and the FFmpeg transport are reused.

## Buffered playback

Click Translate speech and select Live, Generous time — 10 seconds, or Generous time — 15 seconds (recommended). The timing choice is also available in settings. Speech blocks end adaptively at a detected pause followed by Whisper sentence punctuation, starting after four seconds. Unfinished text waits for more context. The 10-second mode caps batches at eight seconds; the 15-second mode caps them at twelve seconds. A 0.9 MB local Silero speech detector supplies pause detection. Sentence endings are heuristic; paragraph/idea completeness cannot be guaranteed. Live mode uses five-second recognition batches and does not delay the video.

The mpv build extracts audio ahead of playback from one incoming feed. It releases original audio/video together only after the selected lead time and completed translation. Azure failures stop translation and return to live playback. Azure requires Internet access, and recognized text counts toward your Azure quota.

The optional FFmpeg 9.0.2 media transport downloads once (115 MB compressed). It copies original video/audio codecs into a local rolling playlist. Temporary media is limited to 512 MB per session; stop, channel changes and disabling translation clean it. Abandoned owned sessions are cleaned on next buffered use. A long pause can outlive the rolling window and require rebuilding the buffer. Captions follow the paused media clock and rise above fullscreen controls.

## Installation

Run HN-IPTV-Setup.exe over the existing installation; do not uninstall first. App identity, playlists, preferences and encrypted Azure credentials are preserved. Downloaded Whisper data is stored under speech-whisper within the app data directory; portable installations use their own data directory.

The release retains the existing feature-rich-v* prerelease update channel. Native playback, fullscreen/PiP, installer upgrades, core checks and real Whisper recognition must pass before publication. Screen OCR translation remains retired; channel-provided CC remains available.
