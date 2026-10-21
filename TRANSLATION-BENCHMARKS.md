# Translation benchmarks — 2026-10-21

## Build decision

Use **turbo recognition with Silero speech detection**, followed by a separate **OPUS-MT/CTranslate2 Arabic-to-English translator**, as the prepared NVIDIA Auto configuration. Use **small** for CPU Auto to keep live processing responsive. Keep medium and all earlier manual selections available. Preserve existing settings; upgrading does not silently change a manually selected model. Auto uses already prepared models until model setup is requested.

OPUS-MT wins the tested Arabic-English text comparison narrowly over Argos at similar warm CPU latency. Turbo leads the Arabic recognition tradeoff, especially overlapping voices, rather than winning every condition. Medium remains stronger on the telephone-filtered test. Noise suppression is optional and off by default because its effects vary by model and condition. Vosk was evaluated as an alternative recognizer and did not win the robustness/speed tradeoff.

These are the strongest **tested components** for this small Arabic-first evaluation, not a universal accuracy ranking. A separate recognizer and translator can propagate recognition errors. The English live-caption accuracy of complete pipelines has not received a blinded human assessment. Turbo CUDA latency has not been measured on this workstation; medium/base CUDA have. Do not interpret CPU measurements as a promised GPU speedup.

## Data and reproducibility

