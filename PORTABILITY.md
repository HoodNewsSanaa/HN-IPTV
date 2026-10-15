# Portable storage and manual upgrades

## Portable edition

Extract the whole portable ZIP to a writable folder. Keep `portable.mode`, the EXE, its DLLs, `libvlc`, and `data` together. Nothing needs to be registered or installed. Data is saved to `data/saved-playlists.json`, `data/channel-preferences.json`, `data/playlists/`, and `data/logos/`. Provider EPG is part of the saved library. The folder can be moved to another Windows computer or USB drive.

There is no master password or portable vault in Update 7. Anyone who can read the portable folder can read provider credentials and credential-bearing M3U URLs. Its `.bak` files have the same sensitivity. Keep the folder private; use device/disk encryption if appropriate. Never commit, upload, email, or attach your `data` folder or imported playlists to a public repository. The packaged releases contain an empty data directory only. Diagnostics record operation names, types and timings, not credentials or URLs.

For a manual update: close HN-IPTV, back up the complete folder, then copy the new application files into the existing portable folder while retaining `data`. Do not mirror-delete the old folder against a fresh release ZIP. Do not run directly inside the ZIP.

## Existing portable data from Update 6

The encrypted `saved-playlists.vault` is left untouched. Encryption cannot be removed without its password; Update 7 intentionally has no unlock UI or decryption code. It opens immediately and displays a status notice if an old vault is present. It writes a separate `saved-playlists.json`, never overwriting the vault.

Keep a backup and the old Update 6 application to access its encrypted profiles. Re-add accounts in Update 7 using the same server address and username; the existing preferences file preserves matching Xtream favorites. Re-import existing M3U files from `data/playlists` to preserve their relative paths and favorites identity. Existing logos/preferences remain in place. Settings held only inside the encrypted vault—including saved profile names, hidden groups and refresh settings—cannot be imported automatically and must be re-entered. Retain the old vault until migration is checked. No legacy recovery or password system is included in this build.

Unencrypted JSON created by Update 7 moves automatically with the folder. Installed DPAPI data is intentionally user-bound and is not a portable export format.

## Installed edition

The installer uses the existing application ID and `%LOCALAPPDATA%/Programs/HN-IPTV` installation path. Saved playlists remain in `%LOCALAPPDATA%/HN-IPTV/saved-playlists.dpapi`, protected with Windows DPAPI. Install a newer setup over the old version. The installer replaces program files and removes redundant old VLC architectures; it does not delete user data during an upgrade. Earlier LumaTV playlist/preferences files are copied on first use when no HN-IPTV copy exists.

Interactive uninstall asks whether to remove saved data. Silent uninstall keeps it. The Start Menu shortcut is created automatically; the desktop shortcut is optional.

## Interrupted or corrupt saves

Writes use a temporary file and atomic replacement, keeping the previous good file as `.bak`. An unreadable library/preferences file is never silently replaced by an empty one. Close the app, preserve the damaged file separately, and restore the corresponding `.bak` under the original filename if needed. Check the recovered data before deleting backups. Installed DPAPI backups require the same Windows user.
