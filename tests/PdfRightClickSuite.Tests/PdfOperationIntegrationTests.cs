using PDFtoImage;
using PdfRightClickSuite.Core;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SkiaSharp;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PdfRightClickSuite.Tests;

public sealed class PdfOperationIntegrationTests
{
    [Fact]
    public async Task Merge_combines_generated_pdfs_and_preserves_page_count()
    {
        using var temp = new TemporaryDirectory();
        var first = CreateSamplePdf(temp.Path, "one.pdf", "one");
        var second = CreateSamplePdf(temp.Path, "two.pdf", "two");
        var output = Path.Combine(temp.Path, "merged.pdf");

        await new PdfMergeService(new PdfPageCountService()).MergeAsync([first, second], output, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.Equal(2, new PdfPageCountService().GetPageCount(output));
    }

    [Fact]
    public async Task Split_writes_one_pdf_per_requested_page()
    {
        using var temp = new TemporaryDirectory();
        var source = CreateMultiPagePdf(temp.Path, "source.pdf", pageCount: 3);
        var outputFolder = Path.Combine(temp.Path, "split");

        var outputs = await new PdfSplitService(new PdfPageCountService())
            .SplitAsync(source, [1, 3], outputFolder, CancellationToken.None);

        Assert.Equal(2, outputs.Count);
        Assert.All(outputs, path =>
        {
            Assert.True(File.Exists(path));
            Assert.Equal(1, new PdfPageCountService().GetPageCount(path));
        });
        Assert.EndsWith("_p001.pdf", outputs[0], StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("_p003.pdf", outputs[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Split_cancellation_rolls_back_created_pages_and_new_output_folder()
    {
        using var temp = new TemporaryDirectory();
        var source = CreateMultiPagePdf(temp.Path, "source.pdf", pageCount: 3);
        var outputFolder = Path.Combine(temp.Path, "split-cancelled");
        using var cancellation = new CancellationTokenSource();
        var progress = new CallbackProgress<int>(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PdfSplitService(new PdfPageCountService())
                .SplitAsync(source, [1, 2, 3], outputFolder, cancellation.Token, progress));

        Assert.False(Directory.Exists(outputFolder));
    }

    [Fact]
    public async Task Convert_image_creates_pdf()
    {
        using var temp = new TemporaryDirectory();
        var imagePath = CreateSamplePng(temp.Path, "image.png");
        var output = Path.Combine(temp.Path, "image.pdf");

        await new PdfConvertService(new PdfMergeService(new PdfPageCountService()), new ExternalToolLocator())
            .ConvertSingleAsync(imagePath, output, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.Equal(1, new PdfPageCountService().GetPageCount(output));
    }

    [Fact]
    public async Task Convert_docx_creates_pdf_with_available_office_backend()
    {
        if (!OperatingSystem.IsWindows() || !MicrosoftOfficePdfConverter.IsAvailableFor(".docx"))
        {
            return;
        }

        using var temp = new TemporaryDirectory();
        var docxPath = CreateSampleDocx(temp.Path, "office-sample.docx");
        var output = Path.Combine(temp.Path, "office-sample.pdf");

        await new PdfConvertService(new PdfMergeService(new PdfPageCountService()), new ExternalToolLocator())
            .ConvertSingleAsync(docxPath, output, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.True(new PdfPageCountService().GetPageCount(output) >= 1);
    }

    [Fact]
    public void Convert_docx_handles_long_output_path_with_available_office_backend()
    {
        if (!OperatingSystem.IsWindows() || !MicrosoftOfficePdfConverter.IsAvailableFor(".docx"))
        {
            return;
        }

        using var temp = new TemporaryDirectory();
        const string fileName = "Statement of No Planned Process or Output Changes.docx";
        var folder = temp.Path;
        while (Path.Combine(folder, fileName).Length < 232)
        {
            folder = Path.Combine(folder, "long-office-path");
        }

        Directory.CreateDirectory(folder);
        var docxPath = CreateSampleDocx(folder, fileName);
        var output = Path.ChangeExtension(docxPath, ".pdf");
        var previousTempPath = Path.Combine(
            folder,
            $".{Path.GetFileNameWithoutExtension(output)}.{new string('0', 32)}.pdf");

        Assert.InRange(output.Length, 220, 259);
        Assert.True(previousTempPath.Length >= 260, $"Expected the previous Office export path to exceed 260 characters, got {previousTempPath.Length}.");

        MicrosoftOfficePdfConverter.Convert(docxPath, output, CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.True(new PdfPageCountService().GetPageCount(output) >= 1);
    }

    [Fact]
    public void Office_conversion_falls_back_to_microsoft_office_when_libreoffice_is_missing()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "PdfRightClickSuite.Core", "PdfConvertService.cs"));
        var methodStart = source.IndexOf("private async Task ConvertOfficeWithLibreOfficeAsync", StringComparison.Ordinal);
        var nextMethodStart = source.IndexOf("private static async Task<ProcessResult>", StringComparison.Ordinal);
        Assert.True(methodStart >= 0, "ConvertOfficeWithLibreOfficeAsync was not found.");
        Assert.True(nextMethodStart > methodStart, "RunProcessAsync was not found after ConvertOfficeWithLibreOfficeAsync.");

        var method = source[methodStart..nextMethodStart];

        Assert.Contains("externalToolLocator.FindLibreOffice()", method, StringComparison.Ordinal);
        Assert.Contains("MicrosoftOfficePdfConverter.IsAvailableFor", method, StringComparison.Ordinal);
        Assert.Contains("MicrosoftOfficePdfConverter.Convert", method, StringComparison.Ordinal);
        Assert.Contains("LibreOffice was not found and Microsoft Office desktop conversion is not available", method, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scan_creates_scanned_like_pdf()
    {
        using var temp = new TemporaryDirectory();
        var source = CreateSamplePdf(temp.Path, "source.pdf", "scan");
        var output = Path.Combine(temp.Path, "source_scanned.pdf");
        var sourceHash = Sha256File(source);
        var settings = ScanEffectSettings.Default;

        await new PdfScanEffectService()
            .CreateScannedLikePdfAsync(source, output, settings, cancellationToken: CancellationToken.None);

        Assert.True(File.Exists(output));
        Assert.Equal(1, new PdfPageCountService().GetPageCount(output));
        Assert.Equal(sourceHash, Sha256File(source));
        Assert.Contains("/Image", Encoding.Latin1.GetString(File.ReadAllBytes(output)), StringComparison.Ordinal);
        Assert.Equal(ScanStrength.LowQuality, settings.Strength);
    }

    [Fact]
    public async Task Scan_colored_preserves_color_while_black_and_white_outputs_grayscale()
    {
        using var temp = new TemporaryDirectory();
        var source = CreateColorSamplePdf(temp.Path, "color-source.pdf");
        var blackAndWhiteOutput = Path.Combine(temp.Path, "color-source_scanned.pdf");
        var coloredOutput = Path.Combine(temp.Path, "color-source_scanned_colored.pdf");
        var settings = ScanEffectSettings.ForPreset(ScanStrength.LowQuality, seed: 42);

        await new PdfScanEffectService()
            .CreateScannedLikePdfAsync(source, blackAndWhiteOutput, settings, cancellationToken: CancellationToken.None);
        await new PdfScanEffectService()
            .CreateScannedLikePdfAsync(source, coloredOutput, settings, cancellationToken: CancellationToken.None, colorMode: ScanColorMode.Colored);

        Assert.True(File.Exists(blackAndWhiteOutput));
        Assert.True(File.Exists(coloredOutput));
        Assert.Equal(1.05f, settings.BlurRadius);
        Assert.Equal(1.5f, settings.MaxRotationDegrees);
        Assert.True(CountColoredSamplePixels(blackAndWhiteOutput) < 5);
        Assert.True(CountColoredSamplePixels(coloredOutput) > 20);
    }

    [Fact]
    public async Task Scan_default_creates_degraded_high_contrast_scanner_output()
    {
        using var temp = new TemporaryDirectory();
        var source = CreateDenseTextSamplePdf(temp.Path, "document-source.pdf");
        var output = Path.Combine(temp.Path, "document-source_scanned.pdf");

        await new PdfScanEffectService()
            .CreateScannedLikePdfAsync(source, output, ScanEffectSettings.Default, cancellationToken: CancellationToken.None);

        var metrics = MeasureScanOutput(output);
        Assert.True(metrics.BrightPercent > 75, $"Expected a mostly light scanned background, got {metrics.BrightPercent:0.##}% bright pixels.");
        Assert.True(metrics.DarkPercent > 0.8, $"Expected readable dark text pixels, got {metrics.DarkPercent:0.##}% dark pixels.");
        Assert.True(metrics.BackgroundNoiseStd < 12, $"Expected controlled scanner texture, got stddev {metrics.BackgroundNoiseStd:0.##}.");
    }

    [Fact]
    public void Scan_rotation_is_counterclockwise_and_near_one_and_a_half_degrees()
    {
        var method = typeof(PdfScanEffectService).GetMethod(
            "CalculateCounterClockwiseRotationDegrees",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        for (var seed = 0; seed < 20; seed++)
        {
            var degrees = (float)method.Invoke(null, new object[] { new Random(seed), 1.5f })!;
            Assert.InRange(degrees, -1.5f, -1.35f);
        }
    }

    [Fact]
    public void Scan_settings_default_to_low_quality_with_supported_presets()
    {
        var light = ScanEffectSettings.ForPreset(ScanStrength.Light);
        var lowQuality = ScanEffectSettings.Default;
        var rough = ScanEffectSettings.ForPreset(ScanStrength.Rough);

        Assert.Equal(ScanStrength.LowQuality, lowQuality.Strength);
        Assert.Equal(150, lowQuality.Dpi);
        Assert.InRange(lowQuality.JpegQuality, 55, 60);
        Assert.InRange(lowQuality.BlurRadius, 1f, 1.1f);
        Assert.Equal(1.5f, lowQuality.MaxRotationDegrees);
        Assert.Equal(1.5f, rough.MaxRotationDegrees);
        Assert.True(lowQuality.BackgroundCleanup < 0.8f);
        Assert.True(lowQuality.TextDarkening > 0.65f);
        Assert.True(lowQuality.WhitePoint >= 215);
        Assert.True(lowQuality.BlackPoint <= 90);
        Assert.True(lowQuality.Dpi < light.Dpi);
        Assert.True(lowQuality.JpegQuality < light.JpegQuality);
        Assert.True(lowQuality.BlurRadius > light.BlurRadius);
        Assert.True(rough.Dpi < lowQuality.Dpi);
        Assert.True(rough.JpegQuality < lowQuality.JpegQuality);
        Assert.True(rough.BlurRadius > lowQuality.BlurRadius);
        Assert.True(rough.NoiseAmplitude > lowQuality.NoiseAmplitude);
        Assert.True(ScanEffectSettings.TryParseStrength("low-quality", out var parsed));
        Assert.Equal(ScanStrength.LowQuality, parsed);
        Assert.False(ScanEffectSettings.TryParseStrength("medium", out _));
    }

    private static string CreateSamplePdf(string folder, string fileName, string text)
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        var path = Path.Combine(folder, fileName);
        using var document = new PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 16);
        gfx.DrawString(text, font, XBrushes.Black, new XRect(0, 0, page.Width.Point, page.Height.Point), XStringFormats.Center);
        document.Save(path);
        return path;
    }

    private static string CreateDenseTextSamplePdf(string folder, string fileName)
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        var path = Path.Combine(folder, fileName);
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(595);
        page.Height = XUnit.FromPoint(842);
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        var titleFont = new XFont("Arial", 15, XFontStyleEx.Bold);
        var bodyFont = new XFont("Arial", 11);
        gfx.DrawString("JOINT AFFIDAVIT OF UNDERTAKING", titleFont, XBrushes.Black, new XRect(72, 88, 451, 28), XStringFormats.Center);
        for (var i = 0; i < 26; i++)
        {
            var y = 138 + (i * 22);
            gfx.DrawString($"This is sample legal document text line {i + 1:00} with clean scanner output metrics.", bodyFont, XBrushes.Black, new XPoint(84, y));
        }

        document.Save(path);
        return path;
    }

    private static string CreateColorSamplePdf(string folder, string fileName)
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        var path = Path.Combine(folder, fileName);
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(612);
        page.Height = XUnit.FromPoint(792);
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(230, 30, 35)), 96, 120, 180, 140);
        gfx.DrawEllipse(new XSolidBrush(XColor.FromArgb(35, 90, 220)), 330, 140, 150, 150);
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(40, 155, 80)), 150, 360, 320, 90);
        document.Save(path);
        return path;
    }

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static int CountColoredSamplePixels(string pdfPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Rendered scan verification is supported on Windows in this test suite.");
        }

