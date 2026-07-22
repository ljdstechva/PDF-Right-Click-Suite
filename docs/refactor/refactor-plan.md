# Refactor Plan And Status

## Batch 1: Request And Native Safety - Completed

- Validate and size-bound request files.
- Consume app-owned shell requests once and remove stale files.
- Preserve user-owned request files outside the shell temp directory.
- Correct native UTF-8 buffer allocation and failed-launch cleanup.
- Add request and native-source regressions.

## Batch 2: Cancellation And Resource Ownership - Completed

- Wire Console Ctrl+C to every long-running CLI path.
- Add late-cancellation checks before final output publication.
- Roll back partial Split output and clean all temporary PDFs/images.
- Own Edge/LibreOffice descendants with a kill-on-close Windows Job Object.
- Stop cancelled install/uninstall child processes.
- Make Office abandonment cleanup safe without force-killing user Office sessions.
- Add deterministic cancellation, timeout, rollback, and cleanup tests.

## Batch 3: Dependencies And Diagnostics - Completed

- Replace the vulnerable xUnit 2 stack with supported xUnit v3 packages.
- Make logging and cleanup failures observable without masking successful work.
- Correct registry-restore error reporting.
- Run vulnerability, deprecated-package, formatter, build, native, and PowerShell parser checks.

## Batch 4: Release And Live Validation - Completed

- Rebuild the self-contained release and Inno Setup installer.
- Install silently without closing or restarting Explorer.
- Verify installed/release hashes, diagnostics, self-test, shell request conversion, and COM menu visibility.
- Refresh and hash the full-transfer ZIP.

## Risk Controls

- Kept accepted scan rendering and menu behavior unchanged.
- Used timestamped installer and installed-binary backups.
- Avoided Explorer restart and foreground UI automation while the user was working.
- Used app-owned temporary fixtures only; verified test processes and workspaces were removed.
