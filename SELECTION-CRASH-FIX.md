# HN-IPTV 0.7.1 selection crash hotfix

The supplied Windows .NET Runtime event identifies an unhandled `System.ObjectDisposedException` in `MainWindow.ChannelsList_SelectionChanged`, calling `CancellationTokenSource.Cancel()`. The prior code disposed the source but retained it in the field. An empty category, a channel without guide data, or catalog replacement could leave that disposed instance for the next selection. Stop had the same ownership defect.

This hotfix clears the field before cancelling/disposing its previous value. Repeated cancellation is therefore harmless when no replacement request exists. It also ignores selection events during shutdown. It does not suppress unhandled exceptions globally.

## Upload and rebuild

1. Extract this source ZIP on your computer. Upload its contents to the existing GitHub repository, preserving the folder structure. Do not delete the repository or your saved data.
2. Commit the changes to the branch that your Windows workflow builds. The default build version is now 0.7.1.
3. Open Actions and wait for the Windows workflow, including its new selection regression and existing playback/installer checks, to finish successfully.
4. Download the new Windows artifact. Extract its portable ZIP completely into a new writable folder and test channel/category changes, empty categories, repeated Stop/Play, and restart.
5. For the installed edition, install the new setup over the existing installation. For an existing portable folder, back it up and replace application files while retaining `data`.
6. Once validated, publish tag/release `v0.7.1` using the Windows workflow, and use its newly generated setup, portable ZIP and SHA256SUMS assets.

This addresses the reported selection exception. Failure to open the portable edition still needs a separate startup log if it persists; the supplied event is from the installed edition. Windows execution cannot be performed in this Linux workspace. See VALIDATION.md for verification limits.
