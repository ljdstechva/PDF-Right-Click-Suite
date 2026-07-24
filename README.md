# PDF Right Click Suite

PdfRightClickSuite is a local Windows PDF utility for File Explorer. It adds a parent right-click menu named **PDF** and launches a polished console workflow for PDF tasks.

## Quick Install

### Install with an AI agent — copy and paste

If you use an AI agent that can operate your Windows computer and run PowerShell, send it the single prompt below. You do not need to supply any paths or additional commands.

```text
Install PDF Right Click Suite on this Windows 10/11 x64 computer from the official GitHub repository https://github.com/ljdstechva/PDF-Right-Click-Suite by downloading the latest release assets PdfRightClickSuiteSetup.exe and SHA256SUMS.txt into a temporary folder, verifying that the installer's SHA-256 exactly matches the published checksum and stopping without running it if the values differ, using Unblock-File only on the verified installer, running the per-user installer without removing unrelated applications while preserving its automatic PDF Gear registry backup, restarting File Explorer only if necessary, running the installed PdfRightClickSuite.Cli.exe with --version, --diagnose --yes, and --self-test --yes, then reporting the installed version, install path, Explorer menu status, and all verification results and deleting only the temporary download files when finished.
```

Expected result: the agent installs the current release for your Windows user, confirms the **PDF** Explorer menu is registered, and reports whether diagnostics and self-test passed.

The installer is not digitally signed. The AI agent should run it only when it came from this repository's [latest release](https://github.com/ljdstechva/PDF-Right-Click-Suite/releases/latest) and its SHA-256 matches the published checksum; it should never disable Windows security controls globally.

### Install it yourself

