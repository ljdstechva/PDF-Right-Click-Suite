using System.Diagnostics;
using PdfRightClickSuite.Core;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace PdfRightClickSuite.Tests;

public sealed class CliProcessTests
{
    [Fact]
    public async Task Version_command_prints_product_name_and_version()
    {
        var result = await RunCliAsync("--version");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("PdfRightClickSuite", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnose_command_writes_report_and_mentions_shell_registration()
    {
        using var temp = new TemporaryDirectory();
        var result = await RunCliAsync("--diagnose", "--diagnostics-dir", temp.Path, "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Diagnostics", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Classic top-menu", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Position=Top", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Duplicate PdfRightClickSuite handlers", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PDF menu icon", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Scan (B&W) default preset: low-quality", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Scan (Colored) action available", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Microsoft Office PDF fallback", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PDF to Word backends: Microsoft Word=", result.Output, StringComparison.Ordinal);
        Assert.Contains("LibreOffice=", result.Output, StringComparison.Ordinal);
        Assert.Contains("PDF to Excel backend: built-in text extraction (PdfPig)", result.Output, StringComparison.Ordinal);
        Assert.Contains("PDF to PowerPoint backend: built-in page rendering (PDFtoImage)", result.Output, StringComparison.Ordinal);
        Assert.Contains("PDF Gear context menu status", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Modern menu intentionally disabled", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Native launch mode", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(Directory.GetFiles(temp.Path, "diagnostics-*.txt"));
    }

    [Fact]
    public async Task Self_test_leaves_no_new_temporary_workspace()
    {
        using var temp = new TemporaryDirectory();
        var selfTestRoot = Path.Combine(Path.GetTempPath(), "PdfRightClickSuiteSelfTest");
        var before = Directory.Exists(selfTestRoot)
            ? Directory.GetDirectories(selfTestRoot).Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];

        var result = await RunCliAsync("--self-test", "--yes", "--diagnostics-dir", temp.Path);

        var after = Directory.Exists(selfTestRoot)
            ? Directory.GetDirectories(selfTestRoot).Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(before, after);
        if (before.Length == 0)
        {
            Assert.False(Directory.Exists(selfTestRoot));
        }
    }

    [Fact]
    public async Task Invalid_scan_strength_prints_friendly_parse_error()
    {
        var result = await RunCliAsync("--action", "scan", "--scan-strength", "medium", "--files", "missing.pdf");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("--scan-strength must be light, low-quality, or rough", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unhandled exception", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Install_user_dry_run_prints_script_path_without_installing()
    {
        var result = await RunCliAsync("--install-user", "--dry-run", "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("install.ps1", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dry run", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Merge_flow_starts_automatically_after_sorting()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "PdfRightClickSuite.Cli", "Program.cs"));
        var mergeMethodStart = source.IndexOf("private static async Task<string> RunMergeAsync", StringComparison.Ordinal);
        var splitMethodStart = source.IndexOf("private static async Task<string> RunSplitAsync", StringComparison.Ordinal);
        Assert.True(mergeMethodStart >= 0, "RunMergeAsync was not found.");
        Assert.True(splitMethodStart > mergeMethodStart, "RunSplitAsync was not found after RunMergeAsync.");

        var mergeMethod = source[mergeMethodStart..splitMethodStart];

        Assert.Contains("SortingUi.Sort", mergeMethod, StringComparison.Ordinal);
        Assert.Contains("ShowAutomaticWrite(\"Merge\"", mergeMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmWrite(\"Merge\"", mergeMethod, StringComparison.Ordinal);

        var openOutputPromptStart = source.IndexOf("private static bool ShouldPromptToOpenOutput", StringComparison.Ordinal);
        var openOutputLocationStart = source.IndexOf("private static void OpenOutputLocation", StringComparison.Ordinal);
        Assert.True(openOutputPromptStart >= 0, "ShouldPromptToOpenOutput was not found.");
        Assert.True(openOutputLocationStart > openOutputPromptStart, "OpenOutputLocation was not found after ShouldPromptToOpenOutput.");

        var openOutputPromptMethod = source[openOutputPromptStart..openOutputLocationStart];

        Assert.DoesNotContain("PdfAction.Split", openOutputPromptMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("PdfAction.Merge", openOutputPromptMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void Split_flow_starts_automatically_after_page_selection()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "PdfRightClickSuite.Cli", "Program.cs"));
        var splitMethodStart = source.IndexOf("private static async Task<string> RunSplitAsync", StringComparison.Ordinal);
        var convertMethodStart = source.IndexOf("private static async Task<string> RunConvertAsync", StringComparison.Ordinal);
        Assert.True(splitMethodStart >= 0, "RunSplitAsync was not found.");
        Assert.True(convertMethodStart > splitMethodStart, "RunConvertAsync was not found after RunSplitAsync.");

        var splitMethod = source[splitMethodStart..convertMethodStart];

        Assert.Contains("Split mode", splitMethod, StringComparison.Ordinal);
        Assert.Contains("All pages", splitMethod, StringComparison.Ordinal);
        Assert.Contains("PromptForPages(pageCount)", splitMethod, StringComparison.Ordinal);
        Assert.Contains("ShowAutomaticWrite(\"Split\"", splitMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmWrite(\"Split\"", splitMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_wires_ctrl_c_to_all_long_running_workflows()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "PdfRightClickSuite.Cli", "Program.cs"));
        var mainStart = source.IndexOf("public static async Task<int> Main", StringComparison.Ordinal);
        var loadRequestStart = source.IndexOf("private static PdfRequest LoadRequest", StringComparison.Ordinal);
        Assert.True(mainStart >= 0, "Main was not found.");
        Assert.True(loadRequestStart > mainStart, "LoadRequest was not found after Main.");

        var main = source[mainStart..loadRequestStart];
        Assert.Contains("Console.CancelKeyPress += cancelHandler", main, StringComparison.Ordinal);
        Assert.Contains("Console.CancelKeyPress -= cancelHandler", main, StringComparison.Ordinal);
        Assert.Contains("cancellationSource.Token", main, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationToken.None", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_requests_are_consumed_once_and_stale_requests_are_cleaned()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "PdfRightClickSuite.Cli", "Program.cs"));
        var loadRequestStart = source.IndexOf("private static PdfRequest LoadRequest", StringComparison.Ordinal);
        var validationStart = source.IndexOf("private static void ValidateAction", StringComparison.Ordinal);
        Assert.True(loadRequestStart >= 0, "LoadRequest was not found.");
        Assert.True(validationStart > loadRequestStart, "ValidateAction was not found after LoadRequest.");

        var loadRequest = source[loadRequestStart..validationStart];
        Assert.Contains("service.IsShellRequestPath", loadRequest, StringComparison.Ordinal);
        Assert.Contains("service.ConsumeShellRequest", loadRequest, StringComparison.Ordinal);
        Assert.Contains("service.CleanupStaleShellRequests", loadRequest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Convert_jpg_request_runs_without_confirmation()
    {
        using var temp = new TemporaryDirectory();
        var jpg = Path.Combine(temp.Path, "sample image.jpg");
        CreateSampleImage(jpg, SkiaSharp.SKEncodedImageFormat.Jpeg);
        var request = WriteRequest(temp.Path, PdfAction.Convert, [jpg]);

        var result = await RunCliAsync("--request", request);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Write output now", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Automatic", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(temp.Path, "sample image.pdf")));
    }

    [Fact]
    public async Task Convert_png_request_runs_without_confirmation()
    {
        using var temp = new TemporaryDirectory();
        var png = Path.Combine(temp.Path, "sample image.png");
        CreateSampleImage(png, SkiaSharp.SKEncodedImageFormat.Png);
        var request = WriteRequest(temp.Path, PdfAction.Convert, [png]);

        var result = await RunCliAsync("--request", request);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Write output now", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(temp.Path, "sample image.pdf")));
    }

    [Fact]
    public async Task Convert_txt_request_runs_without_confirmation()
    {
        using var temp = new TemporaryDirectory();
        var txt = temp.CreateFile("notes.txt", "hello from text conversion");
        var request = WriteRequest(temp.Path, PdfAction.Convert, [txt]);

        var result = await RunCliAsync("--request", request);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Write output now", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(temp.Path, "notes.pdf")));
    }

    [Fact]
    public async Task Convert_collision_uses_unique_output_name_without_prompting()
    {
        using var temp = new TemporaryDirectory();
        var txt = temp.CreateFile("notes.txt", "hello from text conversion");
        File.WriteAllText(Path.Combine(temp.Path, "notes.pdf"), "existing output");
        var request = WriteRequest(temp.Path, PdfAction.Convert, [txt]);

        var result = await RunCliAsync("--request", request);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Write output now", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(temp.Path, "notes (1).pdf")));
    }

    [Fact]
    public async Task Convert_confirm_flag_keeps_opt_in_confirmation()
    {
        using var temp = new TemporaryDirectory();
        var txt = temp.CreateFile("notes.txt", "hello from text conversion");
        var request = WriteRequest(temp.Path, PdfAction.Convert, [txt]);

        var result = await RunCliAsync("--request", request, "--confirm-convert", "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Confirm", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(temp.Path, "notes.pdf")));
    }

    [Fact]
    public async Task Scan_colored_request_runs_without_confirmation_and_writes_colored_suffix()
    {
        using var temp = new TemporaryDirectory();
        var pdf = CreateSamplePdf(temp.Path, "scan source.pdf");
        var request = WriteRequest(temp.Path, PdfAction.ScanColored, [pdf]);

        var result = await RunCliAsync("--request", request);

        Assert.Equal(0, result.ExitCode);
        Assert.DoesNotContain("Write output now", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Scan (Colored)", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(temp.Path, "scan source_scanned_colored.pdf")));
    }

    [Fact]
    public async Task Convert_to_excel_cli_alias_writes_xlsx_beside_source()
    {
        using var temp = new TemporaryDirectory();
        var pdf = CreateSamplePdf(temp.Path, "table source.pdf");

        var result = await RunCliAsync("--action", "convert-to-excel", "--files", pdf, "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Converted with: PdfPig + Open XML SDK", result.Output, StringComparison.Ordinal);
        Assert.Contains("PdfPig + Open XML SDK", result.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(temp.Path, "table source.xlsx")));
    }

    [Fact]
    public async Task Convert_to_powerpoint_cli_alias_writes_pptx_beside_source()
    {
        using var temp = new TemporaryDirectory();
        var pdf = CreateSamplePdf(temp.Path, "slide source.pdf");

        var result = await RunCliAsync("--action", "convert-pptx", "--files", pdf, "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Converted with: PDFtoImage + Open XML SDK", result.Output, StringComparison.Ordinal);
        Assert.Contains("PDFtoImage + Open XML SDK", result.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(temp.Path, "slide source.pptx")));
    }

    [Fact]
    public async Task Convert_to_word_shell_request_drives_same_cli_flow_when_word_is_available()
    {
        if (!OperatingSystem.IsWindows() || !MicrosoftOfficePdfConverter.IsPdfToDocxAvailable())
        {
            return;
        }

        using var temp = new TemporaryDirectory();
        var pdf = CreateSamplePdf(temp.Path, "word source.pdf");
        var request = WriteRequest(temp.Path, PdfAction.ConvertToWord, [pdf]);

        var result = await RunCliAsync("--request", request, "--yes");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Microsoft Word PDF import", result.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(temp.Path, "word source.docx")));
    }

    [Fact]
    public void Pdf_to_office_aliases_map_to_the_appended_actions()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "PdfRightClickSuite.Cli", "Program.cs"));
        foreach (var alias in new[]
                 {
                     "convert-to-word", "convert-docx", "converttoword",
                     "convert-to-excel", "convert-xlsx", "converttoexcel",
                     "convert-to-powerpoint", "convert-pptx", "converttopowerpoint"
                 })
        {
            Assert.Contains($"case \"{alias}\"", source, StringComparison.Ordinal);
        }

        Assert.Contains("action = PdfAction.ConvertToWord", source, StringComparison.Ordinal);
        Assert.Contains("action = PdfAction.ConvertToExcel", source, StringComparison.Ordinal);
        Assert.Contains("action = PdfAction.ConvertToPowerPoint", source, StringComparison.Ordinal);
    }

