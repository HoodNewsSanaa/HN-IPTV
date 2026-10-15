# Manual updates without uninstalling

## Installed edition

1. Download the newer `HN-IPTV-Setup.exe` from GitHub Releases.
2. Close HN-IPTV so it can save its state. Run setup using the same Windows account as before.
3. Follow setup. It reuses the existing installation location and replaces the application files. **Do not uninstall the old version first.**
4. Start HN-IPTV normally. Playlists, provider credentials, favorites, group visibility, settings and caches remain under `%LOCALAPPDATA%\HN-IPTV`.

The existing installer AppId `{CD93EEDF-5518-4324-92BD-06600458F64C}` is unchanged, and setup remains per-user. Installation files normally live in `%LOCALAPPDATA%\Programs\HN-IPTV`. An older copy installed by another account or an unrelated installer identity is outside this same-account upgrade path.

The installer never deletes the user-data directory during installation or upgrade. Uninstall offers optional data deletion interactively; silent uninstall preserves it. There is no application update checker or downloader.

## Portable edition

Close HN-IPTV, back up the portable folder, and replace application files with the complete contents of the newer portable ZIP. Retain the existing `data` directory and `portable.mode`. Do not copy a single EXE alone or extract the new application inside the old `data` directory. Portable credentials remain unprotected by a master password; keep backups and the complete folder private.

## Validation

Windows CI installs a 0.7.1-version registration fixture, creates synthetic user data, installs the new package without `/DIR`, checks the new registered version and original location/data, reinstalls, then silently uninstalls. The fixture uses the new payload with an older installer version: it tests installer mechanics, not the historical 0.7.1 binary or a real user's profile. An upgrade from your actual older installation remains a Windows manual check until performed.
