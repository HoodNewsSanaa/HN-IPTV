# Native Nemotron trial — 2026-10-21

Tested NVIDIA's Nemotron 3.5 ASR Streaming 0.6B Q8 GGUF, revision `1c8deaecc64b91f034d73e08dd8b64625eb3395d`, SHA-256 `a5c435f294eea8f88ce68dd27b8c3bfea7f777cb2fbba04fcd30eaa555f429ae`, using NeMo-Speech.cpp v0.2.0's stable C ABI on a GitHub Windows CPU runner.

Four original FLEURS test recordings (Google, CC-BY-4.0), one each in English, Spanish, German and Arabic, were submitted in 320 ms increments. Each was tested with manual input and Auto. No Azure key or user audio was used.

| Language | Audio | Recognition time / audio time | First partial at capture time | Raw word error rate |
|---|---:|---:|---:|---:|
| en_us | 6.18 s | 0.42 | 1.60 s | 26.7% |
| es_419 | 16.14 s | 0.42 | 1.92 s | 2.4% |
| de_de | 4.80 s | 0.41 | 2.24 s | 30.0% |
| ar_eg | 9.32 s | 0.42 | 0.96 s | 10.0% |

All samples produced intelligible streaming transcripts faster than real time. Manual and automatic input produced the same final text in these four cases. Raw WER retains diacritics and word-splitting differences, so it is sensitive to Arabic vowel marks and English compounds.

The first partial is often an incomplete word and is not the time to a translated caption. The app waits for stable phrase text before sending Azure requests. Initial loading is memory mapped; the trial's approximately 26 ms create time excludes first-inference initialization and is not a full startup benchmark.

These are four short clean clips, not a representative accuracy benchmark. No claim is made about superiority over Whisper, all supported languages, noisy broadcasts, or end-to-end Azure latency. CUDA execution was not tested on the hosted CPU runner. The app validates the chosen backend on the user's PC before deleting the legacy data; Auto can use the tested CPU path if CUDA cannot initialize.

[Trial run](https://github.com/HoodNewsSanaa/HN-IPTV/actions/runs/39617019347). [Machine-readable results](validation/nemotron-trial.json). Test script: `tests/nemotron_trial.py`.
