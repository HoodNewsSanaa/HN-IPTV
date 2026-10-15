# Update 8 verification limits

Completed locally in Linux with .NET SDK 10.0.402 / runtime 10.0.13:

- Compiled the full Windows application with each playback backend and compiled/published the Windows test harness against each published app. Verified assembly hashes match the app under test.
- Ran core regression checks, including large catalogs, mocked provider API, persistence and save queues, plus selected EPG priority/cancellation, PiP/buffer settings and diagnostic privacy.
- Ran seven installer orchestration simulations, PowerShell parser checks and workflow YAML parsing.
- Verified required release dependencies, x64-only engine contents, absence of user data/debug symbols, native PE section integrity, and rejection of a deliberately truncated runtime DLL.
- Published the self-contained VLC Windows portable package and measured payload/ZIP sizes. Built the mpv comparison payload and measured its size separately.

Not executed here: native Windows launch or playback; PiP video/audio/resize/return; fullscreen; minimize/restore; fast switching and empty-category UI regressions; installer compilation, installation, upgrade or uninstall; startup and playback memory/CPU timings; live provider, GPU, logo or EPG checks. The Windows harness was cross-compiled, not run. Synthetic core timings do not establish UI or playback improvements.

The new Windows CI checks are included but have not been triggered from this environment. Installer upgrade validation uses an older-version registration fixture with the new payload; an actual historical installation and real saved profile still need an upgrade trial. No Windows installer binary is supplied from Linux.

During comparison an independently rebuilt baseline runtime DLL was found truncated, restored byte-for-byte from the same runtime pack, and rechecked before measurement. All new payload PE files passed integrity checks. Incremental build symbols were also detected and excluded before packaging. These are local packaging findings, not evidence of the cause of the user's earlier portable startup issue.

Treat this package as a release candidate until the Windows checks and a real-device trial pass. Do not infer a stable mpv result from successful cross-compilation.
