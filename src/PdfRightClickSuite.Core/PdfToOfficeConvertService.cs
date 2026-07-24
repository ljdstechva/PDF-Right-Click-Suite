using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using PDFtoImage;
using SkiaSharp;
using System.Runtime.Versioning;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace PdfRightClickSuite.Core;

public sealed class PdfToOfficeConvertService
{
    private const int PowerPointRenderDpi = 150;
    private const int PowerPointMaximumPixels = 4000;
    private const int PowerPointJpegQuality = 85;
    private const long EmusPerPoint = 12700L;
    private const long MaximumSlideEmus = 56L * 914400L;

    private readonly ExternalToolLocator _externalToolLocator;
    private readonly PdfPageCountService _pageCountService;

    public PdfToOfficeConvertService(ExternalToolLocator externalToolLocator)
        : this(externalToolLocator, new PdfPageCountService())
    {
    }

    internal PdfToOfficeConvertService(
        ExternalToolLocator externalToolLocator,
        PdfPageCountService pageCountService)
    {
        _externalToolLocator = externalToolLocator ?? throw new ArgumentNullException(nameof(externalToolLocator));
        _pageCountService = pageCountService ?? throw new ArgumentNullException(nameof(pageCountService));
    }

    public async Task<PdfToOfficeResult> ConvertAsync(
        string source,
        string output,
        OfficeExportFormat format,
        CancellationToken cancellationToken,
        IProgress<int>? progress = null)
    {
        var sourcePath = ValidateSource(source);
        var outputPath = ValidateOutput(output, format);
        cancellationToken.ThrowIfCancellationRequested();

        int pageCount;
        try
        {
            pageCount = _pageCountService.GetPageCount(sourcePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PdfProcessingException(
                "The PDF could not be opened. It may be encrypted, corrupt, locked, or unsupported.",
                ex);
        }

        if (pageCount < 1)
        {
            throw new PdfProcessingException("The PDF does not contain any pages.");
        }

        var workspace = Path.Combine(Path.GetTempPath(), "PdfRightClickSuite", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            return format switch
            {
                OfficeExportFormat.Word => await ConvertToWordAsync(
                    sourcePath,
                    outputPath,
                    pageCount,
                    cancellationToken).ConfigureAwait(false),
                OfficeExportFormat.Excel => ConvertToExcel(
                    sourcePath,
                    outputPath,
                    pageCount,
                    cancellationToken,
                    progress),
                OfficeExportFormat.PowerPoint => ConvertToPowerPoint(
                    sourcePath,
                    outputPath,
                    workspace,
                    pageCount,
                    cancellationToken,
                    progress),
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown Office export format.")
            };
        }
        finally
        {
            TryDeleteDirectory(workspace);
        }
    }

    private async Task<PdfToOfficeResult> ConvertToWordAsync(
        string sourcePath,
        string outputPath,
        int pageCount,
        CancellationToken cancellationToken)
    {
        Exception? wordFailure = null;
        if (MicrosoftOfficePdfConverter.IsPdfToDocxAvailable())
        {
            try
            {
                MicrosoftOfficePdfConverter.ConvertPdfToDocx(sourcePath, outputPath, cancellationToken);
                return new PdfToOfficeResult("Microsoft Word PDF import", pageCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                wordFailure = ex;
            }
        }

        var libreOffice = _externalToolLocator.FindLibreOffice();
        if (libreOffice is null)
        {
            throw new PdfProcessingException(
                "PDF-to-Word conversion requires Microsoft Word desktop or LibreOffice. Install either application and try again.",
                wordFailure);
        }

        var conversionFolder = Path.Combine(Path.GetTempPath(), "PdfRightClickSuite", Guid.NewGuid().ToString("N"));
        var stagedOutput = AtomicFileWriter.CreateTempPathBeside(outputPath);
        Directory.CreateDirectory(conversionFolder);
        try
        {
            var result = await ExternalProcessRunner.RunAsync(
                libreOffice,
                [
                    "--headless",
                    "--infilter=writer_pdf_import",
                    "--convert-to",
                    "docx",
                    "--outdir",
                    conversionFolder,
                    sourcePath
                ],
                TimeSpan.FromMinutes(3),
                cancellationToken).ConfigureAwait(false);

            var converted = Path.Combine(
                conversionFolder,
                $"{Path.GetFileNameWithoutExtension(sourcePath)}.docx");
            if (result.ExitCode != 0 || !File.Exists(converted) || new FileInfo(converted).Length == 0)
            {
                var details = string.Join(
                    " ",
                    new[] { result.StandardError, result.StandardOutput }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                throw new PdfProcessingException(
                    $"LibreOffice PDF-to-Word conversion failed. {details}".Trim(),
                    wordFailure);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(converted, stagedOutput);
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFileWriter.MoveIntoPlace(stagedOutput, outputPath);
            return new PdfToOfficeResult("LibreOffice writer_pdf_import", pageCount);
        }
        finally
        {
            AtomicFileWriter.TryDelete(stagedOutput);
            TryDeleteDirectory(conversionFolder);
        }
    }

    private static PdfToOfficeResult ConvertToExcel(
        string sourcePath,
        string outputPath,
        int pageCount,
        CancellationToken cancellationToken,
        IProgress<int>? progress)
    {
        var stagedOutput = AtomicFileWriter.CreateTempPathBeside(outputPath);
        try
        {
            var pages = new List<IReadOnlyList<IReadOnlyList<string>>>(pageCount);
            var extractedWordCount = 0;
            using (var document = PdfDocument.Open(sourcePath))
            {
                for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var words = NearestNeighbourWordExtractor.Instance
                        .GetWords(document.GetPage(pageNumber).Letters)
                        .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                        .ToArray();
                    extractedWordCount += words.Length;
                    pages.Add(BuildGrid(words));
                    progress?.Report(pageNumber);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            if (extractedWordCount == 0)
            {
                throw new PdfProcessingException(
                    "This PDF has no extractable text (it may be a scanned image), so it cannot be converted to Excel.");
            }

            WriteWorkbook(stagedOutput, pages);
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFileWriter.MoveIntoPlace(stagedOutput, outputPath);
            return new PdfToOfficeResult("PdfPig + Open XML SDK", pageCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not PdfProcessingException and not IOException)
        {
            throw new PdfProcessingException(
                "PDF-to-Excel conversion failed. The PDF may be encrypted, corrupt, or use unsupported text encoding.",
                ex);
        }
        finally
        {
            AtomicFileWriter.TryDelete(stagedOutput);
        }
    }

    private static PdfToOfficeResult ConvertToPowerPoint(
        string sourcePath,
        string outputPath,
        string workspace,
        int pageCount,
        CancellationToken cancellationToken,
        IProgress<int>? progress)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("PDF-to-PowerPoint rendering is supported on Windows.");
        }

        var stagedOutput = AtomicFileWriter.CreateTempPathBeside(outputPath);
        try
        {
            var renderedPages = RenderPages(
                sourcePath,
                workspace,
                pageCount,
                cancellationToken,
                progress);
            WritePresentation(stagedOutput, renderedPages);
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFileWriter.MoveIntoPlace(stagedOutput, outputPath);
            return new PdfToOfficeResult("PDFtoImage + Open XML SDK", pageCount);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not PdfProcessingException and not IOException)
        {
            throw new PdfProcessingException(
                "PDF-to-PowerPoint conversion failed. The PDF may be encrypted, corrupt, locked, or unsupported by PDFium.",
                ex);
        }
        finally
        {
            AtomicFileWriter.TryDelete(stagedOutput);
        }
    }

    private static IReadOnlyList<IReadOnlyList<string>> BuildGrid(IReadOnlyList<Word> words)
    {
        if (words.Count == 0)
        {
            return Array.Empty<IReadOnlyList<string>>();
        }

        var medianHeight = Median(words.Select(word => word.BoundingBox.Height).Where(value => value > 0));
        var medianCharacterWidth = Median(
            words
                .Where(word => word.Text.Length > 0)
                .Select(word => word.BoundingBox.Width / word.Text.Length)
                .Where(value => value > 0));
        var baselineTolerance = Math.Max(0.5, medianHeight * 0.5);
        var columnGap = Math.Max(1.0, medianCharacterWidth * 1.5);
        var rows = new List<WordRow>();

        foreach (var word in words.OrderByDescending(word => word.BoundingBox.Bottom).ThenBy(word => word.BoundingBox.Left))
        {
            var baseline = word.BoundingBox.Bottom;
            var row = rows
                .Where(candidate => Math.Abs(candidate.Baseline - baseline) <= baselineTolerance)
                .OrderBy(candidate => Math.Abs(candidate.Baseline - baseline))
                .FirstOrDefault();
            if (row is null)
            {
                row = new WordRow(baseline);
                rows.Add(row);
            }

            row.Words.Add(word);
        }

        return rows
            .OrderByDescending(row => row.Baseline)
            .Select(row => BuildCells(row.Words, columnGap))
            .ToArray();
    }

    private static IReadOnlyList<string> BuildCells(IEnumerable<Word> rowWords, double columnGap)
    {
        var cells = new List<string>();
        var current = new List<string>();
        double? previousRight = null;
        foreach (var word in rowWords.OrderBy(word => word.BoundingBox.Left))
        {
            if (previousRight is not null && word.BoundingBox.Left - previousRight.Value > columnGap && current.Count > 0)
            {
                cells.Add(string.Join(" ", current));
                current.Clear();
            }

            current.Add(word.Text.Trim());
            previousRight = word.BoundingBox.Right;
        }

        if (current.Count > 0)
        {
            cells.Add(string.Join(" ", current));
        }

        return cells;
    }

    private static void WriteWorkbook(
        string path,
        IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>> pages)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new S.Workbook();
        var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new S.SheetData();
            worksheetPart.Worksheet = new S.Worksheet(sheetData);

            uint rowIndex = 1;
            foreach (var values in pages[pageIndex])
            {
                var row = new S.Row { RowIndex = rowIndex };
                var columnIndex = 1;
                foreach (var value in values)
                {
                    row.Append(new S.Cell
                    {
                        CellReference = $"{GetSpreadsheetColumnName(columnIndex)}{rowIndex}",
                        DataType = S.CellValues.InlineString,
                        InlineString = new S.InlineString(
                            new S.Text(value) { Space = SpaceProcessingModeValues.Preserve })
                    });
                    columnIndex++;
                }

                sheetData.Append(row);
                rowIndex++;
            }

            sheets.Append(new S.Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = checked((uint)(pageIndex + 1)),
                Name = $"Page {pageIndex + 1}"
            });
        }

        workbookPart.Workbook.Save();
    }

    [SupportedOSPlatform("windows")]
    private static IReadOnlyList<RenderedPage> RenderPages(
        string sourcePath,
        string workspace,
        int pageCount,
        CancellationToken cancellationToken,
        IProgress<int>? progress)
    {
        var rendered = new List<RenderedPage>(pageCount);
        using var document = PdfDocument.Open(sourcePath);
        using var sourceStream = File.OpenRead(sourcePath);
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.GetPage(pageIndex + 1);
            var widthPoints = Convert.ToDouble(page.Width, System.Globalization.CultureInfo.InvariantCulture);
            var heightPoints = Convert.ToDouble(page.Height, System.Globalization.CultureInfo.InvariantCulture);
            var longestSidePoints = Math.Max(widthPoints, heightPoints);
            var dpi = Math.Clamp(
                (int)Math.Floor(PowerPointMaximumPixels * 72d / longestSidePoints),
                36,
                PowerPointRenderDpi);
            var imagePath = Path.Combine(workspace, $"page-{pageIndex + 1:000}.jpg");
            var options = new RenderOptions
            {
                Dpi = dpi,
                Grayscale = false,
                WithAnnotations = true,
                WithFormFill = true,
                UseTiling = true
            };

            sourceStream.Position = 0;
            using var bitmap = Conversion.ToImage(
                sourceStream,
                Index.FromStart(pageIndex),
                leaveOpen: true,
                password: null,
                options: options);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, PowerPointJpegQuality);
            using (var imageStream = File.Create(imagePath))
            {
                encoded.SaveTo(imageStream);
            }

            rendered.Add(new RenderedPage(imagePath, widthPoints, heightPoints));
            progress?.Report(pageIndex + 1);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return rendered;
    }

