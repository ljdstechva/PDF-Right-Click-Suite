using PdfRightClickSuite.Core;

namespace PdfRightClickSuite.Tests;

public sealed class SelectionClassifierTests
{
    [Fact]
    public void Classify_rejects_empty_selection()
    {
        var result = new SelectionClassifier().Classify([]);

        Assert.False(result.CanMerge);
        Assert.False(result.CanSplit);
        Assert.False(result.CanConvert);
        Assert.False(result.CanScan);
        Assert.Contains("No files", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_allows_single_pdf_for_split_and_scan_only()
    {
        using var temp = new TemporaryDirectory();
        var pdf = temp.CreateFile("report.pdf");

        var result = new SelectionClassifier().Classify([pdf]);

        Assert.False(result.CanMerge);
        Assert.True(result.CanSplit);
        Assert.False(result.CanConvert);
        Assert.True(result.CanScan);
        Assert.Equal(".pdf", result.Files.Single().Extension);
    }

    [Fact]
    public void Classify_allows_two_pdfs_for_merge_only()
    {
        using var temp = new TemporaryDirectory();
        var first = temp.CreateFile("a.pdf");
        var second = temp.CreateFile("b.PDF");

        var result = new SelectionClassifier().Classify([first, second]);

        Assert.True(result.CanMerge);
        Assert.False(result.CanSplit);
        Assert.False(result.CanConvert);
        Assert.False(result.CanScan);
    }

    [Fact]
    public void Classify_allows_single_non_pdf_for_convert()
    {
        using var temp = new TemporaryDirectory();
        var text = temp.CreateFile("notes.txt");

        var result = new SelectionClassifier().Classify([text]);

        Assert.False(result.CanMerge);
        Assert.False(result.CanSplit);
        Assert.True(result.CanConvert);
        Assert.False(result.CanScan);
    }

    [Fact]
    public void Classify_allows_multiple_same_extension_non_pdfs_for_convert()
    {
        using var temp = new TemporaryDirectory();
        var first = temp.CreateFile("a.txt");
        var second = temp.CreateFile("b.TXT");

        var result = new SelectionClassifier().Classify([first, second]);

        Assert.True(result.CanConvert);
    }

    [Fact]
    public void Classify_allows_multiple_image_family_files_for_convert()
    {
        using var temp = new TemporaryDirectory();
        var png = temp.CreateFile("a.png");
        var jpg = temp.CreateFile("b.jpg");
        var webp = temp.CreateFile("c.webp");

        var result = new SelectionClassifier().Classify([png, jpg, webp]);

        Assert.True(result.CanConvert);
    }

    [Fact]
    public void Classify_rejects_mixed_pdf_and_non_pdf()
    {
        using var temp = new TemporaryDirectory();
        var pdf = temp.CreateFile("a.pdf");
        var txt = temp.CreateFile("b.txt");

        var result = new SelectionClassifier().Classify([pdf, txt]);

        Assert.False(result.CanMerge);
        Assert.False(result.CanConvert);
        Assert.Contains("PDF", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_rejects_mixed_unrelated_non_pdf_extensions()
    {
        using var temp = new TemporaryDirectory();
        var text = temp.CreateFile("a.txt");
        var html = temp.CreateFile("b.html");

        var result = new SelectionClassifier().Classify([text, html]);

        Assert.False(result.CanConvert);
        Assert.Contains("similar", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Classify_rejects_directories()
    {
        using var temp = new TemporaryDirectory();
        var dir = Directory.CreateDirectory(Path.Combine(temp.Path, "folder")).FullName;

        var result = new SelectionClassifier().Classify([dir]);

        Assert.False(result.CanMerge);
        Assert.False(result.CanSplit);
        Assert.False(result.CanConvert);
        Assert.False(result.CanScan);
        Assert.Contains("directory", result.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
