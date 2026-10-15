# Start a fresh HN-IPTV Windows repository

Target: https://github.com/HoodNewsSanaa/HN-IPTV

This clean package contains Windows source and its workflow. Android files, its workflow and the old Update-6-to-7 patch are omitted. No private signing keys, provider data or compiled packages are included. The application ID and installed data paths remain unchanged so existing Windows users can retain their data.

## Delete the old repository

Deletion has NOT been performed by the assistant: this session has no connected GitHub account. The repository owner must complete these steps. Deletion removes the old repository history, issues and releases; download any release assets you want to keep first. The earlier source package also remains saved separately.

1. Open https://github.com/HoodNewsSanaa/HN-IPTV/settings while signed in as its owner.
2. On General, scroll to Danger Zone and select Delete this repository.
3. Read the deletion prompts, enter the repository name exactly as GitHub requests, and confirm deletion.

## Recreate and upload

1. Open https://github.com/new. Select owner HoodNewsSanaa, name HN-IPTV, and your preferred visibility.
2. Leave automatic README, gitignore and license initialization off: the source package already contains its files. Create the repository.
3. Extract HN-IPTV-Update-7-Windows-Clean.zip. Its files are at the archive root, so the extracted directory directly contains README.md, HN-IPTV, HN-IPTV.Core, installer and .github.
4. On the empty repository page click uploading an existing file (or Add file > Upload files).
5. In Windows Explorer enable View > Show > Hidden items. Select the extracted directory's CONTENTS, including .github, .gitignore and all the other files/folders, and drag them into the upload area. Do not upload the ZIP itself or an extra enclosing folder. This package is below the browser's 100-file upload limit.
6. Commit to main with the message HN-IPTV Update 7 - clean Windows source and installer-test fix.
7. Confirm .github/workflows/windows-release.yml is present at the repository root. If Actions is disabled, enable it for this repository.

Do not push an old local Git clone to this newly created repository, because that can reintroduce the history you deleted. If using GitHub Desktop instead of the web upload, clone the new repository into a new local folder, copy in these extracted contents, commit and push. Keep the old clone separately as a backup.

## Build and test before release

1. Open Actions > Windows builds and releases. The main push starts a build automatically. To start another, choose Run workflow, main, Run workflow.
2. Wait for ALL steps to pass. The workflow includes shared core tests, simulated installer-control-flow regressions, native Windows playback/UI tests, publish/packaging, and the real installer/reinstall/uninstall preservation test. The new regression harness does not replace the real installation test.
3. If a step fails, keep that error. The HN-IPTV-validation artifact now includes artifacts/installer-test logs on failure. Initial installation, reinstall, silent uninstall and failure cleanup have separately named logs. Do not bypass the failing step to publish.
4. Download the HN-IPTV-Windows-0.7.0 artifact from the successful run and extract it. It contains HN-IPTV-Setup.exe, HN-IPTV-Portable.zip and SHA256SUMS.txt. The separate validation artifact is not the user download package.
5. Test both distributions on your Windows PC with an authorized playlist. Back up existing data before installing over an old version. Verify playback, repeated minimize/restore, fullscreen, channel switching, searches, EPG/logos and persistent playlists/favorites. Complete VALIDATION.md before calling the release stable.

## Publish the download

1. Open https://github.com/HoodNewsSanaa/HN-IPTV/releases/new.
2. Create tag v0.7.0 targeting main. Use release title HN-IPTV Update 7 and the Windows notes in CHANGELOG.md.
3. Attach HN-IPTV-Setup.exe, HN-IPTV-Portable.zip and SHA256SUMS.txt from the SAME successful Actions build. Do not attach the outer Actions artifact ZIP or this source-upload ZIP as the app download.
4. Mark it as a pre-release if native/manual validation is incomplete. Publish when ready.
5. The workflow also runs for the published release and is configured to attach/update these assets automatically. Confirm the final Assets list contains all three files. Windows binaries are unsigned unless you add your own signing process.

Users download the installer or portable ZIP; they do not need to compile. Updates remain manual and no in-app updater is included. Retain the portable data directory when replacing application files. See PORTABILITY.md for older encrypted-vault migration limitations.

GitHub documentation:
- https://docs.github.com/en/repositories/creating-and-managing-repositories/deleting-a-repository
- https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository
- https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow
