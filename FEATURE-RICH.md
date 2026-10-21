# HN-IPTV Feature Rich preview — 0.13.0

Branch: `HN-IPTV-Feature-Rich`.

## Translation

The pipeline is **Nemotron 3.5 ASR Streaming Q8 → Azure Translator → timed captions**.

Recognition uses one multilingual local model (741,548,352 bytes), with automatic or manual input language. Output Auto uses the Windows display language. Language coverage and accuracy vary; some languages are experimental. Azure translates recognized text only; audio stays on this PC.

Open Translation settings to prepare Nemotron, choose Auto/CUDA/CPU, and paste the Azure Translator key and region in the Azure tab. Existing encrypted Azure credentials are preserved. Azure requires Internet access and consumes your resource's text-translation quota. Cloud response time and phrase context still contribute to caption delay.

The native Windows runtime replaces Python, PyTorch, Whisper, Argos, OPUS and EasyOCR. CPU runtime download is about 5.5 MB; optional CUDA runtime about 102 MB. The model downloads separately once. Driver compatibility is checked by actual inference, with native CPU fallback for Auto. This build does not require the old multi-gigabyte PyTorch CUDA installation.

Fresh audio is sent about every 320 ms. The native stream retains acoustic context, so old audio is not repeatedly decoded. Recognition and Azure translation run independently with bounded queues. Stable phrases become compact pop-on cues; unfinished trailing word fragments are withheld. Captions still rise above fullscreen controls.

Screen-text OCR translation has been retired as part of removing the old local language stack. Channel-provided CC remains available.

## Upgrade and cleanup

Run HN-IPTV-Setup.exe over the existing installation. Do not uninstall first. App identity, playlists, preferences and Azure credentials are preserved.

When old language downloads exist, first launch prepares the replacement. Old downloads are deleted **only after** the checksum-verified Nemotron model completes real native inference. The cleanup targets only the app-owned `translation` directory, including its private Python, recognition/OCR/translation models, caches and PyTorch CUDA libraries. It refuses linked directories. If setup fails, old data is retained and Translation settings can retry. Files locked by another app instance require closing it and retrying setup.

The new `speech-nemotron` directory contains the model and native runtimes. Downloaded third-party license notices remain beside their runtime. Portable installations use their own data directory.

Feature builds use the `feature-rich-v*` prerelease channel. Native playback/fullscreen/PiP, installer upgrades, core checks and real native speech inference must pass before publication.

See [NEMOTRON-BENCHMARKS.md](NEMOTRON-BENCHMARKS.md) for test scope and limitations.