1. Download [`PdfRightClickSuiteSetup.exe`](https://github.com/ljdstechva/PDF-Right-Click-Suite/releases/latest/download/PdfRightClickSuiteSetup.exe) and [`SHA256SUMS.txt`](https://github.com/ljdstechva/PDF-Right-Click-Suite/releases/latest/download/SHA256SUMS.txt) into the same folder.
2. Open PowerShell in that folder and verify the installer before running it:

```powershell
$installer = Join-Path $PWD "PdfRightClickSuiteSetup.exe"
$checksumFile = Join-Path $PWD "SHA256SUMS.txt"
$checksumLine = Get-Content -LiteralPath $checksumFile |
    Where-Object { $_ -match '\s+PdfRightClickSuiteSetup\.exe$' } |
    Select-Object -First 1

if (-not $checksumLine) { throw "Installer checksum is missing." }

$expected = ($checksumLine -split '\s+')[0].ToUpperInvariant()
$actual = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash

if ($actual -ne $expected) { throw "Installer checksum does not match." }

Unblock-File -LiteralPath $installer
Start-Process -FilePath $installer -Wait
```

3. Verify the installed app:

```powershell
$cli = Join-Path $env:LOCALAPPDATA "Programs\PdfRightClickSuite\PdfRightClickSuite.Cli.exe"
& $cli --version
& $cli --diagnose --yes
& $cli --self-test --yes
```

The install is per-user and normally does not need administrator rights. On Windows 11, right-click a supported file and choose **Show more options** to find the **PDF** menu. The installer backs up and disables matching current-user PDF Gear context-menu entries when found; the restore command is documented under [Install From Source](#install-from-source).

## Features

- Merge two or more PDFs in a user-selected order.
- Split one PDF into all pages or selected page ranges.
- Convert one PDF to Word (`.docx`), Excel (`.xlsx`), or PowerPoint (`.pptx`) from a nested **Convert PDF To** menu.
- Convert supported non-PDF files to PDF without a write confirmation prompt.
- Create deliberately soft, low-quality scanned-look PDFs from one PDF in B&W or color without a write confirmation prompt.
- Open one PDF with an installed PDF app detected from Windows' `.pdf` app associations.
- Keep original files unchanged.
- Write logs to `%LOCALAPPDATA%\PdfRightClickSuite\logs`.

## Supported Windows Versions

- Windows 10 x64 classic context menu.
- Windows 11 x64 through **Show more options** / classic context menu fallback.

The default installer ships the exact-visibility classic `IContextMenu` shell extension only. Windows 11 users should use **Show more options**. Optional/archive modern-menu source remains under `modern` with its supporting scripts; a local release build generates `artifacts\release\optional-modern-menu`, which is not committed to Git. Package registration can be blocked by Windows sideloading/certificate trust policy.

## Menu Visibility

- **Merge PDFs** appears for 2 or more PDF files.
- **Split PDF** appears for exactly 1 PDF file.
- **Convert PDF To** appears for exactly 1 PDF file and contains **Word (.docx)**, **Excel (.xlsx)**, and **PowerPoint (.pptx)**.
- **Convert to PDF** appears for one or more non-PDF files and never when any selected file is a PDF.
- **Make Scanned PDF (B&W)** and **Make Scanned PDF (Colored)** appear for exactly 1 PDF file.
- **Open PDF With** appears for exactly 1 PDF file when Windows reports at least one recommended `.pdf` app association; its submenu lists the detected PDF apps for that machine.
- Directories are not supported.
- Multiple non-PDF conversion appears only when files are similar: same extension, or all files are in the supported image family.

## Supported Convert Formats

- Images: `jpg`, `jpeg`, `png`, `bmp`, `tif`, `tiff`, `webp`
- Text: `txt`, `log`, `csv`
- Markdown: `md` rendered as readable formatted text
- HTML: `html`, `htm` using Microsoft Edge headless print-to-PDF when available
- Office-style files: `doc`, `docx`, `rtf`, `odt`, `xls`, `xlsx`, `ods`, `ppt`, `pptx`, `odp`

Office-style conversion uses LibreOffice headless when available. If LibreOffice is not installed, the CLI falls back to installed Microsoft Office desktop apps for Word, Excel, and PowerPoint formats. Microsoft Office exports use a short local temporary path and an atomic final write so long document/output paths do not exceed Office's export-path limit. HTML uses Microsoft Edge headless print-to-PDF when available and falls back to LibreOffice when Edge is unavailable.

## Convert PDF To Office

Select exactly one PDF, then choose **PDF → Convert PDF To**:

- **Word (.docx)** uses Microsoft Word PDF Reflow when Word is installed, otherwise LibreOffice `writer_pdf_import`. The output is editable, but complex layouts may need cleanup. If neither application is installed, the CLI gives installation guidance.
- **Excel (.xlsx)** is built in. It extracts positioned text into one worksheet per page and works best for table-style PDFs. It does not recreate formulas, charts, or exact formatting. Image-only/scanned PDFs need OCR first.
- **PowerPoint (.pptx)** is built in. It creates one full-slide page image per PDF page at up to 150 DPI, preserving visual appearance; the slide text is not editable.

All three conversions run locally, write beside the source with collision suffixes, leave the source unchanged, and remove staged output on cancellation or failure. No file is uploaded.

## Build

Prerequisites:

- .NET 8 SDK or newer.
- Visual Studio 2022 Build Tools with **Desktop development with C++** for the native shell extension.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1
```

Release output:

```text
artifacts\release\app
```

If C++ Build Tools are missing, the script still publishes the CLI. When a previously installed native DLL is available, it copies that DLL into the release folder and writes `artifacts\release\NativeBuildFallback.txt`; otherwise it writes `artifacts\release\NativeBuildRequired.txt`. Install C++ Build Tools and rerun the build before changing native shell-extension code.

## Install From Source

For a normal installation, use the verified release workflow under [Quick Install](#quick-install). From a source clone, run the current-user install script:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

Default install location:

```text
%LOCALAPPDATA%\Programs\PdfRightClickSuite
```

The installer registers the shell extension under `HKCU`, so normal usage should not require admin rights. It notifies Explorer of association changes and prompts before restarting Explorer during manual script installs. The default flow is classic-only: it does not run AppX/MSIX registration, certificate import, `Add-AppxPackage`, or modern-menu elevation wrappers.

The installer also disables matching PDF Gear current-user context-menu registrations when found, after exporting registry backups. Restore PDF Gear context-menu entries with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:LOCALAPPDATA\Programs\PdfRightClickSuite\scripts\restore-pdfgear-context-menu.ps1" -BackupRoot "$env:LOCALAPPDATA\PdfRightClickSuite\registry-backups"
```

Manual restart:

If `artifacts\release\app` is missing, `install.ps1` runs `scripts\build-release.ps1` first. A complete Explorer install still requires the native shell extension DLL to be present in the release folder.

```powershell
Stop-Process -Name explorer -Force; Start-Process explorer.exe
```

## Uninstall

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

The uninstaller unregisters the shell extension, removes installed files, optionally removes logs, and prompts before restarting Explorer.

## CLI Usage

Shell integration normally writes a JSON request and invokes:

```powershell
PdfRightClickSuite.Cli.exe --request "C:\path\request.json"
```

Manual usage:

```powershell
PdfRightClickSuite.Cli.exe --action merge --files "a.pdf" "b.pdf"
PdfRightClickSuite.Cli.exe --action split --files "input.pdf"
PdfRightClickSuite.Cli.exe --action convert --files "image.png"
PdfRightClickSuite.Cli.exe --action scan --files "input.pdf"
PdfRightClickSuite.Cli.exe --action scan-colored --files "input.pdf"
PdfRightClickSuite.Cli.exe --action convert-to-word --files "input.pdf"
PdfRightClickSuite.Cli.exe --action convert-to-excel --files "input.pdf"
PdfRightClickSuite.Cli.exe --action convert-to-powerpoint --files "input.pdf"
```

Useful options:

```text
--yes
--debug
--diagnose
--self-test
--install-user
--uninstall-user
--dry-run
--confirm-convert
--scan-dpi 150
--scan-strength light|low-quality|rough
--scan-jpeg-quality 58
--scan-blur 1.05
--scan-rotation 1.5
```

Convert, scan, merge-after-sorting, and split-after-page-selection actions run automatically and choose a non-conflicting output name. Scan (B&W) defaults to a deliberately degraded `low-quality` profile: 150 DPI, JPEG quality 58, a `1.05`-pixel blur, reduced cleanup, visible scanner texture, and approximately 1.5 degrees of counterclockwise rotation. The page is scaled to keep all content inside the output after rotation. Scan (Colored) uses the same degradation while preserving colored ink, stamps, and markings. Use `--confirm-convert` only when you want the older convert write confirmation behavior. Merge still opens the sort UI, but pressing Enter to confirm the order now starts the merge immediately and exits after showing the output path. Split asks only whether to write all pages or selected page ranges, then starts immediately and exits after showing the output folder.

Press Ctrl+C to cancel a running operation. Cancelled merge, split, convert, and scan work removes staged or partial output; cancelled or timed-out Edge and LibreOffice process trees are stopped before the CLI returns.

Double-clicking `PdfRightClickSuite.Cli.exe` opens a welcome/status screen instead of exiting silently.

## Sorting Controls

- Up arrow: select previous file
- Down arrow: select next file
- Left arrow: move selected file up
- Right arrow: move selected file down
- Enter: confirm
- Esc: cancel
- R: reset original order
- H or ?: help

## Split Page Input

Valid page expressions:

```text
1
1,3,5
2-6
1,3-5,8
```

Invalid examples:

```text
abc
1,,2
0
-3
3-
7-2
1.5
```

Duplicate pages are deduplicated while preserving the first occurrence. For example, `1,3,1,2-3` becomes `1,3,2`.

## Output Naming

- Merge: `Merged_YYYYMMDD_HHMMSS.pdf`
- Split folder: `<original_name>_split`
- Split files: `<original_name>_p001.pdf`
- Convert single: `<original_name>.pdf`
- Convert multiple merged: `Converted_Merged_YYYYMMDD_HHMMSS.pdf`
- Scan (B&W): `<original_name>_scanned.pdf`
- Scan (Colored): `<original_name>_scanned_colored.pdf`
- PDF to Word: `<original_name>.docx`
- PDF to Excel: `<original_name>.xlsx`
- PDF to PowerPoint: `<original_name>.pptx`

Collisions append ` (1)`, ` (2)`, and so on.

## Troubleshooting

- Menu does not appear: restart Explorer and verify the install script completed without error.
- Windows 11: use **Show more options** to see the classic menu fallback.
- Windows 11 modern menu: intentionally disabled from the default installer. A local release build generates optional sparse-package materials under `artifacts\release\optional-modern-menu`; use them only for explicit modern-menu experiments. The verified supported path is **Show more options**.
- PDF Gear still appears: check the latest manifest under `%LOCALAPPDATA%\PdfRightClickSuite\registry-backups`. Current-user PDF Gear shell verbs are disabled with `LegacyDisable` and `ProgrammaticAccessOnly`; machine-wide HKLM PDF Gear entries require elevated registry write access.
- Encrypted/corrupt PDFs: the CLI reports a friendly error and logs technical details.
- Office conversion says LibreOffice and Microsoft Office are unavailable: install LibreOffice or add `soffice.exe` to `PATH`, or install the matching Microsoft Office desktop app.
- PDF-to-Word says no backend is available: install Microsoft Word desktop or LibreOffice.
- PDF-to-Excel says there is no extractable text: run OCR on the scanned/image-only PDF, then retry.
- PDF-to-PowerPoint produces non-editable text: this is expected because each source page is preserved as a full-slide image.
- Word reports an unexpected PDF export error: update to the current build, which exports through a short Office-safe temporary path before atomically writing the requested long output path.
- Unexpected PDF/log pop-up at Windows sign-in: run `scripts\audit-startup-silence.ps1` to record startup entries. Use `-Apply` only after confirming the matched entry is PdfRightClickSuite diagnostic/self-test/log startup noise.
- Windows asks which app should open a file such as `02` at sign-in: inspect current-user Run entries for an unquoted executable path containing spaces. Back up the exact registry value first, quote the complete executable path, and archive any confirmed stray extensionless log instead of deleting it without inspection.
- Edge not found for HTML: LibreOffice is used as fallback.
- Logs: `%LOCALAPPDATA%\PdfRightClickSuite\logs`; the native shell extension writes `shell-extension.log` with selection count, extensions, availability, request path, and launch result.

## Security And Privacy

- All processing is local.
- No cloud upload is performed.
- Original files are not modified.
- The Explorer extension never performs PDF processing in Explorer. It writes a size-bounded request JSON file, starts the CLI in a new console, and returns.
- Shell request files are validated, consumed once, and deleted. Stale request files are cleaned so selected document paths do not accumulate in the temporary folder.
- External processes are limited to known dependencies: Microsoft Edge, LibreOffice, and installed Microsoft Office desktop apps. Built-in PDF-to-Excel and PDF-to-PowerPoint conversion uses packaged local libraries only. Edge and LibreOffice run inside a Windows Job Object so cancellation or timeout also stops descendant processes.

## Licensing

This repository does not currently declare a license for its original source code. Bundled third-party components retain their respective licenses and copyright notices; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The installer includes that summary together with the upstream .NET and SkiaSharp license and notice files.

## Developer Tests

```powershell
dotnet test .\PdfRightClickSuite.sln
```

The integration tests generate simple sample PDFs, an image, and a DOCX locally, then verify merge, split, page counts, request JSON, naming, sorting, conversion, cancellation rollback, process-tree termination, and temporary-workspace cleanup. PDF-to-Office tests reopen and validate generated XLSX/PPTX packages, check a known two-column grid, cover scanned-PDF behavior, and preserve source hashes. Word-dependent tests run only when Microsoft Word COM conversion is available and include long-path cases.
