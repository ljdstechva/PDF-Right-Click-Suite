using PDFtoImage;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace PdfRightClickSuite.Core;

public sealed class PdfScanEffectService(PdfPageCountService? pageCountService = null)
{
    private readonly PdfPageCountService _pageCountService = pageCountService ?? new PdfPageCountService();

    public Task CreateScannedLikePdfAsync(
        string sourcePdf,
        string outputPath,
        int dpi = 0,
        ScanStrength strength = ScanStrength.LowQuality,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default,
        int? jpegQuality = null,
        float? blurRadius = null,
        float? maxRotationDegrees = null,
        ScanColorMode colorMode = ScanColorMode.BlackAndWhite)
        => CreateScannedLikePdfAsync(
            sourcePdf,
            outputPath,
            ScanEffectSettings.ForPreset(
                strength,
                dpi <= 0 ? null : dpi,
                jpegQuality,
                blurRadius,
                maxRotationDegrees),
            progress,
            cancellationToken,
            colorMode);

    public Task CreateScannedLikePdfAsync(
        string sourcePdf,
        string outputPath,
        ScanEffectSettings settings,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default,
        ScanColorMode colorMode = ScanColorMode.BlackAndWhite)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("PdfRightClickSuite scan rendering is supported on Windows.");
        }

        if (!File.Exists(sourcePdf))
        {
            throw new FileNotFoundException("Source PDF does not exist.", sourcePdf);
        }

        var pageCount = _pageCountService.GetPageCount(sourcePdf);
        var tempPdf = AtomicFileWriter.CreateTempPathBeside(outputPath);
        var tempImages = new List<string>();

        try
        {
            using var sourceDocument = PdfReader.Open(sourcePdf, PdfDocumentOpenMode.Import);
            using var sourceStream = File.OpenRead(sourcePdf);
            using var outputDocument = new PdfDocument();
            var random = new Random(settings.Seed);
            var renderOptions = new RenderOptions
            {
                Dpi = settings.Dpi,
                Grayscale = colorMode == ScanColorMode.BlackAndWhite,
                WithAnnotations = true,
                WithFormFill = true,
                UseTiling = true
            };

            for (var i = 0; i < pageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sourceStream.Position = 0;
                using var rendered = Conversion.ToImage(sourceStream, Index.FromStart(i), leaveOpen: true, password: null, options: renderOptions);
                using var effected = ApplyScannedEffect(rendered, random, settings, colorMode);
                var imagePath = Path.Combine(Path.GetTempPath(), "PdfRightClickSuite", $"{Guid.NewGuid():N}.jpg");
                Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
                SaveJpeg(effected, imagePath, settings.JpegQuality);
                tempImages.Add(imagePath);

                var sourcePage = sourceDocument.Pages[i];
                var page = outputDocument.AddPage();
                page.Width = sourcePage.Width;
                page.Height = sourcePage.Height;

                using var image = XImage.FromFile(imagePath);
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
                progress?.Report(i + 1);
            }

            cancellationToken.ThrowIfCancellationRequested();
            outputDocument.Save(tempPdf);
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFileWriter.MoveIntoPlace(tempPdf, outputPath);
            return Task.CompletedTask;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PdfProcessingException("Scan effect failed. The PDF may be encrypted, corrupt, locked, or unsupported by PDFium.", ex);
        }
        finally
        {
            AtomicFileWriter.TryDelete(tempPdf);
            foreach (var image in tempImages)
            {
                AtomicFileWriter.TryDelete(image);
            }
        }
    }

    private static SKBitmap ApplyScannedEffect(SKBitmap source, Random random, ScanEffectSettings settings, ScanColorMode colorMode)
    {
        using var adjusted = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pageBrightness = random.Next(-settings.BrightnessJitter, settings.BrightnessJitter + 1);
        var bandSpacing = random.Next(24, 62);
        var bandPhase = random.NextDouble() * Math.PI * 2;
        var gradientX = (float)((random.NextDouble() - 0.5) * settings.BrightnessJitter);
        var gradientY = (float)((random.NextDouble() - 0.5) * settings.BrightnessJitter);
        var margin = Math.Max(1, Math.Min(source.Width, source.Height) * 0.045f);

        for (var y = 0; y < source.Height; y++)
        {
            var band = (float)Math.Sin(((double)y / bandSpacing * Math.PI * 2) + bandPhase) * settings.BrightnessJitter * 0.35f;
            var verticalGradient = source.Height <= 1 ? 0 : ((y / (float)(source.Height - 1)) - 0.5f) * gradientY;
            for (var x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                var horizontalGradient = source.Width <= 1 ? 0 : ((x / (float)(source.Width - 1)) - 0.5f) * gradientX;
                var artifactOffset = pageBrightness + (int)band + (int)horizontalGradient + (int)verticalGradient;
                var noise = random.Next(-settings.NoiseAmplitude, settings.NoiseAmplitude + 1);

                if (random.NextDouble() < 0.00035 * (1 + settings.NoiseAmplitude / 8.0))
                {
                    artifactOffset -= random.Next(12, 36);
                }

                var edgeDistance = Math.Min(Math.Min(x, source.Width - 1 - x), Math.Min(y, source.Height - 1 - y));
                if (edgeDistance < margin)
                {
                    artifactOffset -= (int)((1f - (edgeDistance / margin)) * settings.EdgeDarkening * 255f);
                }

                if (colorMode == ScanColorMode.BlackAndWhite)
                {
                    var gray = (int)((color.Red * 0.299f) + (color.Green * 0.587f) + (color.Blue * 0.114f));
                    gray = AdjustLuminance(gray, artifactOffset + noise, settings, preserveColor: false);
                    adjusted.SetPixel(x, y, new SKColor((byte)gray, (byte)gray, (byte)gray, 255));
                }
                else
                {
                    adjusted.SetPixel(x, y, AdjustColoredPixel(color, artifactOffset + noise, settings));
                }
            }
        }

        var degrees = CalculateCounterClockwiseRotationDegrees(random, settings.MaxRotationDegrees);
        var fitScale = CalculateRotationFitScale(source.Width, source.Height, degrees);
        var shiftX = (float)((random.NextDouble() - 0.5) * Math.Min(source.Width, source.Height) * 0.008);
        var shiftY = (float)((random.NextDouble() - 0.5) * Math.Min(source.Width, source.Height) * 0.008);
        var paperColor = new SKColor((byte)settings.PaperTone, (byte)settings.PaperTone, (byte)settings.PaperTone);

        using var surface = SKSurface.Create(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(paperColor);
        canvas.Translate((source.Width / 2f) + shiftX, (source.Height / 2f) + shiftY);
        canvas.RotateDegrees(degrees);
        canvas.Scale(fitScale);
        canvas.DrawBitmap(
            adjusted,
            -source.Width / 2f,
            -source.Height / 2f,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        canvas.Flush();
        using var image = surface.Snapshot();
        if (settings.BlurRadius <= 0)
        {
            return SKBitmap.FromImage(image);
        }

        using var blurSurface = SKSurface.Create(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var blurCanvas = blurSurface.Canvas;
        blurCanvas.Clear(paperColor);
        using var blur = SKImageFilter.CreateBlur(settings.BlurRadius, settings.BlurRadius, SKShaderTileMode.Clamp);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            ImageFilter = blur
        };
        blurCanvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
        blurCanvas.Flush();
        using var blurred = blurSurface.Snapshot();
        return SKBitmap.FromImage(blurred);
    }

    private static float CalculateCounterClockwiseRotationDegrees(Random random, float maxRotationDegrees)
    {
        if (maxRotationDegrees <= 0)
        {
            return 0;
        }

        return -maxRotationDegrees * (0.9f + ((float)random.NextDouble() * 0.1f));
    }

    private static float CalculateRotationFitScale(int width, int height, float degrees)
    {
        if (Math.Abs(degrees) < 0.001f)
        {
            return 1f;
        }

        var radians = Math.Abs(degrees) * Math.PI / 180d;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        var rotatedWidth = (width * cosine) + (height * sine);
        var rotatedHeight = (width * sine) + (height * cosine);
        return (float)Math.Min(width / rotatedWidth, height / rotatedHeight);
    }

    private static SKColor AdjustColoredPixel(SKColor color, int artifactOffset, ScanEffectSettings settings)
    {
        var gray = (int)((color.Red * 0.299f) + (color.Green * 0.587f) + (color.Blue * 0.114f));
        var cleaned = AdjustLuminance(gray, artifactOffset, settings, preserveColor: true);
        var max = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
        var min = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
        var saturation = max - min;
        if (gray >= settings.WhitePoint || saturation < 18)
        {
            return new SKColor((byte)cleaned, (byte)cleaned, (byte)cleaned, 255);
        }

        var targetLuminance = Math.Max(cleaned, (int)(gray * 0.58f));
        var scale = targetLuminance / (float)Math.Max(1, gray);
        var red = Math.Clamp((int)(color.Red * scale), 0, settings.PaperTone);
        var green = Math.Clamp((int)(color.Green * scale), 0, settings.PaperTone);
        var blue = Math.Clamp((int)(color.Blue * scale), 0, settings.PaperTone);
        return new SKColor((byte)red, (byte)green, (byte)blue, 255);
    }

    private static int AdjustLuminance(int value, int artifactOffset, ScanEffectSettings settings, bool preserveColor)
    {
        var range = Math.Max(1, settings.WhitePoint - settings.BlackPoint);
        var normalized = Math.Clamp((value - settings.BlackPoint) / (float)range, 0f, 1f);
        var cleaned = normalized * 255f;
        const float midTone = 176f;

        if (cleaned >= midTone)
        {
            cleaned += (settings.PaperTone - cleaned) * settings.BackgroundCleanup;
        }
        else
        {
            var textDarkening = preserveColor ? settings.TextDarkening * 0.55f : settings.TextDarkening;
            var darkFactor = 1f - Math.Clamp(cleaned / midTone, 0f, 1f);
            cleaned -= cleaned * textDarkening * darkFactor;
        }

        cleaned = ((cleaned - 128f) * settings.Contrast) + 128f;
        var artifactScale = cleaned > 245f ? 0.18f : cleaned > 220f ? 0.35f : cleaned > 170f ? 0.65f : 1f;
        cleaned += artifactOffset * artifactScale;
        return Math.Clamp((int)MathF.Round(cleaned), 0, settings.PaperTone);
    }

    private static void SaveJpeg(SKBitmap bitmap, string path, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

}