        using var stream = File.OpenRead(pdfPath);
        using var bitmap = Conversion.ToImage(
            stream,
            Index.FromStart(0),
            leaveOpen: false,
            password: null,
            options: new RenderOptions
            {
                Dpi = 96,
                Grayscale = false,
                WithAnnotations = true,
                WithFormFill = true,
                UseTiling = true
            });

        var colored = 0;
        var stepX = Math.Max(1, bitmap.Width / 80);
        var stepY = Math.Max(1, bitmap.Height / 80);
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
                var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
                if (max - min > 30)
                {
                    colored++;
                }
            }
        }

        return colored;
    }

    private static ScanOutputMetrics MeasureScanOutput(string pdfPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Rendered scan verification is supported on Windows in this test suite.");
        }

        using var stream = File.OpenRead(pdfPath);
        using var bitmap = Conversion.ToImage(
            stream,
            Index.FromStart(0),
            leaveOpen: false,
            password: null,
            options: new RenderOptions
            {
                Dpi = 96,
                Grayscale = false,
                WithAnnotations = true,
                WithFormFill = true,
                UseTiling = true
            });

        var total = 0;
        var bright = 0;
        var dark = 0;
        var background = new List<double>();
        var stepX = Math.Max(1, bitmap.Width / 160);
        var stepY = Math.Max(1, bitmap.Height / 160);
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var color = bitmap.GetPixel(x, y);
                var gray = (color.Red * 0.299) + (color.Green * 0.587) + (color.Blue * 0.114);
                total++;
                if (gray >= 245)
                {
                    bright++;
                    background.Add(gray);
                }

                if (gray < 96)
                {
                    dark++;
                }
            }
        }

        var average = background.Count == 0 ? 0 : background.Average();
        var variance = background.Count == 0 ? 0 : background.Sum(value => Math.Pow(value - average, 2)) / background.Count;
        return new ScanOutputMetrics(
            bright * 100.0 / total,
            dark * 100.0 / total,
            Math.Sqrt(variance));
    }

    private sealed record ScanOutputMetrics(double BrightPercent, double DarkPercent, double BackgroundNoiseStd);

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    private static string CreateSampleDocx(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        WriteZipEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        WriteZipEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        WriteZipEntry(
            archive,
            "word/document.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p>
                  <w:r>
                    <w:t>PdfRightClickSuite Word conversion backend test.</w:t>
                  </w:r>
                </w:p>
                <w:p>
                  <w:r>
                    <w:t>This DOCX was generated locally by the integration test.</w:t>
                  </w:r>
                </w:p>
                <w:sectPr>
                  <w:pgSz w:w="12240" w:h="15840"/>
                  <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
                </w:sectPr>
              </w:body>
            </w:document>
            """);

        return path;
    }

    private static void WriteZipEntry(ZipArchive archive, string name, string contents)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(contents);
    }

    private static string CreateMultiPagePdf(string folder, string fileName, int pageCount)
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        var path = Path.Combine(folder, fileName);
        using var document = new PdfDocument();
        for (var i = 1; i <= pageCount; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            var font = new XFont("Arial", 16);
            gfx.DrawString($"page {i}", font, XBrushes.Black, new XRect(0, 0, page.Width.Point, page.Height.Point), XStringFormats.Center);
        }

        document.Save(path);
        return path;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PdfRightClickSuite.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate PdfRightClickSuite.sln.");
    }

    private static string CreateSamplePng(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        using var bitmap = new SKBitmap(120, 80);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = SKColors.DarkBlue, IsAntialias = true };
        canvas.DrawRect(new SKRect(15, 15, 105, 65), paint);
        paint.Color = SKColors.White;
        canvas.DrawCircle(60, 40, 18, paint);
        using var stream = File.Create(path);
        bitmap.Encode(stream, SKEncodedImageFormat.Png, quality: 100);
        return path;
    }
}
