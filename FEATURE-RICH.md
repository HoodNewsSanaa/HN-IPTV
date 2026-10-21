# HN-IPTV Feature Rich preview

Branch: `HN-IPTV-Feature-Rich`. This preview adds local translation and fullscreen focus fixes. It remains separate from stable main.

## Installation

Download the Windows artifact from this branch's **Windows builds and releases** run and run `HN-IPTV-Setup.exe` over the existing installation. Do not uninstall first. AppId, current-user scope, application mutex, previous install directory and LocalAppData/HN-IPTV data paths are preserved. Installer regressions include cached model and language-setting sentinels.

The portable ZIP keeps models in its existing data/translation folder. Updates preserve that folder. The installer contains integration code and setup scripts; the optional inference runtime and models download separately once.

## Translation

Use **Translation settings** to select Input and Output languages. Input Auto detects speech and text; Output Auto uses the Windows display language. Manual language codes override either selection and are saved.

Choose input language packs for the content you watch and select Download / prepare local models. Setup downloads a private, checksum-verified Python runtime, CPU inference libraries, a multilingual Whisper speech model, OCR models and translation packs. No system Python installation or account is required. Downloads can total several GB. Existing model files are reused rather than fetched on each launch or app update.

**Translate speech** captures only HN-IPTV's process tree using Windows application loopback (Windows 11 / build 20348+), processes overlapping five-second audio windows, and displays translated captions. It does not capture other apps or the microphone. Silence is skipped. Translations have processing delay and can be inaccurate for music, overlapping speakers, short phrases and accents.

**Translate text** reads frames and visible native captions directly from mpv. OCR never captures the desktop or app controls. It scans installed script models and translates recognized text into a separate overlay. Manual input language selects the matching model. Auto text recognition is limited to installed script models. OCR is CPU-based and may take several seconds per scan, especially when several scripts are installed. Small/moving/stylized text is less reliable.

English audio translation uses Whisper directly. Other audio output languages and all OCR translation use installed CTranslate2/SentencePiece language packs, with an English pivot where a direct pair is absent. Unsupported language/tokenizer combinations report an error rather than falling back to a cloud service.

Inference runs in separate hidden workers over local stdin/stdout, with network connections/DNS disabled. No audio or frames are uploaded or stored in diagnostics. Models stay warm while enabled and unload on Stop, tune, settings changes or disabling the feature. Queues retain the newest audio window to prevent unbounded delay. Translation overlays work in normal view, fullscreen and PiP.

Process loopback captures rendered audio, so muting/zero volume may produce silence depending on Windows audio behavior. This must be checked on the target device. CPU-only performance is not a universal latency guarantee.

## Fullscreen

Fullscreen no longer forces always-on-top. Returning from Alt-Tab reveals controls for three seconds and restores keyboard focus. Esc/F work from both the main window and the fullscreen controls. Exiting fullscreen retains the same native video window.

## Validation

The branch runs the existing package, playback and in-place installation suite plus:
- Fullscreen deactivation/reactivation and Esc regression.
- Native video-frame capture and Windows process-loopback activation.
- Translation settings persistence and old-result invalidation.
- Worker offline, cache and model-archive safety regressions.
- A separate real-model CPU test: generated spoken English and rendered screen text translated into Spanish with worker networking blocked.

Reports and installable Windows packages are attached as GitHub Actions artifacts. This is an experimental feature build; representative foreign live content and target-device performance still need evaluation.

Feature builds use the separate `feature-rich-v*` prerelease update channel. They do not replace GitHub’s latest stable release or automatically switch to the stable channel. Publication requires both the native installer/playback suite and real offline-model inference checks for the same source commit.

## NVIDIA GPU acceleration

Translation settings now offer `auto`, `cuda` and `cpu`. Preparing models with Auto detects an NVIDIA GPU and installs the official PyTorch 2.8 / CUDA 12.8 runtime privately; CPU mode skips this extra download. CUDA libraries require several additional GB and remain cached. No system Python, PATH or CUDA Toolkit changes are required.

Speech recognition, OCR and text translation use CUDA when available. Startup or inference failures (including GPU memory exhaustion) fall back to CPU and the app reports the active backend. Setup runs a short CUDA speech inference to check the libraries. On an RTX 4050 6 GB, start with `small`; `medium` is available for better recognition at increased memory and processing cost. GPU speed alone cannot improve the translation model itself. Decoding now considers more hypotheses. Explicit input language can help with short, noisy or multilingual clips.

Automated Windows tests exercise real CPU model inference plus CUDA-unavailable and out-of-memory fallback cases. Hosted CI has no NVIDIA GPU, so actual CUDA performance must be checked on GPU hardware.

## Translation performance and reliability in 0.11.4

Speech and screen translation share one worker and CUDA context. Models warm up before audio capture begins; the player shows preparation and errors inside the video overlay, including fullscreen. Audio uses phrase boundaries (at least 1.5 seconds, a short silence, or a 3.5-second maximum) with 0.75 seconds of context and a bounded four-segment queue. Captions keep recent context and empty results do not erase them. Three speech beams on CUDA (two on CPU) balance decoding cost and accuracy; model size still determines much of recognition quality.

Screen capture supports negative stride and retains up to 1920×1080 pixels. OCR joins words into lines, respects right-to-left scripts, and filters already-English station logos when translating foreign content to English. Text patches are checked independently of the moving background; unchanged text reuses its result between full scans. Screen translation remains active on paused video. Capture errors retry rather than silently terminating the loop.

The fullscreen and translation windows guard against reentrant activation and callbacks after closing, including fullscreen exit and PiP return. Toggle controls use a darker background, bright text and border when active; CC and mute also keep explicit on/off labels.

No new runtime dependencies or language-pack downloads are required when upgrading from a prepared 0.11.3 installation.

A local offline check on an RTX 4050 Laptop GPU (6 GB) successfully warmed both Arabic OCR and the medium speech model in one CUDA worker. With the models resident, the first translated Arabic frame took about 0.96 seconds (including text-model initialization); subsequent unchanged text over a modified background returned in 2–3 ms. The previous worker re-ran OCR at roughly 170–220 ms per warm frame. These are synthetic-fixture measurements, not end-to-end live caption latency; live audio still includes the capture window and model decoding time.
