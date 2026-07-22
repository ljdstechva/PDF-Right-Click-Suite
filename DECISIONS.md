# Decisions

## Shell integration

Classic `IContextMenu` was implemented as the exact-visibility fallback because it can inspect the current Explorer selection in `QueryContextMenu` and only add valid commands. Windows 11's modern menu can require packaged `IExplorerCommand` deployment and does not reliably support perfect dynamic parent hiding for this use case without additional packaging. The implemented classic handler validates again before launch.

## Explorer safety

The native shell extension does not process PDFs. It classifies selected file paths, writes a UTF-8 JSON request file under `%TEMP%\PdfRightClickSuite`, launches `PdfRightClickSuite.Cli.exe --request <json>` in a new console with `CREATE_NEW_CONSOLE`, and returns immediately.

## Dependencies

- PDFsharp handles merge, split, page counting, and PDF generation.
- Spectre.Console handles CLI tables, panels, prompts, and progress.
- SkiaSharp decodes and normalizes images, including WebP.
- PDFtoImage uses PDFium to render pages for scanned-look output.
- LibreOffice headless is used for Office-style conversions.
- Microsoft Edge headless is preferred for HTML print-to-PDF.

PDFtoImage/PDFium is isolated to scan rendering so merge, split, and normal conversion are not dependent on PDFium.

## Duplicate split pages

Duplicate page entries are deduplicated while preserving first occurrence. This avoids surprising duplicate files in the split folder and keeps output names stable.

## Different selected folders

Merge and multiple conversion write to the first selected file's folder. The CLI shows this clearly before confirmation.

## Automatic convert and scan writes

Convert and scan actions write immediately and never overwrite existing files; output collisions use ` (1)`, ` (2)`, and so on. Convert confirmation remains available only through `--confirm-convert`. Merge still opens the sorting UI, and split still prompts for all pages or selected page ranges.

## Native build tools

The native shell extension is built with Visual Studio 2022 Build Tools and the Desktop development with C++ workload. The release script detects MSBuild through `vswhere`, compiles the x64 COM DLL, and copies `PdfRightClickSuite.ShellExtension.dll` into the release app folder.

When MSBuild/C++ tools are missing on a laptop, `scripts\build-release.ps1` may copy an already installed `PdfRightClickSuite.ShellExtension.dll` into the release folder and write `artifacts\release\NativeBuildFallback.txt`. This fallback is for packaging and verification continuity only; native code changes still require the C++ build tools.

## Default classic-only installer

The default installer no longer attempts Windows 11 modern AppX/MSIX registration. It does not run `Add-AppxPackage`, package certificate import, LocalMachine `TrustedPeople` prompts, or `register-modern-menu-elevated.ps1`. Modern-menu files remain optional/archive-only under `artifacts\release\optional-modern-menu`.

Classic menu placement is handled by the native `IContextMenu` handler inserting the `PDF` parent menu at position `0` during `QueryContextMenu`. Explorer still controls final ordering among all loaded shell extensions.

## PDF Gear context-menu disable

PDF Gear is not uninstalled. PdfRightClickSuite disables matching PDF Gear context-menu registrations by exporting registry backups first, then setting `LegacyDisable` and `ProgrammaticAccessOnly` on shell verbs or renaming matching `shellex\ContextMenuHandlers` keys. Restore is handled by `scripts\restore-pdfgear-context-menu.ps1` using the generated manifest.

The current-user installer can disable HKCU entries. HKLM entries are exported but require elevated registry write access to modify.

## Windows 11 modern menu trust

Modern menu registration is handled by `scripts\register-modern-menu.ps1`, `scripts\register-modern-menu-elevated.ps1`, `scripts\unregister-modern-menu.ps1`, and `scripts\test-modern-menu.ps1`. The normal registration script imports the package signer into CurrentUser trust stores first and falls back to clear diagnostics when AppX deployment still rejects the package. The elevated wrapper intentionally prompts UAC and reruns the same registration flow so the script can import the signer into LocalMachine `TrustedPeople`. On this laptop, `Get-AuthenticodeSignature` reports the sparse MSIX as valid after CurrentUser trust import, but `Add-AppxPackage` still fails with `0x800B0109`, so elevated LocalMachine `TrustedPeople` trust or deployment policy remains the blocker.

This path is now optional/archive-only and is not part of the default installer.

## Markdown conversion

Markdown is converted as readable text rather than full HTML-rendered Markdown. This keeps the first implementation deterministic and avoids adding a Markdown rendering dependency.