    private static void WritePresentation(string path, IReadOnlyList<RenderedPage> pages)
    {
        if (pages.Count == 0)
        {
            throw new PdfProcessingException("The PDF does not contain any pages.");
        }

        var slideWidth = checked((long)Math.Round(pages[0].WidthPoints * EmusPerPoint));
        var slideHeight = checked((long)Math.Round(pages[0].HeightPoints * EmusPerPoint));
        var scale = Math.Min(
            1d,
            Math.Min(MaximumSlideEmus / (double)slideWidth, MaximumSlideEmus / (double)slideHeight));
        slideWidth = Math.Max(1, checked((long)Math.Round(slideWidth * scale)));
        slideHeight = Math.Max(1, checked((long)Math.Round(slideHeight * scale)));

        using var document = PresentationDocument.Create(path, PresentationDocumentType.Presentation);
        var presentationPart = document.AddPresentationPart();
        var slideMasterPart = presentationPart.AddNewPart<SlideMasterPart>();
        var slideLayoutPart = slideMasterPart.AddNewPart<SlideLayoutPart>();
        var themePart = slideMasterPart.AddNewPart<ThemePart>();

        themePart.Theme = CreateTheme();
        slideLayoutPart.SlideLayout = new P.SlideLayout(
            new P.CommonSlideData(CreateShapeTree()) { Name = "Blank" },
            new P.ColorMapOverride(new A.MasterColorMapping()))
        {
            Type = P.SlideLayoutValues.Blank,
            Preserve = true
        };
        slideLayoutPart.AddPart(slideMasterPart);

        var layoutRelationshipId = slideMasterPart.GetIdOfPart(slideLayoutPart);
        slideMasterPart.SlideMaster = new P.SlideMaster(
            new P.CommonSlideData(CreateShapeTree()) { Name = "Office Theme" },
            new P.ColorMap
            {
                Background1 = A.ColorSchemeIndexValues.Light1,
                Text1 = A.ColorSchemeIndexValues.Dark1,
                Background2 = A.ColorSchemeIndexValues.Light2,
                Text2 = A.ColorSchemeIndexValues.Dark2,
                Accent1 = A.ColorSchemeIndexValues.Accent1,
                Accent2 = A.ColorSchemeIndexValues.Accent2,
                Accent3 = A.ColorSchemeIndexValues.Accent3,
                Accent4 = A.ColorSchemeIndexValues.Accent4,
                Accent5 = A.ColorSchemeIndexValues.Accent5,
                Accent6 = A.ColorSchemeIndexValues.Accent6,
                Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
                FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink
            },
            new P.SlideLayoutIdList(
                new P.SlideLayoutId
                {
                    Id = 2147483649U,
                    RelationshipId = layoutRelationshipId
                }),
            new P.TextStyles(new P.TitleStyle(), new P.BodyStyle(), new P.OtherStyle()));

        var slideIdList = new P.SlideIdList();
        presentationPart.Presentation = new P.Presentation(
            new P.SlideMasterIdList(
                new P.SlideMasterId
                {
                    Id = 2147483648U,
                    RelationshipId = presentationPart.GetIdOfPart(slideMasterPart)
                }),
            slideIdList,
            new P.SlideSize
            {
                Cx = checked((int)slideWidth),
                Cy = checked((int)slideHeight)
            },
            new P.NotesSize { Cx = 6858000L, Cy = 9144000L });

        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            var slidePart = presentationPart.AddNewPart<SlidePart>();
            slidePart.AddPart(slideLayoutPart);
            var imagePart = slidePart.AddImagePart(ImagePartType.Jpeg);
            using (var imageStream = File.OpenRead(pages[pageIndex].ImagePath))
            {
                imagePart.FeedData(imageStream);
            }

            var imageRelationshipId = slidePart.GetIdOfPart(imagePart);
            slidePart.Slide = new P.Slide(
                new P.CommonSlideData(
                    CreateShapeTree(
                        new P.Picture(
                            new P.NonVisualPictureProperties(
                                new P.NonVisualDrawingProperties
                                {
                                    Id = 2U,
                                    Name = $"PDF page {pageIndex + 1}"
                                },
                                new P.NonVisualPictureDrawingProperties(
                                    new A.PictureLocks { NoChangeAspect = true }),
                                new P.ApplicationNonVisualDrawingProperties()),
                            new P.BlipFill(
                                new A.Blip { Embed = imageRelationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new P.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = slideWidth, Cy = slideHeight }),
                                new A.PresetGeometry(new A.AdjustValueList())
                                {
                                    Preset = A.ShapeTypeValues.Rectangle
                                })))),
                new P.ColorMapOverride(new A.MasterColorMapping()));

