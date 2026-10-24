# Feature Rich 0.16.0

Whisper Medium Q5 recognizes speech and translates directly into English on the device. Azure is removed. Old provider settings normalize to local English translation. Existing Medium and Silero downloads are reused; prepare Whisper in settings if upgrading from a Turbo-only installation. No translation key or cloud translation request is used.

Open Info for Whisper state, the input language reported by Whisper, any manual input override, and its latest English output. The latest output is generated ahead of the displayed video in buffered mode and is labeled accordingly. It is held in memory, limited in length, never written to diagnostics, and cleared on stop, channel changes and settings restarts. An unknown language is shown as unknown rather than guessed from your Windows language.

The existing adaptive speech boundaries, Vulkan/CPU acceleration, Live and synchronized 10/15-second modes remain. Whisper only translates into English. CPU processing can cause additional buffering; a 15-second target is not a hard latency ceiling.

## Startup updates

Each ordinary launch checks the Feature Rich releases before showing the main window. The startup panel offers progress and Continue without updating. The check has a short timeout; unavailable networks and invalid update metadata fall back to the installed app. Downloads can be cancelled. A verified newer version installs silently and relaunches; the update relaunch bypasses one check to avoid loops. A failed installed update has a one-hour retry cooldown. The manual update button remains available.

Automatic execution requires the SHA-256 published in release notes to match GitHub's asset digest. Downloads use fixed repository/channel asset paths over HTTPS and are size-limited. The installer is hashed again and locked against replacement before execution. Installation runs as the current user with no elevation request. Portable updates retain file rollback and preserve the data directory. Installed updates use the existing Inno Setup installer and reopen the app after an installation failure; a severely damaged existing installation may still require running setup manually.

These releases are not Authenticode signed. The security model trusts this GitHub repository, its release workflow and HTTPS; digests are not an independent publisher signature and cannot protect against a compromised publishing account. Stronger publisher authentication requires establishing code signing or an independently secured signing key.

The startup updater does not run against test data. Model/playlist/credential contents are never included in update metadata or logs. Existing unused model downloads and encrypted credential files are not read or automatically removed by the new translation flow.

## Timeshift

The translation buffer holds media received during this session; it is not provider catch-up TV. Provider catch-up/archive flags and historical playback are not currently implemented, so no claim is made that a channel supports archive playback.
