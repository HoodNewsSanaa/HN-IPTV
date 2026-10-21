# HN-IPTV Feature Rich preview — 0.12.3

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

Captions refresh at phrase boundaries or every 1.5 seconds of continuous audio. Up to 12.5 seconds of preceding audio provides sentence context. Source words are confirmed across overlapping recognition passes before translation. Confirmed phrases become compact, timed pop-on cues rather than a repeatedly rewritten rolling paragraph. Each cue expires after its reading interval; a bounded queue discards stale cues. Confirmation adds processing delay and cannot guarantee correct recognition or translation. Only the newest queued segment is retained; stale queued audio is skipped. Audio capture starts during model warmup. Healthy idle workers and loaded models are reused across channel changes while translation remains enabled; old channel captions and language detection are reset. Settings changes, disabling both modes and canceled in-flight requests rebuild or release the worker. Recognition skips already confirmed old audio while retaining three seconds of acoustic context and all unconfirmed speech. Stable clause punctuation can release a confirmed phrase before the length limit. Caption delay includes collecting audio, word confirmation, model processing, any cloud request and UI scheduling. Simultaneous speakers, accents, music and quiet speech remain difficult.

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

Returning from Alt-Tab reveals fullscreen controls and restores keyboard focus. Esc/F exits fullscreen without replacing the native video window. Overlay callbacks guard against closed windows and reentrant activation, including PiP return. Translated speech and separate text captions move above the fullscreen toolbar while it is visible and return when it hides. Coordinate conversion accounts for display DPI. Toggle controls use darker active backgrounds; CC and mute retain explicit labels.

## Validation

The release gates include core/worker regressions, real private-runtime setup, English speech and screen translation into Spanish, the Spanish BPE pack, the new standalone Arabic OPUS pack, staged speech translation, frame geometry and caching, fullscreen/PiP lifecycle, native frame capture and app loopback, portable rollback, and base-to-feature installation/data preservation.

Local RTX 4050 checks cover actual Arabic OCR, shared medium CUDA inference, rolling speech behavior, silence gating, and a rendered positioned-overlay preview. CPU model profiles and pack validation are reproducible from the linked report. Hosted CI has no NVIDIA GPU; turbo and the new OPUS pack's CUDA timing remain unmeasured.

## Optional Azure Translator

Open **Translation settings → Azure**. Choose provider **azure**, paste **Key 1 or Key 2**, and enter **global** for a standalone Global Translator resource or its exact region code (for example `eastus`). Use **Test Azure connection**, then **Save**. The test translates only the five-character word Hello into Spanish. The app uses the public HTTPS Translator endpoint; private/VNET endpoints are not supported.

The key is masked and saved separately with Windows DPAPI encryption for the current Windows account. It is not written to translation-settings.json, Python arguments, the worker protocol or diagnostic logs. **Clear key field (applied on Save)** switches back to Local and removes the saved encrypted key and its backup when saved. Portable keys remain tied to the Windows account/machine that saved them; paste the key again after moving to a different account or PC.

Azure replaces the text translator for both confirmed speech phrases and screen OCR. Speech models and OCR still run locally. The worker has network access blocked and sends text-only requests to the Windows app; only the app authenticates to Azure. It does not upload audio, images or video. Azure speech always uses recognition followed by text translation, even if Direct English was selected for the Local provider.

Existing prepared recognition/OCR models and CUDA libraries are reused; no new model downloads are needed for an already prepared installation. For fresh Azure setup, prepare the speech and script models as before; local translation packs are skipped. Azure cannot fix a badly recognized transcript.

Repeated text is cached within the active session. Authentication/quota errors stop repeated HTTP attempts until restarting the session or updating settings; rate limits impose a cooldown. Errors do not expose provider response bodies or the key. No silent switch to a different cloud provider occurs. Select **local** to return to offline translation.

The F0 allowance, subscription and billing are managed in Azure; the app does not create a resource or change its pricing tier. See [Azure authentication](https://learn.microsoft.com/en-us/azure/ai-services/translator/text-translation/reference/authentication) and [Translator pricing](https://azure.microsoft.com/en-us/pricing/details/translator/).

Azure HTTP/header/region/cache/error/cancellation behavior is tested with synthetic keys and simulated responses. The private-runtime suite exercises real OCR through the app's C# text bridge, including geometry, cache reuse and sanitized failure propagation. Live Azure service/key validation requires the in-app connection test with your own key.
