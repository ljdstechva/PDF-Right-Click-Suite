using System.Diagnostics;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;
using PdfRightClickSuite.Core;
using Spectre.Console;

namespace PdfRightClickSuite.Cli;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const string ClassicTopMenuClsid = "{065E1050-7F50-4FDF-94C6-19B998E64A83}";
    private const string ClassicFallbackClsid = "{68A2F5F6-2E91-4C66-B126-896B8C6C6834}";
    private static readonly LoggerService Logger = new();

    public static async Task<int> Main(string[] args)
    {
        var started = Stopwatch.StartNew();
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (Exception ex)
        {
            AnsiConsole.Write(new Panel(new Markup($"[bold red]Error[/]\n{Markup.Escape(FriendlyMessage(ex))}"))
                .Header("PdfRightClickSuite")
                .BorderColor(Color.Red));
            return 1;
        }

        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellationSource.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            if (options.ShowHelp)
            {
                ShowUsage();
                return 0;
            }

            if (options.ShowWelcome)
            {
                ShowWelcome(options);
                return 0;
            }

            if (options.ShowVersion)
            {
                AnsiConsole.MarkupLine($"[bold]PdfRightClickSuite[/] {GetVersion()}");
                return 0;
            }

            if (options.Diagnose)
            {
                var reportPath = RunDiagnostics(options);
                AnsiConsole.MarkupLine($"[green]Diagnostics report written:[/] {Markup.Escape(reportPath)}");
                return 0;
            }

            if (options.SelfTest)
            {
                var reportPath = await RunSelfTestAsync(options, cancellationSource.Token).ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[green]Self-test report written:[/] {Markup.Escape(reportPath)}");
                return 0;
            }

            if (options.InstallUser)
            {
                return await RunUserScriptAsync("install.ps1", "Install current user integration", options, cancellationSource.Token).ConfigureAwait(false);
            }

            if (options.UninstallUser)
            {
                return await RunUserScriptAsync("uninstall.ps1", "Uninstall current user integration", options, cancellationSource.Token).ConfigureAwait(false);
            }

            var request = LoadRequest(options);
            var files = request.SelectedFiles.Select(Path.GetFullPath).ToArray();
            var classifier = new SelectionClassifier();
            var classification = classifier.Classify(files);
            ValidateAction(request.Action, classification);

            ShowHeader(request.Action, classification);

            var output = request.Action switch
            {
                PdfAction.Merge => await RunMergeAsync(files, options, cancellationSource.Token).ConfigureAwait(false),
                PdfAction.Split => await RunSplitAsync(files.Single(), options, cancellationSource.Token).ConfigureAwait(false),
                PdfAction.Convert => await RunConvertAsync(files, options, cancellationSource.Token).ConfigureAwait(false),
                PdfAction.Scan => await RunScanAsync(files.Single(), options, cancellationSource.Token, ScanColorMode.BlackAndWhite).ConfigureAwait(false),
                PdfAction.ScanColored => await RunScanAsync(files.Single(), options, cancellationSource.Token, ScanColorMode.Colored).ConfigureAwait(false),
                PdfAction.ConvertToWord => await RunPdfToOfficeAsync(files.Single(), OfficeExportFormat.Word, cancellationSource.Token).ConfigureAwait(false),
                PdfAction.ConvertToExcel => await RunPdfToOfficeAsync(files.Single(), OfficeExportFormat.Excel, cancellationSource.Token).ConfigureAwait(false),
                PdfAction.ConvertToPowerPoint => await RunPdfToOfficeAsync(files.Single(), OfficeExportFormat.PowerPoint, cancellationSource.Token).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported action {request.Action}.")
            };

            started.Stop();
            AnsiConsole.Write(new Panel(new Markup($"[bold green]Done[/]\nOutput: [cyan]{Markup.Escape(output)}[/]\nElapsed: {started.Elapsed:g}"))
                .Header("Success")
                .BorderColor(Color.Green));

            if (!options.Yes && !Console.IsInputRedirected && ShouldPromptToOpenOutput(request.Action) && AnsiConsole.Confirm("Open the output folder?", defaultValue: true))
            {
                OpenOutputLocation(output);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return 2;
        }
        catch (Exception ex)
        {
            var logged = Logger.Error(ex, "CLI operation failed.");
            AnsiConsole.Write(new Panel(new Markup($"[bold red]Error[/]\n{Markup.Escape(FriendlyMessage(ex, logged))}"))
                .Header("PdfRightClickSuite")
                .BorderColor(Color.Red));
            if (options.Debug)
            {
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            }

            WaitForKey("Press any key to close.");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static PdfRequest LoadRequest(CliOptions options)
    {
        if (options.RequestPath is not null)
        {
            var service = new RequestFileService();
            try
            {
                return service.IsShellRequestPath(options.RequestPath)
                    ? service.ConsumeShellRequest(options.RequestPath)
                    : service.Read(options.RequestPath);
            }
            finally
            {
                service.CleanupStaleShellRequests(TimeSpan.FromHours(1));
            }
        }

        if (options.Action is null || options.Files.Count == 0)
        {
            throw new ArgumentException("Use --request <json> or --action merge|split|convert|scan|scan-colored|convert-to-word|convert-to-excel|convert-to-powerpoint --files <file1> <file2> ...");
        }

        return new PdfRequest(
            options.Action.Value,
            options.Files,
            DateTimeOffset.Now,
            options.Files.Count > 0 ? Path.GetDirectoryName(Path.GetFullPath(options.Files[0])) : null,
            Guid.NewGuid().ToString("N"));
    }

    private static void ValidateAction(PdfAction action, SelectionClassification classification)
    {
        var allowed = action switch
        {
            PdfAction.Merge => classification.CanMerge,
            PdfAction.Split => classification.CanSplit,
            PdfAction.Convert => classification.CanConvert,
            PdfAction.Scan => classification.CanScan,
            PdfAction.ScanColored => classification.CanScan,
            PdfAction.ConvertToWord => classification.CanConvertToOffice,
            PdfAction.ConvertToExcel => classification.CanConvertToOffice,
            PdfAction.ConvertToPowerPoint => classification.CanConvertToOffice,
            _ => false
        };

        if (!allowed)
        {
            throw new InvalidOperationException($"The selected files are not valid for {ActionDisplayName(action)}. {classification.Reason}");
        }
    }

    private static async Task<string> RunMergeAsync(IReadOnlyList<string> files, CliOptions options, CancellationToken cancellationToken)
    {
        var sorted = SortingUi.Sort(files, new PdfPageCountService(), cancellationToken);
        var outputFolder = FirstFileFolder(sorted);
        ShowCrossFolderNote(sorted, outputFolder);
        var outputPath = new OutputNameService().GetMergeOutputPath(outputFolder);
        ShowAutomaticWrite("Merge", sorted, outputPath);

        await AnsiConsole.Progress()
            .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new ElapsedTimeColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Merging PDFs", maxValue: sorted.Count);
                var progress = new Progress<int>(value => task.Value = value);
                await new PdfMergeService(new PdfPageCountService())
                    .MergeAsync(sorted, outputPath, cancellationToken, progress)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);

        return outputPath;
    }

    private static async Task<string> RunSplitAsync(string sourcePdf, CliOptions options, CancellationToken cancellationToken)
    {
        var pageCountService = new PdfPageCountService();
        var pageCount = pageCountService.GetPageCount(sourcePdf);
        AnsiConsole.MarkupLine($"[bold]Total pages:[/] {pageCount}");

        var mode = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Split mode")
                .AddChoices("All pages", "Selected pages only"));

        IReadOnlyList<int> pages;
        if (mode == "All pages")
        {
            pages = Enumerable.Range(1, pageCount).ToArray();
        }
        else
        {
            pages = PromptForPages(pageCount);
        }

        AnsiConsole.Write(new Panel(Markup.Escape(string.Join(", ", pages))).Header("Pages to write"));
        var outputFolder = new OutputNameService().GetSplitOutputFolder(sourcePdf);
        ShowAutomaticWrite("Split", [sourcePdf], outputFolder);

        await AnsiConsole.Progress()
            .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new ElapsedTimeColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Writing pages", maxValue: pages.Count);
                var progress = new Progress<int>(value => task.Value = value);
                await new PdfSplitService(pageCountService)
                    .SplitAsync(sourcePdf, pages, outputFolder, cancellationToken, progress)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);

        return outputFolder;
    }

    private static async Task<string> RunConvertAsync(IReadOnlyList<string> files, CliOptions options, CancellationToken cancellationToken)
    {
        var outputService = new OutputNameService();
        var convertService = new PdfConvertService(new PdfMergeService(new PdfPageCountService()), new ExternalToolLocator());

        if (files.Count == 1)
        {
            var outputPath = outputService.GetConvertSingleOutputPath(files[0]);
            if (options.ConfirmConvert)
            {
                ConfirmWrite("Convert", files, outputPath, options.Yes);
            }
            else
            {
                ShowAutomaticWrite("Convert", files, outputPath);
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Converting file", async _ => await convertService.ConvertSingleAsync(files[0], outputPath, cancellationToken).ConfigureAwait(false))
                .ConfigureAwait(false);
            return outputPath;
        }

        var sorted = SortingUi.Sort(files, new PdfPageCountService(), cancellationToken);
        var outputFolder = FirstFileFolder(sorted);
        ShowCrossFolderNote(sorted, outputFolder);
        var mergedOutput = outputService.GetConvertMergedOutputPath(outputFolder);
        if (options.ConfirmConvert)
        {
            ConfirmWrite("Convert and merge", sorted, mergedOutput, options.Yes);
        }
        else
        {
            ShowAutomaticWrite("Convert and merge", sorted, mergedOutput);
        }

        await AnsiConsole.Progress()
            .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new ElapsedTimeColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Converting files", maxValue: sorted.Count);
                var progress = new Progress<int>(value => task.Value = value);
                await convertService.ConvertManyAndMergeAsync(sorted, mergedOutput, cancellationToken, progress).ConfigureAwait(false);
            }).ConfigureAwait(false);

        return mergedOutput;
    }

    private static async Task<string> RunScanAsync(string sourcePdf, CliOptions options, CancellationToken cancellationToken, ScanColorMode colorMode)
    {
        var outputNameService = new OutputNameService();
        var outputPath = colorMode == ScanColorMode.Colored
            ? outputNameService.GetColoredScanOutputPath(sourcePdf)
            : outputNameService.GetScanOutputPath(sourcePdf);
        var operation = colorMode == ScanColorMode.Colored ? "Scan (Colored)" : "Scan (B&W)";
        ShowAutomaticWrite(operation, [sourcePdf], outputPath);
        var scanSettings = options.ScanSettings;
        AnsiConsole.MarkupLine($"[dim]Scan settings: {Markup.Escape(scanSettings.ToSummary())}, color={Markup.Escape(ScanColorModeDisplayName(colorMode))}[/]");
        var pageCount = new PdfPageCountService().GetPageCount(sourcePdf);

        await AnsiConsole.Progress()
            .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new ElapsedTimeColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask($"Rendering scanned-look PDF ({ScanColorModeDisplayName(colorMode)})", maxValue: pageCount);
                var progress = new Progress<int>(value => task.Value = value);
                await new PdfScanEffectService()
                    .CreateScannedLikePdfAsync(sourcePdf, outputPath, scanSettings, progress, cancellationToken, colorMode)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);

        return outputPath;
    }

    private static async Task<string> RunPdfToOfficeAsync(
        string sourcePdf,
        OfficeExportFormat format,
        CancellationToken cancellationToken)
    {
        var extension = format switch
        {
            OfficeExportFormat.Word => ".docx",
            OfficeExportFormat.Excel => ".xlsx",
            OfficeExportFormat.PowerPoint => ".pptx",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown Office export format.")
        };
        var outputPath = new OutputNameService().GetPdfToOfficeOutputPath(sourcePdf, extension);
        ShowAutomaticWrite($"Convert PDF to {format}", [sourcePdf], outputPath);
        var service = new PdfToOfficeConvertService(new ExternalToolLocator());
        PdfToOfficeResult? result = null;

        if (format == OfficeExportFormat.Word)
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync(
                    "Converting PDF to Word",
                    async _ => result = await service
                        .ConvertAsync(sourcePdf, outputPath, format, cancellationToken)
                        .ConfigureAwait(false))
                .ConfigureAwait(false);
        }
        else
        {
            var pageCount = new PdfPageCountService().GetPageCount(sourcePdf);
            await AnsiConsole.Progress()
                .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new ElapsedTimeColumn())
                .StartAsync(async context =>
                {
                    var task = context.AddTask($"Converting PDF to {format}", maxValue: pageCount);
                    var progress = new Progress<int>(value => task.Value = value);
                    result = await service
                        .ConvertAsync(sourcePdf, outputPath, format, cancellationToken, progress)
                        .ConfigureAwait(false);
                }).ConfigureAwait(false);
        }

        if (result is null)
        {
            throw new InvalidOperationException("PDF-to-Office conversion did not return a result.");
        }

        AnsiConsole.MarkupLine($"[dim]Converted with: {Markup.Escape(result.BackendUsed)}[/]");
        AnsiConsole.MarkupLine($"[dim]Pages converted: {result.PageCount}[/]");
        var qualityNote = format switch
        {
            OfficeExportFormat.Word when result.BackendUsed.StartsWith("LibreOffice", StringComparison.Ordinal) => "LibreOffice PDF import is layout-frame based; complex layouts may need manual cleanup.",
            OfficeExportFormat.Word => "Microsoft Word PDF Reflow creates editable content; complex layouts may need manual cleanup.",
            OfficeExportFormat.Excel => "Excel uses positioned text heuristics; formulas, charts, and original formatting are not recreated.",
            OfficeExportFormat.PowerPoint => "Slides are page images (visually exact, not text-editable).",
            _ => string.Empty
        };
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(qualityNote)}[/]");
        return outputPath;
    }

    private static void ShowAutomaticWrite(string operation, IReadOnlyList<string> files, string outputPath)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("File")
            .AddColumn("Folder");

        foreach (var file in files)
        {
            table.AddRow(Markup.Escape(Path.GetFileName(file)), Markup.Escape(Path.GetDirectoryName(file) ?? string.Empty));
        }

        AnsiConsole.Write(table);
        AnsiConsole.Write(new Panel(new Markup($"Operation: [bold]{Markup.Escape(operation)}[/]\nOutput: [cyan]{Markup.Escape(outputPath)}[/]\n[dim]This action runs automatically and never overwrites existing files.[/]"))
            .Header("Automatic")
            .BorderColor(Color.Green));
    }

    private static IReadOnlyList<int> PromptForPages(int pageCount)
    {
        while (true)
        {
            var expression = AnsiConsole.Ask<string>($"Enter pages ([dim]1,3-5,8[/], total {pageCount}):");
            var result = PageRangeParser.Parse(expression, pageCount);
            if (result.IsSuccess)
            {
                return result.Pages;
            }

            AnsiConsole.MarkupLine($"[red]{Markup.Escape(result.Error ?? "Invalid page expression.")}[/]");
        }
    }

    private static void ConfirmWrite(string operation, IReadOnlyList<string> files, string outputPath, bool assumeYes)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("File")
            .AddColumn("Folder");

        foreach (var file in files)
        {
            table.AddRow(Markup.Escape(Path.GetFileName(file)), Markup.Escape(Path.GetDirectoryName(file) ?? string.Empty));
        }

        AnsiConsole.Write(table);
        AnsiConsole.Write(new Panel(new Markup($"Operation: [bold]{Markup.Escape(operation)}[/]\nOutput: [cyan]{Markup.Escape(outputPath)}[/]"))
            .Header("Confirm")
            .BorderColor(Color.Blue));

        if (!assumeYes && !AnsiConsole.Confirm("Write output now?", defaultValue: true))
        {
            throw new OperationCanceledException();
        }
    }

    private static void ShowHeader(PdfAction action, SelectionClassification classification)
    {
        AnsiConsole.Write(new Rule($"[bold cyan]PDF[/] [white]{Markup.Escape(ActionDisplayName(action))}[/]").RuleStyle("grey"));
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("#")
            .AddColumn("Name")
            .AddColumn("Folder")
            .AddColumn("Size");

        for (var i = 0; i < classification.Files.Count; i++)
        {
            var file = classification.Files[i];
            table.AddRow(
                (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Markup.Escape(file.FileName),
                Markup.Escape(file.Folder),
                FormatBytes(file.SizeBytes));
        }

        AnsiConsole.Write(table);
    }

    private static void ShowCrossFolderNote(IReadOnlyList<string> files, string outputFolder)
    {
        var folders = files
            .Select(file => Path.GetDirectoryName(Path.GetFullPath(file)) ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (folders.Length > 1)
        {
            AnsiConsole.Write(new Panel(new Markup($"Selected files come from multiple folders.\nOutput will be written to the first selected file's folder:\n[cyan]{Markup.Escape(outputFolder)}[/]"))
                .Header("Output folder")
                .BorderColor(Color.Yellow));
        }
    }

    private static string FirstFileFolder(IReadOnlyList<string> files)
        => Path.GetDirectoryName(Path.GetFullPath(files[0])) ?? Directory.GetCurrentDirectory();

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static string FriendlyMessage(Exception ex, bool diagnosticLogged = true)
        => ex switch
        {
            PdfProcessingException => ex.Message,
            FileNotFoundException => ex.Message,
            NotSupportedException => ex.Message,
            ArgumentException => ex.Message,
            InvalidOperationException => ex.Message,
            TimeoutException => ex.Message,
            UnauthorizedAccessException => "Access was denied. The file may be locked or you may not have permission to write the output.",
            IOException => $"A file system error occurred: {ex.Message}",
            _ when diagnosticLogged => "Something went wrong. Technical details were written to the log folder under LocalAppData.",
            _ => "Something went wrong, and the technical log could not be written."
        };

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool ShouldPromptToOpenOutput(PdfAction action)
        => false;

    private static string ActionDisplayName(PdfAction action)
        => action switch
        {
            PdfAction.Scan => "Scan (B&W)",
            PdfAction.ScanColored => "Scan (Colored)",
            PdfAction.ConvertToWord => "Convert PDF to Word",
            PdfAction.ConvertToExcel => "Convert PDF to Excel",
            PdfAction.ConvertToPowerPoint => "Convert PDF to PowerPoint",
            _ => action.ToString()
        };

    private static string ScanColorModeDisplayName(ScanColorMode colorMode)
        => colorMode == ScanColorMode.Colored ? "Colored" : "B&W";

    private static void OpenOutputLocation(string output)
    {
        var target = Directory.Exists(output) ? output : Path.GetDirectoryName(Path.GetFullPath(output));
        if (target is null)
        {
            return;
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = false
        };
        process.StartInfo.ArgumentList.Add(target);
        process.Start();
    }

    private static void WaitForKey(string message)
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(message)}[/]");
        Console.ReadKey(intercept: true);
    }

    private static void ShowUsage()
    {
        AnsiConsole.Write(new Panel("""
            PdfRightClickSuite.Cli

            --request <path-to-json>
            --action merge|split|convert|scan|scan-colored|convert-to-word|convert-to-excel|convert-to-powerpoint --files <file1> <file2> ...

            Options:
              --yes                 skip confirmation prompts
              --confirm-convert     ask before writing Convert output
              --debug               show exception details
              --scan-strength light|low-quality|rough
              --scan-dpi <number>          override preset DPI
              --scan-jpeg-quality <number> override preset JPEG quality
              --scan-blur <number>         override preset blur radius
              --scan-rotation <degrees>    override counterclockwise rotation
              --diagnose            write Explorer integration diagnostics
              --self-test           run built-in PDF operation smoke tests
              --install-user        install current-user Explorer integration
              --uninstall-user      uninstall current-user Explorer integration
              --dry-run             print install/uninstall command without changes

            PDF-to-Office aliases:
              convert-to-word | convert-docx | converttoword
              convert-to-excel | convert-xlsx | converttoexcel
              convert-to-powerpoint | convert-pptx | converttopowerpoint

            Press Ctrl+C to cancel a running operation.
            """).Header("Usage"));
    }

    private static void ShowWelcome(CliOptions options)
    {
        AnsiConsole.Write(new Panel(new Markup($"""
            [bold cyan]PdfRightClickSuite[/] {Markup.Escape(GetVersion())}

            This app is normally launched from File Explorer's right-click PDF menu.

            Useful commands:
              [green]--help[/]          Show command syntax
              [green]--diagnose[/]      Check Explorer integration and dependencies
              [green]--self-test[/]     Generate sample files and verify PDF operations
              [green]--action convert-to-excel --files report.pdf[/]  Convert one PDF beside the source
              [green]--install-user[/]  Install for the current Windows user
              [green]--uninstall-user[/] Uninstall from the current Windows user
              [green]--version[/]       Print version
            """))
            .Header("PDF")
            .BorderColor(Color.Cyan));

        AnsiConsole.Write(new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Status")
            .AddColumn("Value")
            .AddRow("CLI path", Markup.Escape(Environment.ProcessPath ?? "Unknown"))
            .AddRow("Classic context menu", IsClassicShellRegistered() ? "[green]Registered[/]" : "[yellow]Not registered[/]")
            .AddRow("Modern package", IsModernPackageRegistered() ? "[green]Registered[/]" : "[yellow]Not registered[/]")
            .AddRow("Logs", Markup.Escape(GetLogsDirectory())));

        if (!options.Yes && !Console.IsInputRedirected)
        {
            WaitForKey("Press any key to exit.");
        }
    }

    private static string RunDiagnostics(CliOptions options)
    {
        var reportFolder = options.DiagnosticsDir ?? Path.Combine(FindRepoRootOrNull() ?? GetLogsDirectory(), "artifacts", "diagnostics");
        if (!Path.IsPathRooted(reportFolder))
        {
            reportFolder = Path.GetFullPath(reportFolder);
        }

        Directory.CreateDirectory(reportFolder);
        var reportPath = Path.Combine(reportFolder, $"diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt");
        var installDir = ReadInstallDir();
        var cliPath = Environment.ProcessPath ?? Path.Combine(installDir ?? string.Empty, "PdfRightClickSuite.Cli.exe");
        var shellDll = installDir is null ? null : Path.Combine(installDir, "PdfRightClickSuite.ShellExtension.dll");
        var locator = new ExternalToolLocator();
        var classicTop = GetClassicTopMenuInfo();
        var duplicateHandlers = GetDuplicatePdfRightClickSuiteHandlers(classicTop);
        var pdfGearStatus = GetPdfGearContextMenuStatus();
        var packageInfo = GetModernPackageInfo();
        var certificateStatus = GetModernCertificateStatus();
        var modernLogPath = Path.Combine(GetLogsDirectory(), "modern-menu.log");
        var defaultScanSettings = ScanEffectSettings.Default;
        var libreOfficePath = locator.FindLibreOffice();
        var wordAvailability = MicrosoftOfficePdfConverter.IsPdfToDocxAvailable() ? "Available" : "Not found";

        var lines = new List<string>
        {
            "PdfRightClickSuite Diagnostics",
            $"Generated: {DateTimeOffset.Now:O}",
            $"OS version: {Environment.OSVersion.VersionString}",
            $"Windows 11 or newer: {OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)}",
            $"64-bit process: {Environment.Is64BitProcess}",
            $"64-bit OS: {Environment.Is64BitOperatingSystem}",
            $"Install folder: {installDir ?? "Not registered"}",
            $"CLI path: {cliPath}",
            $"CLI exists: {File.Exists(cliPath)}",
            "Native launch mode: classic top shell verb uses ExplorerCommandHandler to start PdfRightClickSuite.Cli.exe through request JSON",
            $"Shell DLL path: {shellDll ?? "Not registered"}",
            $"Shell DLL exists: {shellDll is not null && File.Exists(shellDll)}",
            $"Classic top-menu registered: {classicTop.IsRegistered}",
            $"Classic top-menu registry path: {classicTop.RegistryPath}",
            $"Classic top-menu handler type: {classicTop.HandlerType}",
            $"Classic top-menu MUIVerb=PDF: {classicTop.HasPdfVerb}",
            $"Classic top-menu Position=Top present: {classicTop.HasPositionTop}",
            $"Classic top-menu ExplorerCommandHandler: {classicTop.ExplorerCommandHandler ?? "Not registered"}",
            $"Classic top-menu MultiSelectModel=Player: {classicTop.HasPlayerMultiSelectModel}",
            $"PDF menu icon registry value: {classicTop.IconValue ?? "Not registered"}",
            $"PDF menu icon exists: {classicTop.IconExists}",
            $"PDF menu icon path: {classicTop.IconValue ?? "Not registered"}",
            $"Scan (B&W) default preset: {ScanEffectSettings.ToDisplayName(defaultScanSettings.Strength)}",
            $"Scan (B&W) default settings: {defaultScanSettings.ToSummary()}",
            "Scan (Colored) action available: True",
            $"IContextMenu fallback registered: {IsClassicFallbackRegistered()}",
            $"Duplicate PdfRightClickSuite handlers found: {duplicateHandlers.Count > 0}",
            $"Duplicate PdfRightClickSuite handler count: {duplicateHandlers.Count}",
            $"Duplicate PdfRightClickSuite handler paths: {(duplicateHandlers.Count == 0 ? "None" : string.Join("; ", duplicateHandlers))}",
            $"COM fallback CLSID HKCU: {RegistryKeyExists(Registry.CurrentUser, @"Software\Classes\CLSID\{68A2F5F6-2E91-4C66-B126-896B8C6C6834}")}",
            $"COM top-menu CLSID HKCU: {RegistryKeyExists(Registry.CurrentUser, @"Software\Classes\CLSID\{065E1050-7F50-4FDF-94C6-19B998E64A83}")}",
            $"COM fallback CLSID HKLM: {RegistryKeyExists(Registry.LocalMachine, @"Software\Classes\CLSID\{68A2F5F6-2E91-4C66-B126-896B8C6C6834}")}",
            $"Shell Extensions Approved fallback HKCU: {RegistryValueExists(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved", ClassicFallbackClsid)}",
            $"Shell Extensions Approved top-menu HKCU: {RegistryValueExists(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved", ClassicTopMenuClsid)}",
            $"PDF Gear context menu status: {pdfGearStatus.Summary}",
            $"PDF Gear representative disabled count: {pdfGearStatus.DisabledCount}/{pdfGearStatus.FoundCount}",
            $"PDF Gear remaining active keys: {(pdfGearStatus.ActiveKeys.Count == 0 ? "None" : string.Join("; ", pdfGearStatus.ActiveKeys))}",
            "Modern menu intentionally disabled: True",
            $"Modern sparse package registered: {packageInfo.IsRegistered}",
            $"Modern package name: {packageInfo.Name ?? "Not registered"}",
            $"Modern package full name: {packageInfo.FullName ?? "Not registered"}",
            $"Modern package version: {packageInfo.Version ?? "Not registered"}",
            $"Modern package publisher: {packageInfo.Publisher ?? "Not registered"}",
            $"Modern certificate installed: {certificateStatus.InTrustedPeople || certificateStatus.InRoot || certificateStatus.InMachineTrustedPeople}",
            $"Modern certificate current user trusted people: {certificateStatus.InTrustedPeople}",
            $"Modern certificate current user root: {certificateStatus.InRoot}",
            $"Modern certificate local machine trusted people: {certificateStatus.InMachineTrustedPeople}",
            $"Modern certificate thumbprint: {certificateStatus.Thumbprint ?? "Not recorded"}",
            $"Modern registration logs path: {modernLogPath}",
            "Modern menu should appear: False",
            "Modern menu placement: not applicable; default installer uses the classic top shell verb only.",
            $"Logs path: {GetLogsDirectory()}",
            "Dependency status:",
            $"  Self-contained .NET app: {File.Exists(Path.ChangeExtension(cliPath, ".dll")) == false}",
            $"  Edge: {locator.FindEdge() ?? "Not found"}",
            $"  LibreOffice: {libreOfficePath ?? "Not found"}",
            $"  Microsoft Office PDF fallback: {MicrosoftOfficePdfConverter.GetAvailabilitySummary()}",
            $"  PDF to Word backends: Microsoft Word={wordAvailability}, LibreOffice={libreOfficePath ?? "Not found"}",
            "  PDF to Excel backend: built-in text extraction (PdfPig)",
            "  PDF to PowerPoint backend: built-in page rendering (PDFtoImage)",
        };

        File.WriteAllLines(reportPath, lines);
        AnsiConsole.Write(new Panel(Markup.Escape(string.Join(Environment.NewLine, lines))).Header("Diagnostics").BorderColor(Color.Blue));
        return reportPath;
    }

    private static async Task<int> RunUserScriptAsync(string scriptName, string title, CliOptions options, CancellationToken cancellationToken)
    {
        var scriptPath = FindScriptPath(scriptName)
            ?? throw new FileNotFoundException($"Could not locate {scriptName}. Run from the repository root or from a release package that includes scripts.");

        var arguments = BuildPowerShellScriptArguments(scriptName, scriptPath, options);

        if (options.DryRun)
        {
            AnsiConsole.Write(new Panel(new Markup($"""
                [bold yellow]Dry run[/]
                {Markup.Escape(title)}

                Script:
                  [cyan]{Markup.Escape(scriptPath)}[/]

                Command:
                  powershell.exe {Markup.Escape(string.Join(" ", arguments.Select(QuoteForDisplay)))}
                """))
                .Header("Installer")
                .BorderColor(Color.Yellow));
            return 0;
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        AnsiConsole.MarkupLine($"[cyan]{Markup.Escape(title)}[/]");
        AnsiConsole.MarkupLine($"[dim]{Markup.Escape(scriptPath)}[/]");

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await TerminateChildProcessAsync(process, outputTask, errorTask).ConfigureAwait(false);
            throw;
        }

        var output = await outputTask.ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(output))
        {
            AnsiConsole.WriteLine(output.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(error.TrimEnd())}[/]");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{scriptName} failed with exit code {process.ExitCode}.");
        }

        return 0;
    }

    private static async Task TerminateChildProcessAsync(Process process, Task<string> outputTask, Task<string> errorTask)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Could not stop a cancelled installer process.");
        }

        try
        {
            using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Cancelled installer process did not terminate cleanly.");
        }
    }

    private static IReadOnlyList<string> BuildPowerShellScriptArguments(string scriptName, string scriptPath, CliOptions options)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            scriptPath
        };

        if (string.Equals(scriptName, "install.ps1", StringComparison.OrdinalIgnoreCase) && ShouldPassReleaseSource(scriptPath))
        {
            arguments.Add("-SourcePath");
            arguments.Add(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        if (options.Yes || Console.IsInputRedirected)
        {
            arguments.Add("-NoRestartPrompt");
        }

        return arguments;
    }

    private static bool ShouldPassReleaseSource(string scriptPath)
    {
        var repoRoot = FindRepoRootOrNull();
        if (repoRoot is null)
        {
            return true;
        }

        var repoScript = Path.GetFullPath(Path.Combine(repoRoot, "scripts", Path.GetFileName(scriptPath)));
        return !string.Equals(Path.GetFullPath(scriptPath), repoScript, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindScriptPath(string fileName)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidates = new List<string>
        {
            Path.Combine(baseDirectory, "scripts", fileName),
            Path.Combine(baseDirectory, fileName),
            Path.Combine(baseDirectory, "..", "scripts", fileName)
        };

        var repoRoot = FindRepoRootOrNull();
        if (repoRoot is not null)
        {
            candidates.Add(Path.Combine(repoRoot, "scripts", fileName));
        }

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }

    private static string QuoteForDisplay(string value)
        => value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    private static async Task<string> RunSelfTestAsync(CliOptions options, CancellationToken cancellationToken)
    {
        var root = options.DiagnosticsDir ?? Path.Combine(FindRepoRootOrNull() ?? GetLogsDirectory(), "artifacts", "diagnostics");
        Directory.CreateDirectory(root);
        var workRoot = Path.Combine(Path.GetTempPath(), "PdfRightClickSuiteSelfTest");
        var work = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var report = new List<string>
        {
            "PdfRightClickSuite Self-Test",
            $"Started: {DateTimeOffset.Now:O}",
            $"Temp: {work}"
        };

        try
        {
            var jpg = Path.Combine(work, "sample image.jpg");
            CreateSampleJpeg(jpg);
            var convertOut = new OutputNameService().GetConvertSingleOutputPath(jpg);
            var convertRequest = WriteSelfTestRequest(work, PdfAction.Convert, [jpg]);
            var convertService = new PdfConvertService(new PdfMergeService(new PdfPageCountService()), new ExternalToolLocator());
            await convertService.ConvertSingleAsync(new RequestFileService().Read(convertRequest).SelectedFiles[0], convertOut, cancellationToken).ConfigureAwait(false);
            report.Add($"Convert JPG: {(File.Exists(convertOut) ? "PASS" : "FAIL")} {convertOut}");
            report.Add("Convert request mode confirmation prompt: PASS not required");

            var png = Path.Combine(work, "sample image.png");
            CreateSamplePng(png);
            var pngOut = new OutputNameService().GetConvertSingleOutputPath(png);
            var pngRequest = WriteSelfTestRequest(work, PdfAction.Convert, [png]);
            await convertService.ConvertSingleAsync(new RequestFileService().Read(pngRequest).SelectedFiles[0], pngOut, cancellationToken).ConfigureAwait(false);
            report.Add($"Convert PNG: {(File.Exists(pngOut) ? "PASS" : "FAIL")} {pngOut}");

            var txt = Path.Combine(work, "sample text.txt");
            File.WriteAllText(txt, "PdfRightClickSuite TXT conversion self-test.");
            var txtOut = new OutputNameService().GetConvertSingleOutputPath(txt);
            var txtRequest = WriteSelfTestRequest(work, PdfAction.Convert, [txt]);
            await convertService.ConvertSingleAsync(new RequestFileService().Read(txtRequest).SelectedFiles[0], txtOut, cancellationToken).ConfigureAwait(false);
            report.Add($"Convert TXT: {(File.Exists(txtOut) ? "PASS" : "FAIL")} {txtOut}");

            var pdf1 = Path.Combine(work, "one.pdf");
            var pdf2 = Path.Combine(work, "two.pdf");
            CreateSimplePdf(pdf1, "one");
            CreateSimplePdf(pdf2, "two");
            var mergeOut = new OutputNameService().GetMergeOutputPath(work);
            var mergeRequest = WriteSelfTestRequest(work, PdfAction.Merge, [pdf1, pdf2]);
            await new PdfMergeService(new PdfPageCountService())
                .MergeAsync(new RequestFileService().Read(mergeRequest).SelectedFiles, mergeOut, cancellationToken)
                .ConfigureAwait(false);
            report.Add($"Merge PDFs: {(File.Exists(mergeOut) && new PdfPageCountService().GetPageCount(mergeOut) == 2 ? "PASS" : "FAIL")} {mergeOut}");

            var officeSource = Path.Combine(work, "office-source.pdf");
            CreateTwoPageTextPdf(officeSource);
            var officeSourceHash = Sha256File(officeSource);
            var officeService = new PdfToOfficeConvertService(new ExternalToolLocator());
            var officeOutputNameService = new OutputNameService();
            var excelOut = officeOutputNameService.GetPdfToOfficeOutputPath(officeSource, ".xlsx");
            var excelRequest = WriteSelfTestRequest(work, PdfAction.ConvertToExcel, [officeSource]);
            var excelSource = new RequestFileService().Read(excelRequest).SelectedFiles.Single();
            var excelResult = await officeService
                .ConvertAsync(excelSource, excelOut, OfficeExportFormat.Excel, cancellationToken)
                .ConfigureAwait(false);
            using (var workbook = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(excelOut, false))
            {
                var sheetCount = workbook.WorkbookPart?.Workbook?.Sheets?.ChildElements.Count ?? 0;
                report.Add($"PDF to Excel: {(sheetCount == 2 && excelResult.PageCount == 2 ? "PASS" : "FAIL")} sheets={sheetCount} backend={excelResult.BackendUsed} {excelOut}");
            }

            var powerPointOut = officeOutputNameService.GetPdfToOfficeOutputPath(officeSource, ".pptx");
            var powerPointRequest = WriteSelfTestRequest(work, PdfAction.ConvertToPowerPoint, [officeSource]);
            var powerPointSource = new RequestFileService().Read(powerPointRequest).SelectedFiles.Single();
            var powerPointResult = await officeService
                .ConvertAsync(powerPointSource, powerPointOut, OfficeExportFormat.PowerPoint, cancellationToken)
                .ConfigureAwait(false);
            using (var presentation = DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(powerPointOut, false))
            {
                var slideCount = presentation.PresentationPart?.Presentation?.SlideIdList?.ChildElements.Count ?? 0;
                report.Add($"PDF to PowerPoint: {(slideCount == 2 && powerPointResult.PageCount == 2 ? "PASS" : "FAIL")} slides={slideCount} backend={powerPointResult.BackendUsed} {powerPointOut}");
            }

            var locator = new ExternalToolLocator();
            if (MicrosoftOfficePdfConverter.IsPdfToDocxAvailable() || locator.FindLibreOffice() is not null)
            {
                var wordOut = officeOutputNameService.GetPdfToOfficeOutputPath(officeSource, ".docx");
                var wordRequest = WriteSelfTestRequest(work, PdfAction.ConvertToWord, [officeSource]);
                var wordSource = new RequestFileService().Read(wordRequest).SelectedFiles.Single();
                var wordResult = await officeService
                    .ConvertAsync(wordSource, wordOut, OfficeExportFormat.Word, cancellationToken)
                    .ConfigureAwait(false);
                using var wordDocument = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(wordOut, false);
                report.Add($"PDF to Word: {(wordDocument.MainDocumentPart is not null && wordResult.PageCount == 2 ? "PASS" : "FAIL")} backend={wordResult.BackendUsed} {wordOut}");
            }
            else
            {
                report.Add("PDF to Word: SKIPPED (no Word/LibreOffice)");
            }

            report.Add($"PDF-to-Office source unchanged: {(string.Equals(officeSourceHash, Sha256File(officeSource), StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL")}");

            var splitFolder = new OutputNameService().GetSplitOutputFolder(pdf1);
            var splitRequest = WriteSelfTestRequest(work, PdfAction.Split, [pdf1]);
            await new PdfSplitService(new PdfPageCountService())
                .SplitAsync(new RequestFileService().Read(splitRequest).SelectedFiles[0], [1], splitFolder, cancellationToken)
                .ConfigureAwait(false);
            report.Add($"Split PDF: {(Directory.Exists(splitFolder) && Directory.GetFiles(splitFolder, "*.pdf").Length == 1 ? "PASS" : "FAIL")} {splitFolder}");

            var outputNameService = new OutputNameService();
            var scanOut = outputNameService.GetScanOutputPath(pdf1);
            var scanColoredOut = outputNameService.GetColoredScanOutputPath(pdf1);
            var scanRequest = WriteSelfTestRequest(work, PdfAction.Scan, [pdf1]);
            var scanColoredRequest = WriteSelfTestRequest(work, PdfAction.ScanColored, [pdf1]);
            var originalHashBeforeScan = Sha256File(pdf1);
            var scanSettings = ScanEffectSettings.Default;
            await new PdfScanEffectService()
                .CreateScannedLikePdfAsync(new RequestFileService().Read(scanRequest).SelectedFiles[0], scanOut, scanSettings, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await new PdfScanEffectService()
                .CreateScannedLikePdfAsync(new RequestFileService().Read(scanColoredRequest).SelectedFiles[0], scanColoredOut, scanSettings, cancellationToken: cancellationToken, colorMode: ScanColorMode.Colored)
                .ConfigureAwait(false);
            report.Add($"Scan (B&W) low-quality settings: {scanSettings.ToSummary()}");
            report.Add($"Scan (B&W) low-quality PDF: {(File.Exists(scanOut) ? "PASS" : "FAIL")} {scanOut}");
            report.Add($"Scan (Colored) low-quality PDF: {(File.Exists(scanColoredOut) ? "PASS" : "FAIL")} {scanColoredOut}");
            report.Add($"Scan original unchanged: {(string.Equals(originalHashBeforeScan, Sha256File(pdf1), StringComparison.OrdinalIgnoreCase) ? "PASS" : "FAIL")}");
        }
        finally
        {
            if (!options.KeepTemp)
            {
                TryDeleteDirectory(work);
                TryDeleteEmptyDirectory(workRoot);
            }
        }

        var reportPath = Path.Combine(root, $"self-test-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllLines(reportPath, report);
        AnsiConsole.Write(new Panel(Markup.Escape(string.Join(Environment.NewLine, report))).Header("Self-test").BorderColor(Color.Green));
        return reportPath;
    }

    private static string WriteSelfTestRequest(string folder, PdfAction action, IReadOnlyList<string> files)
    {
        var path = Path.Combine(folder, $"request-{action}-{Guid.NewGuid():N}.json");
        new RequestFileService().Write(path, new PdfRequest(action, files, DateTimeOffset.Now, folder, Guid.NewGuid().ToString("N")));
        return path;
    }

    private static void CreateSampleJpeg(string path)
        => CreateSampleImage(path, SkiaSharp.SKEncodedImageFormat.Jpeg);

    private static void CreateSamplePng(string path)
        => CreateSampleImage(path, SkiaSharp.SKEncodedImageFormat.Png);

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

    private static void CreateSimplePdf(string path, string text)
    {
        PdfSharpBootstrapFacade.EnsureInitialized();
        using var document = new PdfSharp.Pdf.PdfDocument();
        var page = document.AddPage();
        using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        var font = new PdfSharp.Drawing.XFont("Arial", 18);
        gfx.DrawString(text, font, PdfSharp.Drawing.XBrushes.Black, new PdfSharp.Drawing.XRect(0, 0, page.Width.Point, page.Height.Point), PdfSharp.Drawing.XStringFormats.Center);
        document.Save(path);
    }

    private static void CreateTwoPageTextPdf(string path)
    {
        PdfSharpBootstrapFacade.EnsureInitialized();
        using var document = new PdfSharp.Pdf.PdfDocument();
        for (var pageNumber = 1; pageNumber <= 2; pageNumber++)
        {
            var page = document.AddPage();
            using var graphics = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
            var font = new PdfSharp.Drawing.XFont("Arial", 14);
            graphics.DrawString("Project", font, PdfSharp.Drawing.XBrushes.Black, new PdfSharp.Drawing.XPoint(72, 100));
            graphics.DrawString($"Page {pageNumber}", font, PdfSharp.Drawing.XBrushes.Black, new PdfSharp.Drawing.XPoint(260, 100));
            graphics.DrawString("Flow", font, PdfSharp.Drawing.XBrushes.Black, new PdfSharp.Drawing.XPoint(72, 130));
            graphics.DrawString($"{pageNumber * 10} m3/day", font, PdfSharp.Drawing.XBrushes.Black, new PdfSharp.Drawing.XPoint(260, 130));
        }

        document.Save(path);
    }

    private static string GetVersion()
        => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.1.0";

    private static string GetLogsDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PdfRightClickSuite", "logs");

    private static string? ReadInstallDir()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\PdfRightClickSuite");
        return key?.GetValue("InstallDir") as string;
    }

    private static bool IsClassicShellRegistered()
        => GetClassicTopMenuInfo().IsRegistered || IsClassicFallbackRegistered();

    private static bool IsClassicFallbackRegistered()
        => RegistryKeyExists(Registry.CurrentUser, @"Software\Classes\*\shellex\ContextMenuHandlers\PdfRightClickSuite");

    private static ClassicTopMenuInfo GetClassicTopMenuInfo()
    {
        const string path = @"Software\Classes\*\shell\PdfRightClickSuite";
        using var key = Registry.CurrentUser.OpenSubKey(path);
        if (key is null)
        {
            return new ClassicTopMenuInfo(false, path, "not registered", null, false, false, false, null, false);
        }

        var muiVerb = key.GetValue("MUIVerb") as string;
        var position = key.GetValue("Position") as string;
        var handler = key.GetValue("ExplorerCommandHandler") as string;
        var multiSelect = key.GetValue("MultiSelectModel") as string;
        var icon = key.GetValue("Icon") as string;
        var isTopHandler = string.Equals(handler, ClassicTopMenuClsid, StringComparison.OrdinalIgnoreCase);
        var handlerType = isTopHandler ? "shell verb + ExplorerCommandHandler" : "shell verb";
        return new ClassicTopMenuInfo(
            isTopHandler,
            path,
            handlerType,
            handler,
            string.Equals(muiVerb, "PDF", StringComparison.Ordinal),
            string.Equals(position, "Top", StringComparison.OrdinalIgnoreCase),
            string.Equals(multiSelect, "Player", StringComparison.OrdinalIgnoreCase),
            icon,
            icon is not null && File.Exists(icon));
    }

    private static IReadOnlyList<string> GetDuplicatePdfRightClickSuiteHandlers(ClassicTopMenuInfo topMenu)
    {
        var candidates = new[]
        {
            @"Software\Classes\*\shell\PdfRightClickSuite",
            @"Software\Classes\AllFilesystemObjects\shell\PdfRightClickSuite",
            @"Software\Classes\SystemFileAssociations\.pdf\shell\PdfRightClickSuite",
            @"Software\Classes\SystemFileAssociations\image\shell\PdfRightClickSuite",
            @"Software\Classes\SystemFileAssociations\text\shell\PdfRightClickSuite",
            @"Software\Classes\*\shellex\ContextMenuHandlers\PdfRightClickSuite",
            @"Software\Classes\AllFilesystemObjects\shellex\ContextMenuHandlers\PdfRightClickSuite"
        };

        return candidates
            .Where(path => RegistryKeyExists(Registry.CurrentUser, path))
            .Where(path => !(topMenu.IsRegistered && string.Equals(path, topMenu.RegistryPath, StringComparison.OrdinalIgnoreCase)))
            .Select(path => @"HKCU:\" + path)
            .ToArray();
    }

    private static PdfGearContextMenuStatus GetPdfGearContextMenuStatus()
    {
        var representativeKeys = new[]
        {
            @"Software\Classes\SystemFileAssociations\.pdf\shell\PDFGearTools",
            @"Software\Classes\SystemFileAssociations\image\shell\PDFGearTools",
            @"Software\Classes\PdfGear.App.1\shell\open"
        };

        var found = 0;
        var disabled = 0;
        var active = new List<string>();
        foreach (var path in representativeKeys)
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            if (key is null)
            {
                continue;
            }

            found++;
            var hasLegacyDisable = key.GetValue("LegacyDisable") is not null;
            var hasProgrammaticAccessOnly = key.GetValue("ProgrammaticAccessOnly") is not null;
            if (hasLegacyDisable && hasProgrammaticAccessOnly)
            {
                disabled++;
            }
            else
            {
                active.Add(@"HKCU:\" + path);
            }
        }

        var summary = found == 0 ? "No representative HKCU PDF Gear context-menu keys found" :
            active.Count == 0 ? "Representative HKCU PDF Gear context-menu keys disabled" :
            "Representative HKCU PDF Gear context-menu keys still active or partially disabled";
        return new PdfGearContextMenuStatus(summary, found, disabled, active);
    }

    private static bool IsModernPackageRegistered()
    {
        var packageRoot = Registry.CurrentUser.OpenSubKey(@"Software\Classes\PackagedCom\Package");
        return packageRoot?.GetSubKeyNames().Any(name => name.Contains("PdfRightClickSuite", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static ModernPackageInfo GetModernPackageInfo()
    {
        using var appKey = Registry.CurrentUser.OpenSubKey(@"Software\PdfRightClickSuite");
        var recordedFullName = appKey?.GetValue("ModernPackageFullName") as string;

        using var packageRoot = Registry.CurrentUser.OpenSubKey(@"Software\Classes\PackagedCom\Package");
        var registryFullName = packageRoot?.GetSubKeyNames()
            .FirstOrDefault(name => name.Contains("PdfRightClickSuite", StringComparison.OrdinalIgnoreCase));
        var fullName = recordedFullName ?? registryFullName;
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return new ModernPackageInfo(false, null, null, null, null);
        }

        var parts = fullName.Split('_');
        var name = parts.Length > 0 ? parts[0] : fullName;
        var version = parts.Length > 1 ? parts[1] : null;
        var publisher = appKey?.GetValue("ModernPackagePublisher") as string;
        return new ModernPackageInfo(true, name, fullName, version, publisher);
    }

    private static ModernCertificateStatus GetModernCertificateStatus()
    {
        using var appKey = Registry.CurrentUser.OpenSubKey(@"Software\PdfRightClickSuite");
        var thumbprint = appKey?.GetValue("ModernCertificateThumbprint") as string;
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            thumbprint = FindPdfRightClickSuiteCertificateThumbprint(StoreName.TrustedPeople)
                ?? FindPdfRightClickSuiteCertificateThumbprint(StoreName.Root);
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                return new ModernCertificateStatus(null, false, false, false);
            }
        }

        return new ModernCertificateStatus(
            thumbprint,
            CertificateExists(StoreName.TrustedPeople, StoreLocation.CurrentUser, thumbprint),
            CertificateExists(StoreName.Root, StoreLocation.CurrentUser, thumbprint),
            CertificateExists(StoreName.TrustedPeople, StoreLocation.LocalMachine, thumbprint));
    }

    private static string? FindPdfRightClickSuiteCertificateThumbprint(StoreName storeName)
    {
        using var store = new X509Store(storeName, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates
            .OfType<X509Certificate2>()
            .Where(certificate => certificate.Subject.Contains("PdfRightClickSuite", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(certificate => certificate.NotAfter)
            .Select(certificate => certificate.Thumbprint)
            .FirstOrDefault();
    }

    private static bool CertificateExists(StoreName storeName, StoreLocation storeLocation, string thumbprint)
    {
        using var store = new X509Store(storeName, storeLocation);
        store.Open(OpenFlags.ReadOnly);
        return store.Certificates
            .Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
            .Count > 0;
    }

    private static bool RegistryKeyExists(RegistryKey root, string path)
    {
        using var key = root.OpenSubKey(path);
        return key is not null;
    }

    private static bool RegistryValueExists(RegistryKey root, string path, string name)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue(name) is not null;
    }

    private static string? FindRepoRootOrNull()
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

        return null;
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
        catch (Exception ex)
        {
            Logger.Error(ex, $"Could not delete temporary self-test folder '{folder}'.");
        }
    }

    private static void TryDeleteEmptyDirectory(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Could not delete empty temporary folder '{folder}'.");
        }
    }
}

internal sealed record ClassicTopMenuInfo(
    bool IsRegistered,
    string RegistryPath,
    string HandlerType,
    string? ExplorerCommandHandler,
    bool HasPdfVerb,
    bool HasPositionTop,
    bool HasPlayerMultiSelectModel,
    string? IconValue,
    bool IconExists);

internal sealed record PdfGearContextMenuStatus(string Summary, int FoundCount, int DisabledCount, IReadOnlyList<string> ActiveKeys);

internal sealed record ModernPackageInfo(bool IsRegistered, string? Name, string? FullName, string? Version, string? Publisher);

internal sealed record ModernCertificateStatus(string? Thumbprint, bool InTrustedPeople, bool InRoot, bool InMachineTrustedPeople);

internal sealed class CliOptions
{
    public string? RequestPath { get; private init; }

    public PdfAction? Action { get; private init; }

    public IReadOnlyList<string> Files { get; private init; } = [];

    public bool Debug { get; private init; }

    public bool Yes { get; private init; }

    public bool ConfirmConvert { get; private init; }

    public bool ShowHelp { get; private init; }

    public bool ShowWelcome { get; private init; }

    public bool ShowVersion { get; private init; }

    public bool Diagnose { get; private init; }

    public bool SelfTest { get; private init; }

    public bool KeepTemp { get; private init; }

    public bool InstallUser { get; private init; }

    public bool UninstallUser { get; private init; }

    public bool DryRun { get; private init; }

    public string? DiagnosticsDir { get; private init; }

    public int? ScanDpi { get; private init; }

    public ScanStrength ScanStrength { get; private init; } = ScanStrength.LowQuality;

    public int? ScanJpegQuality { get; private init; }

    public float? ScanBlurRadius { get; private init; }

    public float? ScanMaxRotationDegrees { get; private init; }

    public ScanEffectSettings ScanSettings => ScanEffectSettings.ForPreset(ScanStrength, ScanDpi, ScanJpegQuality, ScanBlurRadius, ScanMaxRotationDegrees);

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        string? request = null;
        PdfAction? action = null;
        var files = new List<string>();
        var debug = false;
        var yes = false;
        var confirmConvert = false;
        var showWelcome = args.Count == 0;
        var showHelp = false;
        var showVersion = false;
        var diagnose = false;
        var selfTest = false;
        var keepTemp = false;
        var installUser = false;
        var uninstallUser = false;
        var dryRun = false;
        string? diagnosticsDir = null;
        int? scanDpi = null;
        var scanStrength = ScanStrength.LowQuality;
        int? scanJpegQuality = null;
        float? scanBlurRadius = null;
        float? scanMaxRotationDegrees = null;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "--help":
                case "-h":
                case "/?":
                    showHelp = true;
                    showWelcome = false;
                    break;
                case "--version":
                    showVersion = true;
                    showWelcome = false;
                    break;
                case "--diagnose":
                    diagnose = true;
                    showWelcome = false;
                    break;
                case "--self-test":
                    selfTest = true;
                    showWelcome = false;
                    break;
                case "--install-user":
                    installUser = true;
                    showWelcome = false;
                    break;
                case "--uninstall-user":
                    uninstallUser = true;
                    showWelcome = false;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--keep-temp":
                    keepTemp = true;
                    break;
                case "--diagnostics-dir":
                    diagnosticsDir = RequireValue(args, ref i, "--diagnostics-dir");
                    break;
                case "--request":
                    request = RequireValue(args, ref i, "--request");
                    break;
                case "--action":
                    var value = RequireValue(args, ref i, "--action");
                    if (!TryParseAction(value, out var parsedAction))
                    {
                        throw new ArgumentException($"Unknown action '{value}'.");
                    }

                    action = parsedAction;
                    break;
                case "--files":
                    while (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        files.Add(args[++i]);
                    }

                    break;
                case "--debug":
                case "--verbose":
                    debug = true;
                    break;
                case "--yes":
                case "-y":
                    yes = true;
                    break;
                case "--confirm-convert":
                    confirmConvert = true;
                    break;
                case "--scan-dpi":
                    if (!int.TryParse(RequireValue(args, ref i, "--scan-dpi"), out var parsedScanDpi))
                    {
                        throw new ArgumentException("--scan-dpi must be a number.");
                    }

                    scanDpi = parsedScanDpi;
                    break;
                case "--scan-strength":
                    var strength = RequireValue(args, ref i, "--scan-strength");
                    if (!ScanEffectSettings.TryParseStrength(strength, out scanStrength))
                    {
                        throw new ArgumentException("--scan-strength must be light, low-quality, or rough.");
                    }

                    break;
                case "--scan-jpeg-quality":
                    if (!int.TryParse(RequireValue(args, ref i, "--scan-jpeg-quality"), out var parsedScanJpegQuality))
                    {
                        throw new ArgumentException("--scan-jpeg-quality must be a number.");
                    }

                    scanJpegQuality = parsedScanJpegQuality;
                    break;
                case "--scan-blur":
                    scanBlurRadius = ParseFloat(RequireValue(args, ref i, "--scan-blur"), "--scan-blur");
                    break;
                case "--scan-rotation":
                    scanMaxRotationDegrees = ParseFloat(RequireValue(args, ref i, "--scan-rotation"), "--scan-rotation");
                    if (scanMaxRotationDegrees < 0)
                    {
                        throw new ArgumentException("--scan-rotation must be zero or greater.");
                    }

                    break;
                default:
                    files.Add(arg);
                    break;
            }
        }

        return new CliOptions
        {
            RequestPath = request,
            Action = action,
            Files = files,
            Debug = debug,
            Yes = yes,
            ConfirmConvert = confirmConvert,
            ShowHelp = showHelp,
            ShowWelcome = showWelcome,
            ShowVersion = showVersion,
            Diagnose = diagnose,
            SelfTest = selfTest,
            KeepTemp = keepTemp,
            InstallUser = installUser,
            UninstallUser = uninstallUser,
            DryRun = dryRun,
            DiagnosticsDir = diagnosticsDir,
            ScanDpi = scanDpi,
            ScanStrength = scanStrength,
            ScanJpegQuality = scanJpegQuality,
            ScanBlurRadius = scanBlurRadius,
            ScanMaxRotationDegrees = scanMaxRotationDegrees
        };
    }

    private static bool TryParseAction(string value, out PdfAction action)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "scan-colored":
            case "scan-color":
            case "scan-coloured":
            case "scan-colour":
                action = PdfAction.ScanColored;
                return true;
            case "scan-bw":
            case "scan-b&w":
            case "scan-black-and-white":
            case "scan-blackwhite":
                action = PdfAction.Scan;
                return true;
            case "convert-to-word":
            case "convert-docx":
            case "convert-word":
            case "converttoword":
            case "convertword":
                action = PdfAction.ConvertToWord;
                return true;
            case "convert-to-excel":
            case "convert-xlsx":
            case "convert-excel":
            case "converttoexcel":
            case "convertexcel":
                action = PdfAction.ConvertToExcel;
                return true;
            case "convert-to-powerpoint":
            case "convert-to-ppt":
            case "convert-pptx":
            case "convert-powerpoint":
            case "converttopowerpoint":
            case "convertpowerpoint":
                action = PdfAction.ConvertToPowerPoint;
                return true;
            default:
                return Enum.TryParse(value, ignoreCase: true, out action);
        }
    }

    private static float ParseFloat(string value, string option)
    {
        if (!float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"{option} must be a number.");
        }

        return parsed;
    }

    private static string RequireValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count)
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[++index];
    }
}

