# Installer-test correction

The original test successfully checked install, registration, same-version reinstall and silent-uninstall data preservation, then launched unins000.exe again from finally. Inno Setup can leave that file briefly present while its temporary clone finishes cleanup. The second invocation returned 1 and failed the job after a premature success message.

The corrected test:

- Records the uninstall attempt before launching it and never issues a second attempt from finally.
- Waits up to 30 seconds for the native uninstaller clone to finish removing unins000.exe.
- Retries transient locks when deleting test-owned directories, with a bounded timeout.
- Prints success only after all assertions and cleanup succeed.
- Preserves the original test failure if cleanup also fails.
- Refuses existing app data AND an existing installation; removes only the synthetic data it created.
- Captures stage-specific Inno logs under artifacts/installer-test and uploads them with validation reports even if the native test fails.

## Verified in this correction session

PowerShell 7.5.4 on Linux parsed all included PowerShell files without syntax errors. The corrected installer-test control flow passed seven simulated scenarios:

1. Successful install/reinstall/uninstall with delayed uninstaller-file deletion: exactly one uninstall.
2. Failed uninstall: not retried, nonzero exit still fails.
3. Failed reinstall followed by failed cleanup: original reinstall error preserved.
4. Cleanup timeout: fails without printing success.
5. Installer timeout: process kill requested and original timeout preserved.
6. Existing data: refused before starting a process; data preserved.
7. Existing installation: refused before starting a process.

The same harness reproduces the original script's Installer returned 1 failure. The regression output is in validation/installer-orchestration.txt. Run with:

```powershell
pwsh -NoProfile -File ./test-installer-control-flow.ps1
```

These checks simulate process results and registry state. They do NOT install Inno Setup or verify real Windows installation/uninstallation. The corrected native installer test and entire updated workflow must still run in Windows Actions. Application C# code, version 0.7.0 and installer identity have not changed in this correction; the Windows app was not rebuilt here. Earlier build evidence is retained in validation/ with its original scope.

Upstream uninstaller lifecycle reference:
https://jrsoftware.org/ishelp/topic_uninstexitcodes.htm
