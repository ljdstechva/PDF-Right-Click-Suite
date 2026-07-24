using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using PdfRightClickSuite.Core;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using System.Security.Cryptography;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace PdfRightClickSuite.Tests;

public sealed class PdfToOfficeConversionTests
{
    [Fact]
    public async Task Excel_conversion_creates_one_valid_sheet_per_page_with_extracted_grid()
    {
        using var temp = new TemporaryDirectory();
        var source = CreateTextPdf(temp.Path, "table.pdf", pageCount: 2);
        var output = Path.Combine(temp.Path, "table.xlsx");
        var sourceHash = Sha256File(source);

        var result = await new PdfToOfficeConvertService(new ExternalToolLocator())
            .ConvertAsync(source, output, OfficeExportFormat.Excel, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.PageCount);
        Assert.Contains("PdfPig", result.BackendUsed, StringComparison.Ordinal);
        Assert.Equal(sourceHash, Sha256File(source));
        using var workbook = SpreadsheetDocument.Open(output, false);
        var workbookPart = workbook.WorkbookPart ?? throw new InvalidDataException("Workbook part is missing.");
        var workbookRoot = workbookPart.Workbook ?? throw new InvalidDataException("Workbook root is missing.");
        var sheets = workbookRoot.Sheets ?? throw new InvalidDataException("Workbook sheets are missing.");
        Assert.Equal(2, sheets.Elements<S.Sheet>().Count());
        var firstSheet = sheets.Elements<S.Sheet>().First();
        var firstSheetRelationship = firstSheet.Id?.Value
            ?? throw new InvalidDataException("First sheet relationship is missing.");
        var firstWorksheet = ((WorksheetPart)workbookPart.GetPartById(firstSheetRelationship)).Worksheet
            ?? throw new InvalidDataException("First worksheet is missing.");
        Assert.Equal("Project", firstWorksheet.Descendants<S.Cell>().Single(cell => cell.CellReference == "A1").InnerText);
        Assert.Equal("Page 1", firstWorksheet.Descendants<S.Cell>().Single(cell => cell.CellReference == "B1").InnerText);
        Assert.Equal("Flow", firstWorksheet.Descendants<S.Cell>().Single(cell => cell.CellReference == "A2").InnerText);
        Assert.Equal("10 m3/day", firstWorksheet.Descendants<S.Cell>().Single(cell => cell.CellReference == "B2").InnerText);
        var text = string.Join(
            " ",
            workbookPart.WorksheetParts
                .SelectMany(part => (part.Worksheet ?? throw new InvalidDataException("Worksheet is missing.")).Descendants<S.Text>())
                .Select(value => value.Text));
        Assert.Contains("Project", text, StringComparison.Ordinal);
        Assert.Empty(new OpenXmlValidator().Validate(workbook, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PowerPoint_conversion_creates_one_valid_full_slide_image_per_page()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TemporaryDirectory();
        var source = CreateTextPdf(temp.Path, "slides.pdf", pageCount: 2);
        var output = Path.Combine(temp.Path, "slides.pptx");
        var sourceHash = Sha256File(source);

        var result = await new PdfToOfficeConvertService(new ExternalToolLocator())
            .ConvertAsync(source, output, OfficeExportFormat.PowerPoint, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.PageCount);
        Assert.Contains("PDFtoImage", result.BackendUsed, StringComparison.Ordinal);
        Assert.Equal(sourceHash, Sha256File(source));
        using var presentation = PresentationDocument.Open(output, false);
        var presentationPart = presentation.PresentationPart ?? throw new InvalidDataException("Presentation part is missing.");
        var presentationRoot = presentationPart.Presentation ?? throw new InvalidDataException("Presentation root is missing.");
        var slideIdList = presentationRoot.SlideIdList
            ?? throw new InvalidDataException("Presentation slide list is missing.");
        Assert.Equal(2, slideIdList.ChildElements.Count);
        Assert.All(presentationPart.SlideParts, part => Assert.Single(part.ImageParts));
        Assert.Empty(new OpenXmlValidator().Validate(presentation, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Excel_conversion_rejects_image_only_pdf_with_ocr_guidance()
    {
        using var temp = new TemporaryDirectory();
        var source = CreateImageOnlyPdf(temp.Path, "scan.pdf");
        var output = Path.Combine(temp.Path, "scan.xlsx");

        var exception = await Assert.ThrowsAsync<PdfProcessingException>(() =>
            new PdfToOfficeConvertService(new ExternalToolLocator())
                .ConvertAsync(source, output, OfficeExportFormat.Excel, TestContext.Current.CancellationToken));

        Assert.Equal(
            "This PDF has no extractable text (it may be a scanned image), so it cannot be converted to Excel.",
            exception.Message);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task PowerPoint_conversion_accepts_image_only_pdf()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TemporaryDirectory();
        var source = CreateImageOnlyPdf(temp.Path, "scan.pdf");
        var output = Path.Combine(temp.Path, "scan.pptx");

        await new PdfToOfficeConvertService(new ExternalToolLocator())
            .ConvertAsync(source, output, OfficeExportFormat.PowerPoint, TestContext.Current.CancellationToken);

        using var presentation = PresentationDocument.Open(output, false);
        var slideCount = presentation.PresentationPart?.Presentation?.SlideIdList?.ChildElements.Count ?? 0;
        Assert.Equal(1, slideCount);
    }

    [Fact]
    public async Task Excel_cancellation_removes_staged_output_and_preserves_source()
    {
        using var temp = new TemporaryDirectory();
        var source = CreateTextPdf(temp.Path, "cancel.pdf", pageCount: 3);
        var output = Path.Combine(temp.Path, "cancel.xlsx");
        var sourceHash = Sha256File(source);
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "PdfRightClickSuite");
        var workspacesBefore = GetGuidWorkspaces(workspaceRoot);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress = new CallbackProgress<int>(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PdfToOfficeConvertService(new ExternalToolLocator())
                .ConvertAsync(source, output, OfficeExportFormat.Excel, cancellation.Token, progress));

        Assert.False(File.Exists(output));
        Assert.Equal(sourceHash, Sha256File(source));
        Assert.Empty(Directory.GetFiles(temp.Path, ".*.tmp"));
        Assert.Equal(workspacesBefore, GetGuidWorkspaces(workspaceRoot));
    }

    [Fact]
    public async Task Conversion_rejects_existing_output_without_modifying_it()
    {
        using var temp = new TemporaryDirectory();
        var source = CreateTextPdf(temp.Path, "source.pdf", pageCount: 1);
        var output = temp.CreateFile("source.xlsx", "user-owned");

        var exception = await Assert.ThrowsAsync<IOException>(() =>
            new PdfToOfficeConvertService(new ExternalToolLocator())
                .ConvertAsync(source, output, OfficeExportFormat.Excel, TestContext.Current.CancellationToken));

        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("user-owned", File.ReadAllText(output));
    }

    [Fact]
    public async Task Word_conversion_handles_long_paths_when_microsoft_word_is_available()
    {
        if (!OperatingSystem.IsWindows() || !MicrosoftOfficePdfConverter.IsPdfToDocxAvailable())
        {
            return;
        }

        using var temp = new TemporaryDirectory();
        const string fileName = "Statement of No Planned Process or Output Changes.pdf";
        var folder = temp.Path;
        while (Path.Combine(folder, fileName).Length < 232)
        {
            folder = Path.Combine(folder, "long-office-path");
        }

        Directory.CreateDirectory(folder);
        var source = CreateTextPdf(folder, fileName, pageCount: 1);
        var output = Path.ChangeExtension(source, ".docx");
        var sourceHash = Sha256File(source);
        Assert.InRange(output.Length, 220, 259);

        var result = await new PdfToOfficeConvertService(new ExternalToolLocator())
            .ConvertAsync(source, output, OfficeExportFormat.Word, TestContext.Current.CancellationToken);

        Assert.Contains("Microsoft Word", result.BackendUsed, StringComparison.Ordinal);
        Assert.Equal(sourceHash, Sha256File(source));
        using var document = WordprocessingDocument.Open(output, false);
        Assert.NotNull(document.MainDocumentPart);
    }

    private static string CreateTextPdf(string folder, string fileName, int pageCount)
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        var path = Path.Combine(folder, fileName);
        using var document = new PdfDocument();
        for (var pageNumber = 1; pageNumber <= pageCount; pageNumber++)
        {
            var page = document.AddPage();
            using var graphics = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 12);
            graphics.DrawString("Project", font, XBrushes.Black, new XPoint(72, 100));
            graphics.DrawString($"Page {pageNumber}", font, XBrushes.Black, new XPoint(260, 100));
            graphics.DrawString("Flow", font, XBrushes.Black, new XPoint(72, 130));
            graphics.DrawString($"{pageNumber * 10} m3/day", font, XBrushes.Black, new XPoint(260, 130));
        }

        document.Save(path);
        return path;
    }

    private static string CreateImageOnlyPdf(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        using var document = new PdfDocument();
        var page = document.AddPage();
        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawRectangle(XBrushes.LightGray, 72, 72, 300, 200);
        document.Save(path);
        return path;
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string[] GetGuidWorkspaces(string root)
        => Directory.Exists(root)
            ? Directory.GetDirectories(root)
                .Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