    private static async Task<CliRunResult> RunCliAsync(params string[] args)
    {
        var root = FindRepoRoot();
        var dotnet = FindDotNet();
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = dotnet,
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        process.StartInfo.ArgumentList.Add("run");
        process.StartInfo.ArgumentList.Add("--project");
        process.StartInfo.ArgumentList.Add(Path.Combine(root, "src", "PdfRightClickSuite.Cli", "PdfRightClickSuite.Cli.csproj"));
        process.StartInfo.ArgumentList.Add("--");
        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

            throw new TimeoutException($"CLI command timed out: {string.Join(" ", args)}");
        }

        return new CliRunResult(process.ExitCode, output + error);
    }

    private static string WriteRequest(string folder, PdfAction action, IReadOnlyList<string> files)
    {
        var path = Path.Combine(folder, $"request-{action}-{Guid.NewGuid():N}.json");
        new RequestFileService().Write(path, new PdfRequest(action, files, DateTimeOffset.Now, folder, Guid.NewGuid().ToString("N")));
        return path;
    }

    private static void CreateSampleImage(string path, SkiaSharp.SKEncodedImageFormat format)
    {
        using var bitmap = new SkiaSharp.SKBitmap(160, 100);
        using var canvas = new SkiaSharp.SKCanvas(bitmap);
        canvas.Clear(SkiaSharp.SKColors.White);
        using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.DarkBlue, IsAntialias = true };
        canvas.DrawRect(new SkiaSharp.SKRect(20, 20, 140, 80), paint);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private static string CreateSamplePdf(string folder, string fileName)
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        var path = Path.Combine(folder, fileName);
        using var document = new PdfDocument();
        var page = document.AddPage();
        using var gfx = XGraphics.FromPdfPage(page);
        var font = new XFont("Arial", 16);
        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        gfx.DrawRectangle(XBrushes.Red, 80, 120, 180, 120);
        gfx.DrawString("colored scan cli test", font, XBrushes.Blue, new XRect(0, 0, page.Width.Point, page.Height.Point), XStringFormats.Center);
        document.Save(path);
        return path;
    }

    private static string FindDotNet()
    {
        var userDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet.exe");
        if (File.Exists(userDotnet))
        {
            return userDotnet;
        }

        return "dotnet";
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

    private sealed record CliRunResult(int ExitCode, string Output);
}