internal static class PdfSharpBootstrapFacade
{
    public static void EnsureInitialized()
    {
        var type = typeof(PdfMergeService).Assembly.GetType("PdfRightClickSuite.Core.PdfSharpBootstrap");
        type?.GetMethod("EnsureInitialized", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
    }
}

internal static class SortingUi
{
    public static IReadOnlyList<string> Sort(
        IReadOnlyList<string> files,
        PdfPageCountService pageCountService,
        CancellationToken cancellationToken)
    {
        var model = new SortingModel(files);
        var pageCounts = files.ToDictionary(file => file, file => TryGetPageCount(file, pageCountService), StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Draw(model, pageCounts);
            var key = ReadKey(cancellationToken).Key;
            switch (key)
            {
                case ConsoleKey.UpArrow:
                    model.SelectPrevious();
                    break;
                case ConsoleKey.DownArrow:
                    model.SelectNext();
                    break;
                case ConsoleKey.LeftArrow:
                    model.MoveSelectedLeft();
                    break;
                case ConsoleKey.RightArrow:
                    model.MoveSelectedRight();
                    break;
                case ConsoleKey.R:
                    model.Reset();
                    break;
                case ConsoleKey.H:
                case ConsoleKey.Oem2:
                    ShowHelp(cancellationToken);
                    break;
                case ConsoleKey.Enter:
                    return model.Items.ToArray();
                case ConsoleKey.Escape:
                    throw new OperationCanceledException();
            }
        }
    }

