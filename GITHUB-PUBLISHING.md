# Publish Update 8

1. Extract `HN-IPTV-0.8.0-Source.zip` and upload its contents to the existing repository, preserving folders including `.github/workflows`. Do not upload `data`, credentials, caches, build outputs or native DLL downloads.
2. Commit to `main` or `master`. Open Actions and wait for **Windows builds and releases** to pass. Check its Windows UI and upgrade-test results. These checks cannot run in the Linux development environment.
3. Download the Windows artifact for a local trial. Install the new setup over the older installation; do not uninstall first. Verify your saved data, channel/category switching, PiP, minimize/restore, fullscreen, provider EPG/logos and restart.
4. When ready, open Actions → **Windows builds and releases** → Run workflow. Enter version `0.8.0`, enable **publish_release**, and run it. All checks run before the packages are published.
5. Alternatively create/publish release `v0.8.0` on the updated commit. The release-published workflow attaches its successfully tested assets. Pushing a tag alone does not publish in this workflow.
6. The release contains `HN-IPTV-Setup.exe` and `HN-IPTV-Portable.zip`. SHA-256 checksums appear in its notes and in `SHA256SUMS.txt` inside the Actions artifact. Source archives are added automatically by GitHub.

Development branch builds use quicker compression; published releases use stronger compression. The tested publish is reused for packaging, not rebuilt. Release creation uses GitHub's workflow token; the installed application never contacts GitHub for updates.

The optional **Compare VLC and mpv on Windows** workflow runs three synthetic trials per engine on the same runner and uploads evidence only. It does not publish mpv or change the default engine. Graphics, real providers and long-session behavior still need device testing.
