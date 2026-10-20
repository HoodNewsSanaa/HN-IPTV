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