            slideIdList.Append(new P.SlideId
            {
                Id = checked((uint)(256 + pageIndex)),
                RelationshipId = presentationPart.GetIdOfPart(slidePart)
            });
        }

        presentationPart.Presentation.Save();
    }

    private static P.ShapeTree CreateShapeTree(params OpenXmlElement[] children)
    {
        var shapeTree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(
                new A.TransformGroup(
                    new A.Offset { X = 0L, Y = 0L },
                    new A.Extents { Cx = 0L, Cy = 0L },
                    new A.ChildOffset { X = 0L, Y = 0L },
                    new A.ChildExtents { Cx = 0L, Cy = 0L })));
        shapeTree.Append(children);
        return shapeTree;
    }

    private static A.Theme CreateTheme()
    {
        var colorScheme = new A.ColorScheme(
            new A.Dark1Color(new A.SystemColor { Val = A.SystemColorValues.WindowText, LastColor = "000000" }),
            new A.Light1Color(new A.SystemColor { Val = A.SystemColorValues.Window, LastColor = "FFFFFF" }),
            new A.Dark2Color(new A.RgbColorModelHex { Val = "1F497D" }),
            new A.Light2Color(new A.RgbColorModelHex { Val = "EEECE1" }),
            new A.Accent1Color(new A.RgbColorModelHex { Val = "4F81BD" }),
            new A.Accent2Color(new A.RgbColorModelHex { Val = "C0504D" }),
            new A.Accent3Color(new A.RgbColorModelHex { Val = "9BBB59" }),
            new A.Accent4Color(new A.RgbColorModelHex { Val = "8064A2" }),
            new A.Accent5Color(new A.RgbColorModelHex { Val = "4BACC6" }),
            new A.Accent6Color(new A.RgbColorModelHex { Val = "F79646" }),
            new A.Hyperlink(new A.RgbColorModelHex { Val = "0000FF" }),
            new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "800080" }))
        {
            Name = "Office"
        };
        var fontScheme = new A.FontScheme(
            new A.MajorFont(
                new A.LatinFont { Typeface = "Arial" },
                new A.EastAsianFont { Typeface = string.Empty },
                new A.ComplexScriptFont { Typeface = string.Empty }),
            new A.MinorFont(
                new A.LatinFont { Typeface = "Arial" },
                new A.EastAsianFont { Typeface = string.Empty },
                new A.ComplexScriptFont { Typeface = string.Empty }))
        {
            Name = "Office"
        };
        var formatScheme = new A.FormatScheme(
            new A.FillStyleList(
                new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })),
            new A.LineStyleList(
                CreateOutline(),
                CreateOutline(),
                CreateOutline()),
            new A.EffectStyleList(
                new A.EffectStyle(new A.EffectList()),
                new A.EffectStyle(new A.EffectList()),
                new A.EffectStyle(new A.EffectList())),
            new A.BackgroundFillStyleList(
                new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
                new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor })))
        {
            Name = "Office"
        };

        return new A.Theme(new A.ThemeElements(colorScheme, fontScheme, formatScheme))
        {
            Name = "Office"
        };
    }

    private static A.Outline CreateOutline()
        => new(
            new A.SolidFill(new A.SchemeColor { Val = A.SchemeColorValues.PhColor }),
            new A.PresetDash { Val = A.PresetLineDashValues.Solid });

    private static string GetSpreadsheetColumnName(int columnIndex)
    {
        if (columnIndex < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        }

        var result = string.Empty;
        while (columnIndex > 0)
        {
            columnIndex--;
            result = (char)('A' + (columnIndex % 26)) + result;
            columnIndex /= 26;
        }

        return result;
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return 1d;
        }

        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2d
            : sorted[middle];
    }

    private static string ValidateSource(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        var sourcePath = Path.GetFullPath(source);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source PDF does not exist.", sourcePath);
        }

        if (!string.Equals(Path.GetExtension(sourcePath), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfProcessingException("PDF-to-Office conversion requires exactly one .pdf source file.");
        }

        return sourcePath;
    }

    private static string ValidateOutput(string output, OfficeExportFormat format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        var outputPath = Path.GetFullPath(output);
        var expectedExtension = format switch
        {
            OfficeExportFormat.Word => ".docx",
            OfficeExportFormat.Excel => ".xlsx",
            OfficeExportFormat.PowerPoint => ".pptx",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown Office export format.")
        };
        if (!string.Equals(Path.GetExtension(outputPath), expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfProcessingException(
                $"{format} output must use the '{expectedExtension}' extension.");
        }

        if (File.Exists(outputPath) || Directory.Exists(outputPath))
        {
            throw new IOException($"Output already exists: {outputPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        return outputPath;
    }

    private static void TryDeleteDirectory(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Could not delete temporary PDF-to-Office folder '{folder}': {ex.Message}");
        }
    }

    private sealed class WordRow(double baseline)
    {
        public double Baseline { get; } = baseline;

        public List<Word> Words { get; } = [];
    }

    private sealed record RenderedPage(string ImagePath, double WidthPoints, double HeightPoints);
}
