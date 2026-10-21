# Third-party components

- Microsoft .NET 10 / WPF. The self-contained publish includes Microsoft's `LICENSE.txt` and `ThirdPartyNotices.txt`.
- LibVLCSharp 3.10.2: the NuGet package declares `LGPL-2.1-or-later`. Source: https://code.videolan.org/videolan/LibVLCSharp ; mirror/tag: https://github.com/videolan/libvlcsharp/tree/3.10.2 .
- VideoLAN.LibVLC.Windows 3.0.24: the NuGet package declares `LGPL-2.1-or-later`. Package build/source information: https://code.videolan.org/videolan/libvlc-nuget ; VLC source releases: https://download.videolan.org/pub/videolan/vlc/3.0.24/ . The complete x64 plugin set is retained. VLC includes components with their own notices; retain source-package disclosures when redistributing modified native libraries.
- LGPL 2.1 text is included in `licenses/LGPL-2.1.txt`. Native libraries remain separate DLLs.
- The vintage television artwork is unchanged from Update 6. No new artwork is supplied by this update.
- The small HLS/MPEG-TS test fixtures are generated color patterns and a sine tone, produced with FFmpeg solely for regression testing. They contain no provider streams or account data and are excluded from application packages.

No project-wide license was added or changed by this update.


mpv is the default engine. VLC packages and notices above apply to optional VLC builds. Only the selected native engine is included in a package.

## mpv package

The standard mpv build includes the unmodified libmpv-2.dll pinned by mpv-build.json. GPL version 3 text is included in licenses/GPL-3.0.txt.

- mpv source revision: https://github.com/mpv-player/mpv/tree/492bae0768
- Upstream binary: https://github.com/shinchiro/mpv-winbuild-cmake/releases/tag/20261015
- Build scripts and dependency recipes: https://github.com/shinchiro/mpv-winbuild-cmake/tree/1f8d2e7
- HN-IPTV application and embedding source: https://github.com/HoodNewsSanaa/HN-IPTV

The bootstrap manifest records exact archive and DLL hashes. Native libraries remain separate and replaceable. Application source and build scripts are available in the linked HN-IPTV repository.

## Optional translation models in Feature Rich 0.12

The separately downloaded OPUS pack is derived from [Helsinki-NLP/opus-mt-ar-en](https://huggingface.co/Helsinki-NLP/opus-mt-ar-en), revision `c5b2a50db78d6be98ae207223f8d4f63bc7a0ff1`, under Apache-2.0. The archive includes the upstream model card, Apache license, NOTICE, source/target SentencePiece models and conversion metadata. Model weights are converted to CTranslate2 float16; inference uses CPU float32 or CUDA float16.

Optional multilingual turbo recognition uses [mobiuslabsgmbh/faster-whisper-large-v3-turbo](https://huggingface.co/mobiuslabsgmbh/faster-whisper-large-v3-turbo), a CTranslate2 conversion of OpenAI Whisper large-v3-turbo (MIT). Silero VAD is bundled by faster-whisper; its upstream attribution remains in that dependency. No Transformers package is installed in the app runtime.

The public benchmark uses [Google FLEURS](https://huggingface.co/datasets/google/fleurs) Arabic test recordings (CC-BY-4.0) and [OPUS-100](https://huggingface.co/datasets/Helsinki-NLP/opus-100) Arabic-English test text. These fixtures are evaluation data and are not packaged in the installer. See TRANSLATION-BENCHMARKS.md for scope, metrics and limitations.

## Native streaming speech (0.13.1)

NeMo-Speech.cpp v0.2.0 is downloaded separately from NVIDIA's official release under Apache-2.0, with bundled third-party notices retained in the downloaded SDK. Nemotron 3.5 ASR Streaming 0.6B Q8 weights use OpenMDW 1.1; see https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b. The app pins the model revision and verifies SHA-256. No weights are embedded in the installer. Azure Translator is an external Microsoft service configured with the user's own account.

## Optional buffered media transport

Generous time downloads Gyan's FFmpeg 9.0.2 essentials Windows build from https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2 and verifies SHA-256 60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba. The FFmpeg executable runs as a separate process; its bundled license and documentation are retained in the downloaded archive directory. FFmpeg project and source information: https://ffmpeg.org/ and https://www.gyan.dev/ffmpeg/builds/.