- Six publicly licensed [Google FLEURS ar_eg test](https://huggingface.co/datasets/google/fleurs) recordings, CC-BY-4.0: 8.16–16.02 seconds, 128 normalized Arabic reference words total. All six carry the same gender label; the available Arabic test rows examined did not offer a second label. This is read speech, not representative live-TV dialogue or every Arabic dialect.
- Each clip is tested clean, with seeded Gaussian noise at 10 dB SNR, synthetic tonal/chord accompaniment at 5 dB, telephone-band filtering plus clipping, and another test voice at -6 dB. Thirty cases reuse six recordings and are not thirty independent speakers. The synthetic music is not a real music corpus.
- Thirty-two held-out [OPUS-100 Arabic-English test](https://huggingface.co/datasets/Helsinki-NLP/opus-100) text pairs, plus seeded letter/space deletion for 32 OCR-corruption cases. This sample is small; a nominal held-out split does not prove absence of overlap with upstream models' training data.
- Seed: 137. Input manifest SHA-256: `b348639d295ff8d602931c54cea325ddc587f0955046e6e58a084b07cec7a97d`.
- WER is normalized Arabic word error rate; lower is better. chrF is automatic character similarity to a single English reference; higher is better and does not equal human semantic accuracy.
- Speech models use faster-whisper 1.2.1 / CTranslate2 CPU int8, two threads, beam 2. Text models use CTranslate2 CPU float32, two threads; each model uses its documented decoder/tokenizer settings. Warm latency excludes initial downloads/conversion. Inference is offline in the main comparisons and the package validation.
- Reproduce with [compare_models.py](tools/benchmarks/compare_models.py), [live_profiles.py](tools/benchmarks/live_profiles.py), and [build_opus.py](tools/model-package/build_opus.py). [Baseline and optimized text run](https://github.com/HoodNewsSanaa/HN-IPTV/actions/runs/39579866889), [same-runner speech profile and compact package run](https://github.com/HoodNewsSanaa/HN-IPTV/actions/runs/39582933157). Full per-case predictions and timings are in those runs' artifacts.
- The benchmark JSON's legacy `download_bytes` field counts **model files on disk**, not network download size. The live-profile `startup_seconds` field includes the whole profile run and must not be treated as model startup time. Different hosted runners explain variations between runs; compare the same-runner tables below for speed.

## Recognition and speech gating: same runner

AMD EPYC 9V45 hosted CPU, two inference threads. Latency is warm median processing time per complete roughly 12-second clip, not delay from live speech to displayed caption. VAD in this CPU profile removes detected silent gaps; the app uses the same thresholds to gate fresh audio and trim the context window's outer silence.

| Profile | Clean WER % | Noise WER % | Synthetic music WER % | Telephone WER % | Competing voice WER % | Clean median |
|---|---:|---:|---:|---:|---:|---:|
| small-baseline | 17.2 | 25.8 | 16.4 | 18.8 | 37.5 | 1.42 s |
| small-vad | 16.4 | 26.6 | 14.1 | 18.8 | 38.3 | 1.39 s |
| medium-baseline | 8.6 | 13.3 | 9.4 | 10.2 | 18.0 | 3.90 s |
| medium-vad | 7.8 | 14.1 | 7.8 | 7.0 | 18.0 | 3.85 s |
| turbo-baseline | 7.8 | 13.3 | 8.6 | 7.8 | 10.2 | 3.46 s |
| turbo-vad | 7.0 | 12.5 | 7.8 | 9.4 | 12.5 | 3.49 s |
| turbo-vad_denoise | 7.8 | 12.5 | 7.8 | 9.4 | 14.1 | 3.51 s |

Turbo baseline produced three inserted words on each of the silence, noise-only and pure-tone probes. With VAD, all three probes produced no words and returned in approximately 3–5 ms rather than approximately 3 seconds. These are three simple probes, not a complete hallucination benchmark. VAD does not separate simultaneous speakers or remove music underneath dialogue.

Model-file footprints: small 486 MB, medium 1,531 MB, turbo 1,622 MB. Peak process RSS across the sequential run reached approximately 2,546 MiB for turbo; RSS is a high-water mark and does not measure CUDA memory.

For context, the earlier separate-runner comparison gave base 34.4% clean / 50.8% noise / 57.8% competing-voice WER, and Vosk Arabic MGB2 14.8% / 46.9% / 57.0%. Vosk's clean median was 7.14 seconds. Its model expands to 698 MB from a roughly 318 MB archive. Those runner speeds are not a controlled same-hardware comparison with this table.

## Text translation: same runner

AMD EPYC 9V45 CPU, same runner, separate process per model. Beam 3 for OPUS/NLLB, Argos beam 4 and its original length penalty. NLLB's English target prefix and language tokenizer are applied.

| Model/runtime | Clean chrF | Corrupted chrF | Clean median | Clean p95 | Model files | Peak process RSS |
|---|---:|---:|---:|---:|---:|---:|
| argos | 53.78 | 48.30 | 80 ms | 188 ms | 96 MB | 692 MiB |
| opus-ct2 | 54.79 | 49.94 | 78 ms | 188 ms | 310 MB | 985 MiB |
| nllb-ct2 | 53.66 | 44.00 | 689 ms | 1660 ms | 2470 MB | 3917 MiB |

The initial PyTorch OPUS/NLLB pass was replaced by this optimized CTranslate2 comparison to avoid selecting a winner from unequal inference engines. Argos remains a useful smaller fallback. No conclusion about other language pairs is justified by this Arabic-English table.

### Compact deployed OPUS pack

Pinned source: [Helsinki-NLP/opus-mt-ar-en at c5b2a50db78d6be98ae207223f8d4f63bc7a0ff1](https://huggingface.co/Helsinki-NLP/opus-mt-ar-en/tree/c5b2a50db78d6be98ae207223f8d4f63bc7a0ff1). Apache-2.0 license, upstream model card, NOTICE and metadata are included.

Float16 weights reduce model files from approximately 310 MB to **157 MB**, with a **143 MB download** (decimal units). CPU loads those weights as float32; CUDA uses float16. SentencePiece source/target tokenizers were verified against MarianTokenizer, including source EOS. The converted CTranslate2 config is preserved.

Offline package verification scored **54.79 clean chrF / 49.57 corrupted chrF**, within the acceptance tolerance of the float32 comparison. Package validation ran on another hosted runner, so its approximately 190 ms median is not evidence that float16 changes inference speed. SHA-256: `3dc65d3ee89b5cfd26d9dfd07bd0b72f4eea956ddba495e9efa3f6ad1d14fbe3`. [Optional pack release](https://github.com/HoodNewsSanaa/HN-IPTV/releases/tag/translation-models-v1). No Transformers dependency is added to the installed app.

## Local RTX 4050 checks and live behavior

An RTX 4050 Laptop GPU with 6 GB VRAM successfully held Arabic OCR and medium recognition in the same worker. With medium, VAD's complete-clip clean WER was 7.8% versus 8.6% without VAD. Local warm complete-clip processing was approximately 1.4–1.5 seconds; power/thermal state and a concurrent warmup during the baseline make these indicative timings rather than a controlled GPU speed comparison. Base and medium were prepared locally; turbo and the new OPUS CUDA pack were not, so their GPU performance remains unmeasured.

Short independent windows damaged recognition: 3.5 seconds with 0.75 seconds of overlap yielded 28.9% merged-transcript WER; 2.5 seconds with 0.5 seconds of overlap yielded 52.3%. Longer windows also exposed duplicate-append errors. The build therefore retains up to 12.5 seconds of preceding audio and refreshes every 2.5 seconds, replaces the rolling caption instead of appending revisions, checks speech in newly arrived audio, and retains only the newest queued chunk. Old queued chunks are discarded after four seconds. The rolling caption policy is a response to these findings; its final live semantic accuracy is not established by the independent-window WER figures.

A one-clip real worker check with medium CUDA and the existing Argos pack returned direct-English rolling results in approximately 0.30–0.56 seconds and staged results in 0.63–0.84 seconds after warmup. Those times exclude audio collection and UI scheduling. New silence with old speech in context correctly produced no fresh caption. This is a functionality/latency probe, not a scored comparison of final English accuracy.

The positioned overlay was rendered with an actual synthetic Arabic OCR result: fitted English masks cover the original lettering, while speech captions move away from the translated bottom banner. Frame geometry, letterboxing, jitter tracking, stale-region removal, fullscreen exit and PiP return have regression coverage. Existing Argos GPU screen inference/cache checks passed; unchanged text reuses translated regions instead of rerunning OCR on each moving background frame.

## Limits

Six Arabic read-speech clips and 32 translation pairs cannot establish performance across accents, dialects, languages, sports crowds, songs, sustained crosstalk or stylized screen graphics. Real subtitles/clean audio tracks, when supplied by the stream, may be more accurate than recognition. GPU acceleration reduces processing cost; it cannot restore words masked by another speaker. Conservative spectral suppression is an optional filter, not a dialogue-separation model.
