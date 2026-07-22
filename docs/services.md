# Public Core Services

The core project exposes small services used by the CLI and shell integration tests.

- `SelectionClassifier` implements visibility rules for Explorer commands.
- `PageRangeParser` validates and expands split page expressions.
- `OutputNameService` centralizes collision-safe file and folder naming.
- `SortingModel` models reorder behavior independently from terminal rendering.
- `RequestFileService` reads and writes shell request JSON files.
- `PdfPageCountService`, `PdfMergeService`, and `PdfSplitService` handle structural PDF operations through PDFsharp.
- `PdfConvertService` converts supported non-PDF inputs and delegates Office/HTML conversion to known external tools.
- `PdfScanEffectService` renders pages through PDFium, applies a mild scan effect, and rebuilds a PDF.
- `ExternalToolLocator` searches common install paths and `PATH` for LibreOffice and Microsoft Edge.
- `LoggerService` writes technical logs under `%LOCALAPPDATA%\PdfRightClickSuite\logs`.

All write operations use unique output paths from `OutputNameService` and temp-file writes where practical.
