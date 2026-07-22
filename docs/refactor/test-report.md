# Test Report

Environment: Windows x64, .NET SDK 8.0.422, runtime 8.0.28, MSBuild 17.14.40, Inno Setup 6.7.3.

## Automated Results

| Check | Result |
| --- | --- |
| `dotnet restore .\PdfRightClickSuite.sln` | Passed |
| `dotnet format ... --verify-no-changes` | Passed, 0/42 files changed |
| `dotnet build ... --configuration Release` | Passed, 0 warnings/errors |
| `dotnet test ... --configuration Release` | Passed, 92/92 |
| Coverage collection | 69.78% line (776/1,112), 55.23% branch (269/487) |
| Native x64 C++ build | Passed at `/W4`, 0 warnings/errors |
| NuGet vulnerable-package audit | No known vulnerable packages |
| PowerShell parser | 15/15 scripts, 0 parse errors |
| Release build | Passed, including tests and native build |
| Inno Setup compile | Passed |

Coverage improved from 66.48% line and 53.07% branch at baseline. The measured scope is managed Core code; native and PowerShell behavior is covered by builds, source-contract tests, parser checks, and live probes rather than Cobertura.

## Live Installed Validation

- Silent installer: exit 0; no Windows restart required.
- Installed `--version`, `--diagnose --yes`, and `--self-test --yes`: exit 0.
- Installed and release CLI/native DLL hashes: exact match.
- Self-test: JPG/PNG/TXT Convert, Merge, Split, B&W Scan, Colored Scan, and source-preservation checks passed; no temp workspace remained.
- Shell-style Convert request: exit 0, PDF created, request deleted, no request files remained.
- COM probe: one PDF showed Split, B&W Scan, Colored Scan, and Open PDF With; one TXT showed Convert; two PDFs showed Merge; mixed PDF+TXT showed no commands.
- Cancellation tests: partial Split rollback, converter timeout, converter parent/child cancellation, Office deferred cleanup, and five repeated process-lifecycle runs passed.

Evidence: `artifacts/quality-refactor-20260713`.

## Not Applicable Or Intentionally Avoided

- Browser tests, database migrations, API/auth tests, and frontend accessibility tooling do not apply to this local shell utility.
- Foreground Explorer screenshots and forced Explorer restart were avoided because the user was working. Isolated COM probing verified the registered native menu without taking focus.
- PSScriptAnalyzer was not installed; PowerShell AST parsing plus installer-script tests provided the available static coverage.
