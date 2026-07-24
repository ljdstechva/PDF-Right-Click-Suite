using PdfRightClickSuite.Core;

namespace PdfRightClickSuite.Tests;

public sealed class OutputNameServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 1, 5, 10, 11, TimeSpan.Zero);

    [Fact]
    public void GetMergeOutputPath_uses_timestamp_when_no_collision_exists()
    {
        using var temp = new TemporaryDirectory();
        var service = new OutputNameService(new FixedClock(FixedNow));

        var path = service.GetMergeOutputPath(temp.Path);

        Assert.Equal(Path.Combine(temp.Path, "Merged_20260701_051011.pdf"), path);
    }

    [Fact]
    public void GetMergeOutputPath_appends_collision_suffix()
    {
        using var temp = new TemporaryDirectory();
        temp.CreateFile("Merged_20260701_051011.pdf");
        var service = new OutputNameService(new FixedClock(FixedNow));

        var path = service.GetMergeOutputPath(temp.Path);

        Assert.Equal(Path.Combine(temp.Path, "Merged_20260701_051011 (1).pdf"), path);
    }

    [Fact]
    public void GetScanOutputPath_increments_multiple_collision_suffixes()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.CreateFile("Résumé Final.pdf");
        temp.CreateFile("Résumé Final_scanned.pdf");
        temp.CreateFile("Résumé Final_scanned (1).pdf");
        var service = new OutputNameService(new FixedClock(FixedNow));

        var path = service.GetScanOutputPath(source);

        Assert.Equal(Path.Combine(temp.Path, "Résumé Final_scanned (2).pdf"), path);
    }

    [Fact]
    public void GetColoredScanOutputPath_uses_colored_suffix_and_collision_suffixes()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.CreateFile("Resume Final.pdf");
        temp.CreateFile("Resume Final_scanned_colored.pdf");
        var service = new OutputNameService(new FixedClock(FixedNow));

        var path = service.GetColoredScanOutputPath(source);

        Assert.Equal(Path.Combine(temp.Path, "Resume Final_scanned_colored (1).pdf"), path);
    }

    [Fact]
    public void GetSplitOutputFolder_handles_spaces_and_folder_collisions()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.CreateFile("annual report.pdf");
        Directory.CreateDirectory(Path.Combine(temp.Path, "annual report_split"));
        var service = new OutputNameService(new FixedClock(FixedNow));

        var folder = service.GetSplitOutputFolder(source);

        Assert.Equal(Path.Combine(temp.Path, "annual report_split (1)"), folder);
    }

    [Fact]
    public void GetConvertSingleOutputPath_keeps_original_stem_and_uses_pdf_extension()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.CreateFile("notes final.txt");
        var service = new OutputNameService(new FixedClock(FixedNow));

        var path = service.GetConvertSingleOutputPath(source);

        Assert.Equal(Path.Combine(temp.Path, "notes final.pdf"), path);
    }

    [Fact]
    public void GetConvertMergedOutputPath_uses_first_file_folder()
    {
        using var temp = new TemporaryDirectory();
        var service = new OutputNameService(new FixedClock(FixedNow));

        var path = service.GetConvertMergedOutputPath(temp.Path);

        Assert.Equal(Path.Combine(temp.Path, "Converted_Merged_20260701_051011.pdf"), path);
    }

    [Theory]
    [InlineData(".docx")]
    [InlineData("xlsx")]
    [InlineData(".pptx")]
    public void GetPdfToOfficeOutputPath_uses_source_stem_and_collision_suffix(string extension)
    {
        using var temp = new TemporaryDirectory();
        var source = temp.CreateFile("annual report.pdf");
        var normalizedExtension = extension.StartsWith('.') ? extension : $".{extension}";
        temp.CreateFile($"annual report{normalizedExtension}");
        var service = new OutputNameService(new FixedClock(FixedNow));

        var path = service.GetPdfToOfficeOutputPath(source, extension);

        Assert.Equal(Path.Combine(temp.Path, $"annual report (1){normalizedExtension}"), path);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now => now;
    }
}
