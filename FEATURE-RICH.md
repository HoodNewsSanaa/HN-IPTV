# Whisper buffered captions (0.14.0)

The current recognition stack is **Whisper large-v3-turbo Q5 → Azure Translator → timed captions**. Open Translation settings and prepare the new 574 MB multilingual model once. Choose Auto acceleration (Vulkan GPU, with validated CPU fallback) or CPU / Vulkan explicitly. Native runtimes are included; Python and CUDA toolkit installs are unnecessary.

The existing Generous time mode delays original audio and video together by a target 10 or 15 seconds, giving Whisper an 8- or 12-second audio batch before translation. Captions follow media time and pause with the video. If processing cannot keep up, playback waits for translated media; the selected delay is not a hard latency guarantee. CPU inference can be substantially slower.

Whisper generates punctuation using audio context. Azure receives the recognized text only; neither vocal emotion nor every nuance of intonation is preserved. Caption timing is phrase-level, not exact translated-word alignment. Automatic input language detection and manual input/output language settings remain available.

Existing Nemotron files are retained so installing the previous release can reuse them. New model setup happens from Translation settings and does not automatically download at boot. Existing Azure credentials, playlists and the FFmpeg transport are reused.

# HN-IPTV Feature Rich preview — 0.13.1

Branch: `HN-IPTV-Feature-Rich`.

## Translation

The pipeline is **the previous Nemotron engine 3.5 ASR Streaming Q8 → Azure Translator → timed captions**.

Recognition uses one multilingual local model (741,548,352 bytes), with automatic or manual input language. Output Auto uses the Windows display language. Language coverage and accuracy vary; some languages are experimental. Azure translates recognized text only; audio stays on this PC.

Open Translation settings to prepare the previous Nemotron engine, choose Auto/CUDA/CPU, and paste the Azure Translator key and region in the Azure tab. Existing encrypted Azure credentials are preserved. Azure requires Internet access and consumes your resource's text-translation quota. Cloud response time and phrase context still contribute to caption delay.

The native Windows runtime replaces Python, PyTorch, Whisper, Argos, OPUS and EasyOCR. CPU runtime download is about 5.5 MB; optional CUDA runtime about 102 MB. The model downloads separately once. Driver compatibility is checked by actual inference, with native CPU fallback for Auto. This build does not require the old multi-gigabyte PyTorch CUDA installation.

Fresh audio is sent about every 320 ms. The native stream retains acoustic context, so old audio is not repeatedly decoded. Recognition and Azure translation run independently with bounded queues. Stable phrases become compact pop-on cues; unfinished trailing word fragments are withheld. Captions still rise above fullscreen controls.

Screen-text OCR translation has been retired as part of removing the old local language stack. Channel-provided CC remains available.

## Generous time

Click Translate speech and choose Live, Generous time — 10 seconds, or Generous time — 15 seconds (recommended). The choice is also in Translation settings.

Generous time uses one incoming media feed. It extracts recognition audio ahead of playback, translates groups of roughly 6–12 seconds, preferring completed utterances through Azure, and delays original video and audio together. Captions use the previous Nemotron engine's source word timestamps and the player's media clock. Translated paragraphs are split into compact cues proportionally across their source speech interval; this is phrase-level synchronization, not exact translated-word alignment.

The 10-second option caps groups at eight seconds to leave processing headroom. The 15-second option permits up to 12 seconds of context.

The rolling playlist exposes media only after its lead time and completed translation. If processing falls behind, playback waits/refills rather than showing late captions. Source/network latency, segment boundaries and translation delays can make the actual delay longer than the selected 10 or 15 seconds. Azure failures stop buffered translation and return to live playback with an error.

The optional FFmpeg 9.0.2 media transport downloads once (115 MB compressed), separately from the installer and language model. It copies original video/audio codecs into short local segments and decodes a separate audio output for recognition. It does not add another language model. mpv builds support this mode; Live remains available in comparison builds.

Temporary media is limited to 512 MB per session and a bounded rolling window. Stop, channel changes and disabling translation remove session files. Known abandoned sessions are removed on next buffered use. A long pause can outlive the rolling window and require rebuilding the buffer. Captions follow the paused media clock and avoid the fullscreen controls.

## Upgrade and cleanup

Run HN-IPTV-Setup.exe over the existing installation. Do not uninstall first. App identity, playlists, preferences and Azure credentials are preserved.

When old language downloads exist, first launch prepares the replacement. Old downloads are deleted **only after** the checksum-verified the previous Nemotron engine model completes real native inference. The cleanup targets only the app-owned `translation` directory, including its private Python, recognition/OCR/translation models, caches and PyTorch CUDA libraries. It refuses linked directories. If setup fails, old data is retained and Translation settings can retry. Files locked by another app instance require closing it and retrying setup.

The new `speech-nemotron` directory contains the model and native runtimes. Downloaded third-party license notices remain beside their runtime. Portable installations use their own data directory.

Feature builds use the `feature-rich-v*` prerelease channel. Native playback/fullscreen/PiP, installer upgrades, core checks and real native speech inference must pass before publication.

See [NEMOTRON-BENCHMARKS.md](NEMOTRON-BENCHMARKS.md) for test scope and limitations.
