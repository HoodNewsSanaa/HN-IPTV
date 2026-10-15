# Update 8 validation

See [VALIDATION-0.8.md](VALIDATION-0.8.md) for the current build. Everything below is historical and does not establish Update 8 runtime validation.

# 0.7.1 hotfix validation

The user's Windows Runtime event confirms `ObjectDisposedException` in `MainWindow.ChannelsList_SelectionChanged`. The underlying cancel/dispose/cancel sequence was reproduced locally using the PowerShell .NET runtime. The hotfix clears the owning fields and adds actual-window regressions to the existing Windows smoke suite. Local hotfix checks: the complete WPF app and updated Windows smoke-test harness cross-compiled in Release for win-x64 with zero warnings/errors; core smoke tests passed; seven installer-orchestration simulations passed; the actual cancellation helper cancelled its token, cleared ownership and accepted 100 repeated calls. Native WPF execution remains pending on GitHub Actions/Windows. Portable startup is not established by the installed-edition crash log.

The historical 0.7.0 verification below predates this hotfix and does not establish that 0.7.1 was compiled or tested.

# Update 7 validation status

This is a compiled Windows release candidate, not a fully device-tested release. Built from the latest saved Update 6 archive (version 2). The GitHub URL returned an unauthenticated 404 in this environment; no remote branch, commit, workflow or release was changed. Reconcile any newer repository work before uploading.

## Completed here

- Ran the unchanged Update 6 core smoke suite successfully and cross-published its full Windows application as the comparison baseline.
- Ran Update 7 core regression tests successfully, including 50,000-channel M3U and mocked Xtream catalogs, authentication/format/EPG/cancellation, filtering, preferences, portable persistence, snapshot stability, atomic write failure/backup, corrupted-data preservation and save coalescing/error recovery.
- Ran the WPF progress-binding source check successfully.
- Cross-compiled the complete Windows app and Windows runtime-test harness in Release, with zero warnings and zero errors in the recorded final compile.
- Cross-published the self-contained Windows x64 application and created the complete portable ZIP and SHA-256 checksum.
- Cross-published the optional ReadyToRun variant for size comparison. No startup result is inferred from that build.
- Parsed PowerShell scripts and workflow YAML, and validated package dependencies, architecture exclusions and absence of user data. This does not establish that Inno Setup or hosted Windows CI has run successfully.
- Measured baseline/update component and ZIP sizes and synthetic core timings. See PERFORMANCE.md and validation/ for the evidence and limits.

## Not verified in this Linux environment

- Native WPF launch, startup timing, display/GPU rendering, audio, playback memory or the originally reported freeze.
- Actual HLS/MPEG-TS playback, repeated minimize/restore, fast channel switches, fullscreen, DPI/multiple monitors, sleep/wake and graceful native shutdown.
- A real provider's authentication, huge network catalog, EPG, logos or refresh behavior. Network behavior was tested with controlled HTTP fixtures, not provider credentials.
- A Windows installer binary, installation/uninstallation, optional desktop shortcut, interactive data-removal prompt, upgrade from the actual Update 6 installation or its installed size.
- Real USB-drive portability, Windows DPAPI behavior or Windows antivirus/SmartScreen behavior.

The Windows workflow now runs a deterministic local HLS/TS fixture against the actual application window, checks playback progress across ten minimize/restores and fullscreen, and reports startup/restore timings. It also installs/reinstalls/uninstalls in the disposable runner account and checks saved-data preservation. These checks were added and cross-compiled; **they have not run here**. The reinstall check uses the same new package and is not a substitute for a real Update 6 upgrade test.

## Before publishing a stable release

1. Run Windows builds and releases manually in GitHub Actions, inspect both validation reports and download both packages. Do not bypass a failing check.
2. Test both distributions on Windows with an authorized provider and a large M3U file. Verify saved playlists, favorites, hidden groups, separate searches, long-press interactions, logos, EPG and manual/scheduled refresh across restarts.
3. Run minimize/restore and fullscreen repeatedly while video and audio play. Check the native picture visually, not just the playback clock. Compare startup and memory against Update 6 on the same machine.
4. Install over Update 6 and confirm saved data survives. Test both choices in the interactive uninstaller in a disposable account. Test portable replacement while retaining data, and moving the entire folder to another writable Windows location.
5. Review PORTABILITY.md. Old password-encrypted vault files are preserved, but cannot be read without the old password mechanism; export/re-add profiles using Update 6 before retiring it. Preferences and local M3U files are retained where available. Plain portable credentials must stay out of GitHub and shared diagnostics.

No built-in updater, Stalker portal or external automatic XMLTV matching utility was added. Release distribution remains manual for users.

## Installer-test correction (2026-10-15)

The user reported that Windows Actions reached and passed the original install/reinstall/silent-uninstall assertions, then failed because the finally block ran the uninstaller again. This does not constitute a successful complete workflow or manual Windows playback test.

The revised script parses successfully and passes seven orchestration regressions on PowerShell 7.5.4/Linux using simulated processes and registry state. The unchanged old script reproduces the duplicate-uninstall failure under the same harness. No application C# or installer recipe changed in this correction. The corrected native installer run and complete workflow still need Windows Actions; they have not been run in this session. See INSTALLER-TEST-FIX.md.