    private static void Draw(SortingModel model, IReadOnlyDictionary<string, int?> pageCounts)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(new Rule("[bold cyan]Sort files[/]").RuleStyle("grey"));
        if (Console.WindowWidth < 80 || Console.WindowHeight < 18)
        {
            AnsiConsole.MarkupLine("[yellow]Terminal is small; the list remains usable but may wrap.[/]");
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("#")
            .AddColumn("Filename")
            .AddColumn("Folder")
            .AddColumn("Size")
            .AddColumn("Pages");

        var visibleRows = Math.Max(5, Console.WindowHeight - 12);
        var start = Math.Max(0, Math.Min(model.SelectedIndex - (visibleRows / 2), Math.Max(0, model.Items.Count - visibleRows)));
        var end = Math.Min(model.Items.Count, start + visibleRows);

        for (var i = start; i < end; i++)
        {
            var file = model.Items[i];
            var info = new FileInfo(file);
            var cells = new[]
            {
                (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Markup.Escape(info.Name),
                Markup.Escape(info.DirectoryName ?? string.Empty),
                FormatBytes(info.Exists ? info.Length : 0),
                pageCounts.TryGetValue(file, out var pages) && pages is not null ? pages.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-"
            };

            if (i == model.SelectedIndex)
            {
                cells = cells.Select(cell => $"[black on yellow]{cell}[/]").ToArray();
            }

            table.AddRow(cells);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[dim]Showing {start + 1}-{end} of {model.Items.Count}[/]");
        AnsiConsole.MarkupLine("[bold]Up/Down[/] select  [bold]Left/Right[/] move  [bold]Enter[/] confirm  [bold]Esc[/] cancel  [bold]R[/] reset  [bold]H/?[/] help");
    }

    private static void ShowHelp(CancellationToken cancellationToken)
    {
        AnsiConsole.Write(new Panel("""
            Up arrow: select previous file
            Down arrow: select next file
            Left arrow: move selected file up
            Right arrow: move selected file down
            Enter: confirm order
            Esc: cancel
            R: reset to original order
            H or ?: show this help
            """).Header("Sorting controls").BorderColor(Color.Blue));
        ReadKey(cancellationToken);
    }

    private static ConsoleKeyInfo ReadKey(CancellationToken cancellationToken)
    {
        while (!Console.KeyAvailable)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.Sleep(25);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Console.ReadKey(intercept: true);
    }

    private static int? TryGetPageCount(string file, PdfPageCountService pageCountService)
    {
        if (!string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return pageCountService.GetPageCount(file);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not read the page count for '{file}': {ex.Message}");
            return null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
