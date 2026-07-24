using System.Diagnostics;
using System.ComponentModel;
using System.Security;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;

namespace PdfRightClickSuite.Core;

public sealed class PdfConvertService(PdfMergeService mergeService, ExternalToolLocator externalToolLocator)
{
    private static readonly ISet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt",
        ".log",
        ".csv",
        ".md"
    };

    private static readonly ISet<string> HtmlExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".html",
        ".htm"
    };

    private static readonly ISet<string> OfficeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".doc",
        ".docx",
        ".rtf",
        ".odt",
        ".xls",
        ".xlsx",
        ".ods",
        ".ppt",
        ".pptx",
        ".odp"
    };

    public async Task ConvertSingleAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Input file does not exist.", inputPath);
        }

        var extension = Path.GetExtension(inputPath);
        if (SelectionClassifier.ImageExtensions.Contains(extension))
        {
            ConvertImageToPdf(inputPath, outputPath, cancellationToken);
            return;
        }

        if (TextExtensions.Contains(extension))
        {
            ConvertTextToPdf(inputPath, outputPath, cancellationToken);
            return;
        }

        if (HtmlExtensions.Contains(extension))
        {
            await ConvertHtmlToPdfAsync(inputPath, outputPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (OfficeExtensions.Contains(extension))
        {
            await ConvertOfficeWithLibreOfficeAsync(inputPath, outputPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        throw new NotSupportedException($"Unsupported conversion format '{extension}'. Supported: images, txt, log, csv, md, html, htm, and Office document formats.");
    }

    public async Task ConvertManyAndMergeAsync(IReadOnlyList<string> inputFiles, string outputPath, CancellationToken cancellationToken, IProgress<int>? progress = null)
    {
        if (inputFiles.Count == 0)
        {
            throw new ArgumentException("At least one input file is required.", nameof(inputFiles));
        }

        var tempFolder = Path.Combine(Path.GetTempPath(), "PdfRightClickSuite", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        var tempPdfs = new List<string>();
        try
        {
            for (var i = 0; i < inputFiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tempPdf = Path.Combine(tempFolder, $"{i:0000}.pdf");
                await ConvertSingleAsync(inputFiles[i], tempPdf, cancellationToken).ConfigureAwait(false);
                tempPdfs.Add(tempPdf);
                progress?.Report(i + 1);
            }

            await mergeService.MergeAsync(tempPdfs, outputPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(tempFolder);
        }
    }

    private static void ConvertImageToPdf(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedImage = Path.Combine(Path.GetTempPath(), "PdfRightClickSuite", $"{Guid.NewGuid():N}.png");
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedImage)!);
        var tempPdf = AtomicFileWriter.CreateTempPathBeside(outputPath);

        try
        {
            using var bitmap = SKBitmap.Decode(inputPath)
                ?? throw new NotSupportedException($"Could not decode image file: {inputPath}");
            using (var stream = File.Create(normalizedImage))
            {
                bitmap.Encode(stream, SKEncodedImageFormat.Png, quality: 100);
            }

            PdfSharpBootstrap.EnsureInitialized();
            using var document = new PdfDocument();
            var page = document.AddPage();
            var landscape = bitmap.Width > bitmap.Height;
            page.Width = XUnit.FromPoint(landscape ? 792 : 612);
            page.Height = XUnit.FromPoint(landscape ? 612 : 792);

            using var image = XImage.FromFile(normalizedImage);
            using var gfx = XGraphics.FromPdfPage(page);
            var rect = FitInside(image.PixelWidth, image.PixelHeight, page.Width.Point, page.Height.Point, margin: 36);
            gfx.DrawImage(image, rect.X, rect.Y, rect.Width, rect.Height);
            cancellationToken.ThrowIfCancellationRequested();
            document.Save(tempPdf);
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFileWriter.MoveIntoPlace(tempPdf, outputPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PdfProcessingException($"Image conversion failed for '{inputPath}'.", ex);
        }
        finally
        {
            AtomicFileWriter.TryDelete(normalizedImage);
            AtomicFileWriter.TryDelete(tempPdf);
        }
    }

    private static void ConvertTextToPdf(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        PdfSharpBootstrap.EnsureInitialized();
        var tempPdf = AtomicFileWriter.CreateTempPathBeside(outputPath);
        try
        {
            using var document = new PdfDocument();
            using var reader = new StreamReader(inputPath);
            var font = new XFont("Courier New", 9.5);
            PdfPage? page = null;
            XGraphics? gfx = null;
            var y = 0.0;
            var lineHeight = 13.0;
            const double margin = 42;
            const int maxChars = 96;

            try
            {
                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = reader.ReadLine() ?? string.Empty;
                    foreach (var wrapped in WrapLine(line, maxChars))
                    {
                        if (page is null || gfx is null || y > page.Height.Point - margin)
                        {
                            gfx?.Dispose();
                            page = document.AddPage();
                            page.Width = XUnit.FromPoint(612);
                            page.Height = XUnit.FromPoint(792);
                            gfx = XGraphics.FromPdfPage(page);
                            y = margin;
                        }

                        gfx.DrawString(wrapped, font, XBrushes.Black, margin, y);
                        y += lineHeight;
                    }
                }
            }
            finally
            {
                gfx?.Dispose();
            }

            if (document.PageCount == 0)
            {
                var empty = document.AddPage();
                using var emptyGfx = XGraphics.FromPdfPage(empty);
                emptyGfx.DrawString(string.Empty, font, XBrushes.Black, 42, 42);
            }

            cancellationToken.ThrowIfCancellationRequested();
            document.Save(tempPdf);
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFileWriter.MoveIntoPlace(tempPdf, outputPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PdfProcessingException($"Text conversion failed for '{inputPath}'.", ex);
        }
        finally
        {
            AtomicFileWriter.TryDelete(tempPdf);
        }
    }

    private async Task ConvertHtmlToPdfAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        var edge = externalToolLocator.FindEdge();
        if (edge is not null)
        {
            var tempPdf = AtomicFileWriter.CreateTempPathBeside(outputPath);
            try
            {
                var result = await RunProcessAsync(
                    edge,
                    [
                        "--headless",
                        "--disable-gpu",
                        $"--print-to-pdf={tempPdf}",
                        new Uri(Path.GetFullPath(inputPath)).AbsoluteUri
                    ],
                    TimeSpan.FromMinutes(2),
                    cancellationToken).ConfigureAwait(false);

                if (result.ExitCode == 0 && File.Exists(tempPdf))
                {
                    AtomicFileWriter.MoveIntoPlace(tempPdf, outputPath);
                    return;
                }

                AtomicFileWriter.TryDelete(tempPdf);
            }
            finally
            {
                AtomicFileWriter.TryDelete(tempPdf);
            }
        }

        await ConvertOfficeWithLibreOfficeAsync(inputPath, outputPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task ConvertOfficeWithLibreOfficeAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        var libreOffice = externalToolLocator.FindLibreOffice();
        if (libreOffice is null)
        {
            var extension = Path.GetExtension(inputPath);
            if (MicrosoftOfficePdfConverter.CanConvert(extension) && MicrosoftOfficePdfConverter.IsAvailableFor(extension))
            {
                MicrosoftOfficePdfConverter.Convert(inputPath, outputPath, cancellationToken);
                return;
            }

            throw new FileNotFoundException(
                "LibreOffice was not found and Microsoft Office desktop conversion is not available for this format. Install LibreOffice, add soffice.exe to PATH, or install Microsoft Office for Word, Excel, and PowerPoint fallback conversions. Formats: doc, docx, rtf, odt, xls, xlsx, ods, ppt, pptx, odp, and HTML when Edge is unavailable.");
        }

        var tempFolder = Path.Combine(Path.GetTempPath(), "PdfRightClickSuite", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempFolder);
        try
        {
            var result = await RunProcessAsync(
                libreOffice,
                ["--headless", "--convert-to", "pdf", "--outdir", tempFolder, inputPath],
                TimeSpan.FromMinutes(3),
                cancellationToken).ConfigureAwait(false);

            var converted = Path.Combine(tempFolder, $"{Path.GetFileNameWithoutExtension(inputPath)}.pdf");
            if (result.ExitCode != 0 || !File.Exists(converted))
            {
                throw new PdfProcessingException($"LibreOffice conversion failed for '{inputPath}'. {result.StandardError} {result.StandardOutput}".Trim());
            }

            if (File.Exists(outputPath))
            {
                throw new IOException($"Output already exists: {outputPath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
            File.Move(converted, outputPath);
        }
        finally
        {
            TryDeleteDirectory(tempFolder);
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var result = await ExternalProcessRunner.RunAsync(fileName, arguments, timeout, cancellationToken).ConfigureAwait(false);
        return new ProcessResult(result.ExitCode, result.StandardOutput, result.StandardError);
    }

    private static IReadOnlyList<string> WrapLine(string line, int maxChars)
    {
        if (line.Length <= maxChars)
        {
            return [line];
        }

        var parts = new List<string>();
        for (var i = 0; i < line.Length; i += maxChars)
        {
            parts.Add(line.Substring(i, Math.Min(maxChars, line.Length - i)));
        }

        return parts;
    }

    private static XRect FitInside(double sourceWidth, double sourceHeight, double pageWidth, double pageHeight, double margin)
    {
        var maxWidth = pageWidth - (margin * 2);
        var maxHeight = pageHeight - (margin * 2);
        var scale = Math.Min(maxWidth / sourceWidth, maxHeight / sourceHeight);
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        return new XRect((pageWidth - width) / 2, (pageHeight - height) / 2, width, height);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.TraceWarning($"Could not delete temporary conversion folder '{folder}': {ex.Message}");
        }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
