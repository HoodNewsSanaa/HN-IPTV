# HN-IPTV Feature Rich preview — 0.12.0

Branch: `HN-IPTV-Feature-Rich`. Stable main remains separate.

## Installation and updates

Run the new HN-IPTV-Setup.exe over the existing installation. Do not uninstall first. The AppId, per-user install location, mutex and LocalAppData/HN-IPTV data directory are preserved. Settings, playlists, cached models and CUDA libraries survive upgrades. Portable updates preserve their data directory.

Feature builds use the `feature-rich-v*` prerelease update channel. Optional `translation-models-v*` releases are model assets and are never selected as application updates. Publication requires native playback/fullscreen/PiP/installer checks and real offline model inference for the same source commit.

## Recommended translation setup

Open **Translation settings**, choose the input packs you watch, and select **Download / prepare local models**. Input Auto detects the language; manual input and output codes remain available. Output Auto uses your Windows display language.

Speech model **auto** prepares turbo on an available NVIDIA CUDA device, or small on CPU. Turbo transcribes the original speech and then uses a separate translator. For Arabic, Auto also uses separate recognition and translation with older speech models. Direct English translation remains selectable with non-turbo models. Other output languages always require a text translator. The manual tiny/base/small/medium choices remain available; existing saved selections are preserved.

Auto text translation uses the compact OPUS-MT pack for **Arabic to English**, and Argos for other pairs, with an English pivot when required. The optional OPUS download is approximately **143 MB**, expands to approximately 157 MB, and is checksum verified. It uses existing CTranslate2/SentencePiece dependencies. Argos is also selectable manually. If OPUS has not been prepared, an existing cached Arabic Argos pack remains usable and the app reports that fallback.

Selecting Auto does not download models during playback. It selects an already prepared model until you run setup. Turbo model files total approximately 1.62 GB and download once; existing medium/base models are retained. CUDA libraries are reused. A fresh private runtime plus CUDA, recognition and OCR models can still require several GB.

The [benchmark report](TRANSLATION-BENCHMARKS.md) explains why turbo + VAD and OPUS were selected for the tested Arabic case, and where medium or smaller models remain useful. Turbo is not a promise of universally faster or more accurate captions.

## Spoken dialogue

**Translate speech** captures only this app's process tree using Windows application loopback (Windows 11 / build 20348+). It does not capture the microphone or other apps.

Silero VAD checks fresh audio for speech before recognition. Low-confidence/repetitive recognition segments are suppressed. Auto can retain a confident detected language briefly, with periodic redetection; a manual language is useful for noisy or short clips. Optional **Reduce steady background noise** applies conservative spectral attenuation, off by default because filtering can also damage recognition.

Captions refresh at phrase boundaries or every 2.5 seconds of continuous audio. Up to 12.5 seconds of preceding audio provides sentence context. A revised rolling translation replaces the previous caption instead of appending duplicate fragments. Empty results preserve readable captions. Only the newest queued segment is retained; stale queued audio is skipped. Caption delay includes collecting audio, model processing and UI scheduling. Simultaneous speakers, accents, music and quiet speech remain difficult.

Process loopback captures rendered audio, so mute/zero volume may produce silence depending on Windows audio behavior.

## Text inside the player

**Translate text** reads frames directly from mpv, including visible native captions. OCR never captures your desktop or app controls.

The default **positioned** overlay keeps OCR coordinates, covers source lettering with sampled background-colored masks, and fits the translated text into that area. It tracks small position changes, tolerates one missed detection, clips to the video viewport, respects letterboxing, and works in normal view, fullscreen and PiP. This is an opaque text replacement overlay, not generative image reconstruction.

Speech captions remain separate and move away from translated bottom banners. Choose **captions** in settings to keep the earlier separate text bar. Auto recognition scans prepared script models; manual input selects a script directly. Tiny, moving, rotated or stylized lettering may be missed. A flat mask cannot perfectly reconstruct a complex moving background.

Text patches are checked independently of the moving video background. Unchanged words reuse cached translations and geometry. Full scans refresh periodically, paused frames remain translatable, and frame capture errors retry.

## Hardware and privacy

Acceleration choices are **auto**, **cuda** and **cpu**. Auto detects NVIDIA hardware during setup and optionally installs official private PyTorch/CUDA libraries. CPU skips that extra download. No system Python, PATH or CUDA Toolkit changes are required.

Speech recognition, OCR and text translation share one hidden worker/CUDA context and warm up before audio capture. CUDA load or memory failures fall back to CPU and show the backend. CPU performance may require a smaller recognition model or disabling simultaneous OCR.

Inference communicates through local stdin/stdout with network connections and DNS blocked. Audio and frames are not uploaded or stored in diagnostics. Only explicit model setup downloads data. Workers unload on Stop, tuning, settings changes or disabling translation.

## Fullscreen and controls

Returning from Alt-Tab reveals fullscreen controls and restores keyboard focus. Esc/F exits fullscreen without replacing the native video window. Overlay callbacks guard against closed windows and reentrant activation, including PiP return. Toggle controls use darker active backgrounds; CC and mute retain explicit labels.

## Validation

The release gates include core/worker regressions, real private-runtime setup, English speech and screen translation into Spanish, the Spanish BPE pack, the new standalone Arabic OPUS pack, staged speech translation, frame geometry and caching, fullscreen/PiP lifecycle, native frame capture and app loopback, portable rollback, and base-to-feature installation/data preservation.

Local RTX 4050 checks cover actual Arabic OCR, shared medium CUDA inference, rolling speech behavior, silence gating, and a rendered positioned-overlay preview. CPU model profiles and pack validation are reproducible from the linked report. Hosted CI has no NVIDIA GPU; turbo and the new OPUS pack's CUDA timing remain unmeasured.
