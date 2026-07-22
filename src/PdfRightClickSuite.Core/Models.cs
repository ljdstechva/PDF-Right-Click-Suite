using System.Diagnostics.CodeAnalysis;

namespace PdfRightClickSuite.Core;

public enum PdfAction
{
    Merge,
    Split,
    Convert,
    Scan,
    ScanColored
}

public enum ScanStrength
{
    Light,
    LowQuality,
    Rough
}

public enum ScanColorMode
{
    BlackAndWhite,
    Colored
}

public sealed record ScanEffectSettings(
    ScanStrength Strength,
    int Dpi,
    int JpegQuality,
    float BlurRadius,
    float MaxRotationDegrees,
    int NoiseAmplitude,
    float Contrast,
    int BrightnessJitter,
    int PaperTone,
    float EdgeDarkening,
    int BlackPoint,
    int WhitePoint,
    float BackgroundCleanup,
    float TextDarkening,
    int Seed)
{
    public const int DefaultSeed = 1337;

    public static ScanEffectSettings Default { get; } = ForPreset(ScanStrength.LowQuality);

    public static ScanEffectSettings ForPreset(
        ScanStrength strength,
        int? dpi = null,
        int? jpegQuality = null,
        float? blurRadius = null,
        float? maxRotationDegrees = null,
        int seed = DefaultSeed)
    {
        var preset = strength switch
        {
            ScanStrength.Light => new ScanEffectSettings(strength, 240, 84, 0.04f, 0.18f, 1, 1.16f, 1, 255, 0.018f, 76, 198, 0.94f, 0.62f, seed),
            ScanStrength.LowQuality => new ScanEffectSettings(strength, 150, 58, 1.05f, 1.5f, 4, 1.16f, 4, 250, 0.055f, 88, 218, 0.72f, 0.78f, seed),
            ScanStrength.Rough => new ScanEffectSettings(strength, 120, 42, 1.55f, 1.5f, 8, 1.22f, 8, 246, 0.095f, 94, 226, 0.58f, 0.88f, seed),
            _ => throw new ArgumentOutOfRangeException(nameof(strength), strength, "Unknown scan strength.")
        };

        return preset with
        {
            Dpi = Math.Clamp(dpi ?? preset.Dpi, 96, 300),
            JpegQuality = Math.Clamp(jpegQuality ?? preset.JpegQuality, 25, 95),
            BlurRadius = Math.Clamp(blurRadius ?? preset.BlurRadius, 0f, 2.5f),
            MaxRotationDegrees = Math.Clamp(maxRotationDegrees ?? preset.MaxRotationDegrees, 0f, 3f)
        };
    }

    public static bool TryParseStrength(string value, out ScanStrength strength)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "light":
                strength = ScanStrength.Light;
                return true;
            case "low-quality":
            case "lowquality":
                strength = ScanStrength.LowQuality;
                return true;
            case "rough":
                strength = ScanStrength.Rough;
                return true;
            default:
                strength = default;
                return false;
        }
    }

    public static string ToDisplayName(ScanStrength strength)
        => strength switch
        {
            ScanStrength.Light => "light",
            ScanStrength.LowQuality => "low-quality",
            ScanStrength.Rough => "rough",
            _ => strength.ToString()
        };

    public string ToSummary()
        => $"strength={ToDisplayName(Strength)}, dpi={Dpi}, jpegQuality={JpegQuality}, blur={BlurRadius:0.##}, counterclockwiseRotation={MaxRotationDegrees:0.##}, cleanup={BackgroundCleanup:0.##}";
}

public sealed record PageRangeParseResult(bool IsSuccess, IReadOnlyList<int> Pages, string? Error)
{
    public static PageRangeParseResult Success(IReadOnlyList<int> pages) => new(true, pages, null);

    public static PageRangeParseResult Failure(string error) => new(false, Array.Empty<int>(), error);
}

public sealed record SelectedFileInfo(
    string FullPath,
    string FileName,
    string Folder,
    string Extension,
    long SizeBytes);

public sealed record SelectionClassification(
    bool CanMerge,
    bool CanSplit,
    bool CanConvert,
    bool CanScan,
    string Reason,
    IReadOnlyList<SelectedFileInfo> Files);

public sealed record PdfRequest(
    PdfAction Action,
    IReadOnlyList<string> SelectedFiles,
    DateTimeOffset InvokedAt,
    string? ExplorerFolder,
    string RequestId)
{
    public bool Equals(PdfRequest? other)
    {
        return other is not null
            && Action == other.Action
            && SelectedFiles.SequenceEqual(other.SelectedFiles, StringComparer.Ordinal)
            && InvokedAt.Equals(other.InvokedAt)
            && string.Equals(ExplorerFolder, other.ExplorerFolder, StringComparison.Ordinal)
            && string.Equals(RequestId, other.RequestId, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Action);
        foreach (var file in SelectedFiles)
        {
            hash.Add(file, StringComparer.Ordinal);
        }

        hash.Add(InvokedAt);
        hash.Add(ExplorerFolder, StringComparer.Ordinal);
        hash.Add(RequestId, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;
}

[SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Carries concise user-facing PDF processing context.")]
public sealed class PdfProcessingException : Exception
{
    public PdfProcessingException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
