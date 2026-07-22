# Final Architecture

## Runtime Flow

1. Explorer loads the x64 COM shell extension.
2. The extension classifies the selection and exposes only valid PDF commands.
3. Invoking a PDF command writes a small JSON request under `%TEMP%\PdfRightClickSuite` and starts the installed CLI.
4. The CLI validates and consumes the app-owned request, then classifies the files again as a trust boundary.
5. Spectre.Console gathers only the interaction required by the action.
6. Core services process PDFs through sibling temporary files and publish with a no-overwrite atomic move.
7. The CLI shows the final output or a friendly error; technical details go to LocalAppData logs.

## Module Responsibilities

- `native/PdfRightClickSuite.ShellExtension`: Explorer selection, dynamic commands, PDF app discovery, and CLI launch only.
- `src/PdfRightClickSuite.Cli`: argument/request orchestration, terminal interaction, diagnostics, installation wrappers, and self-test.
- `src/PdfRightClickSuite.Core`: classification, naming, PDF operations, request validation, external conversion, logging, and process lifecycle.
- `scripts`: current-user install/uninstall, registry backup/restore, audits, and release assembly.
- `installer`: Inno Setup packaging and registration.
- `tests`: unit, integration, CLI-process, cancellation, native-source, and installer-script regressions.

## Reliability Boundaries

- `RequestFileService` owns validation and app-temp request cleanup.
- `AtomicFileWriter` owns sibling staging and no-overwrite publication.
- Merge, Split, Convert, and Scan own rollback of their temporary or emitted files.
- `WindowsProcessJob` owns Edge/LibreOffice descendants and kills them when cancelled, timed out, or disposed.
- Microsoft Office conversion runs on an STA worker and performs deferred temp cleanup after an abandoned call; it intentionally does not kill Office processes that might contain user work.

## Packaging

- `scripts/build-release.ps1` restores, tests, publishes a self-contained win-x64 CLI, rebuilds the native DLL, and stages scripts/assets.
- Inno Setup installs per user under `%LOCALAPPDATA%\Programs\PdfRightClickSuite` and registers the native DLL.
- The verified Windows 11 path remains the classic **Show more options** menu. Modern MSIX material remains archive-only.
