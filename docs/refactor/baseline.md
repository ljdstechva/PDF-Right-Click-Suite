# Quality Refactor Baseline

Date: 2026-07-13 (Asia/Singapore)

## Product And Stack

- Windows File Explorer PDF utility with a native x64 COM shell extension and a self-contained .NET 8 CLI.
- C#/.NET 8, C++17/Win32 COM, Spectre.Console, PDFsharp, PDFtoImage/PDFium, and SkiaSharp.
- External conversion boundaries: Microsoft Edge, LibreOffice, and Microsoft Office COM fallback.
- xUnit test project and Inno Setup current-user installer.
- No web frontend, server API, database, authentication, queue, cloud service, or AI integration.

## Change Protection

- The project is not a Git worktree, so no branch or status was available.
- `artifacts/quality-refactor-20260713/source-baseline.json` records SHA256 hashes for 62 source/configuration files before edits.
- Existing feature-specific artifacts and user settings were preserved.

## Initial Checks

| Gate | Baseline result |
| --- | --- |
| Restore | Passed |
| Managed build | Passed, 0 warnings/errors |
| Native x64 build | Passed, 0 warnings/errors |
| Formatter | 0 changed files |
| Tests | 77/77 passed |
| Coverage | 66.48% line, 53.07% branch |
| Installed version/diagnose/self-test | Exit 0 |
| PowerShell analysis | PSScriptAnalyzer unavailable; parser and script tests used later |
| Dependency audit | Runtime clean; test graph had two high-severity vulnerable transitives |

## Baseline Findings

- Explorer request JSON files were retained in `%TEMP%` and disclosed selected document paths.
- Ctrl+C was not wired to operations, and Edge/LibreOffice could survive timeout or cancellation.
- cancellation could leave staged or partial merge, split, convert, or scan files.
- Native UTF-8 conversion allocated one byte too little, and failed launches retained request files.
- Error logging and several cleanup paths suppressed failures without diagnostics.
- Test dependencies used deprecated xUnit 2 packages with two known high-severity transitive vulnerabilities.

Evidence is under `artifacts/quality-refactor-20260713/logs`.
