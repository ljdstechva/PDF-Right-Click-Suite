using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace PdfRightClickSuite.Core;

public static class MicrosoftOfficePdfConverter
{
    private const string WordProgId = "Word.Application";
    private const string ExcelProgId = "Excel.Application";
    private const string PowerPointProgId = "PowerPoint.Application";

    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromMinutes(3);

    public static bool CanConvert(string extension) => ResolveApplication(extension) is not null;

    public static bool IsAvailableFor(string extension)
    {
        var application = ResolveApplication(extension);
        return application is not null && TryGetComType(application.ProgId) is not null;
    }

    public static string GetAvailabilitySummary()
        => $"Word={FormatAvailability(WordProgId)}, Excel={FormatAvailability(ExcelProgId)}, PowerPoint={FormatAvailability(PowerPointProgId)}";

    public static void Convert(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var application = ResolveApplication(Path.GetExtension(inputPath))
            ?? throw new NotSupportedException($"Microsoft Office conversion does not support '{Path.GetExtension(inputPath)}'.");
        var applicationType = TryGetComType(application.ProgId)
            ?? throw new FileNotFoundException($"Microsoft Office desktop conversion is not available for '{Path.GetExtension(inputPath)}'.");
        var officeTempPdf = CreateOfficeTempPdfPath();
        var stagedPdf = AtomicFileWriter.CreateTempPathBeside(outputPath);

        try
        {
            RunOnStaThread(
                () => application.Convert(applicationType, inputPath, officeTempPdf),
                ConversionTimeout,
                cancellationToken,
                () =>
                {
                    AtomicFileWriter.TryDelete(officeTempPdf);
                    AtomicFileWriter.TryDelete(stagedPdf);
                });

            if (!File.Exists(officeTempPdf) || new FileInfo(officeTempPdf).Length == 0)
            {
                throw new PdfProcessingException($"Microsoft Office conversion did not create an output PDF for '{inputPath}'.");
            }

            File.Copy(officeTempPdf, stagedPdf);
            AtomicFileWriter.MoveIntoPlace(stagedPdf, outputPath);
        }
        catch (Exception ex) when (ex is not IOException and not OperationCanceledException and not TimeoutException and not PdfProcessingException)
        {
            throw new PdfProcessingException($"Microsoft Office conversion failed for '{inputPath}'.", ex);
        }
        finally
        {
            AtomicFileWriter.TryDelete(officeTempPdf);
            AtomicFileWriter.TryDelete(stagedPdf);
        }
    }

    private static OfficeApplication? ResolveApplication(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".doc" or ".docx" or ".rtf" or ".odt" => new OfficeApplication(WordProgId, ConvertWord),
            ".xls" or ".xlsx" or ".ods" => new OfficeApplication(ExcelProgId, ConvertExcel),
            ".ppt" or ".pptx" or ".odp" => new OfficeApplication(PowerPointProgId, ConvertPowerPoint),
            _ => null
        };
    }

    private static Type? TryGetComType(string progId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return Type.GetTypeFromProgID(progId);
        }
        catch (COMException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string FormatAvailability(string progId) => TryGetComType(progId) is null ? "Not found" : "Available";

    private static string CreateOfficeTempPdfPath()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PdfRightClickSuite", "Office");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $"{Guid.NewGuid():N}.pdf");
    }

    private static void RunOnStaThread(Action action, TimeSpan timeout, CancellationToken cancellationToken, Action onAbandoned)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Microsoft Office conversion is only supported on Windows.");
        }

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ExceptionDispatchInfo? captured = null;
        var abandoned = 0;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                completed.TrySetResult();
                if (Volatile.Read(ref abandoned) != 0)
                {
                    try
                    {
                        onAbandoned();
                    }
                    catch (Exception ex)
                    {
                        Trace.TraceWarning($"Microsoft Office abandoned-conversion cleanup failed: {ex.Message}");
                    }
                }
            }
        })
        {
            IsBackground = true,
            Name = "PdfRightClickSuite Office PDF conversion"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            if (!completed.Task.Wait(timeout, cancellationToken))
            {
                Interlocked.Exchange(ref abandoned, 1);
                throw new TimeoutException($"Microsoft Office conversion exceeded {timeout.TotalMinutes:0} minutes.");
            }
        }
        catch (OperationCanceledException)
        {
            Interlocked.Exchange(ref abandoned, 1);
            throw;
        }

        captured?.Throw();
    }

    private static void ConvertWord(Type applicationType, string inputPath, string tempPdf)
    {
        object? wordObject = null;
        object? documentsObject = null;
        object? documentObject = null;

        try
        {
            wordObject = Activator.CreateInstance(applicationType)!;
            dynamic word = wordObject;
            word.Visible = false;
            word.DisplayAlerts = 0;

            documentsObject = word.Documents;
            dynamic documents = documentsObject;
            documentObject = documents.Open(FileName: inputPath, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false);
            dynamic document = documentObject;
            document.ExportAsFixedFormat(OutputFileName: tempPdf, ExportFormat: 17, OpenAfterExport: false);
        }
        finally
        {
            CloseComObject(documentObject, value =>
            {
                dynamic document = value;
                document.Close(SaveChanges: 0);
            });
            ReleaseComObject(documentsObject);
            CloseComObject(wordObject, value =>
            {
                dynamic word = value;
                word.Quit(SaveChanges: 0);
            });
        }
    }

    private static void ConvertExcel(Type applicationType, string inputPath, string tempPdf)
    {
        object? excelObject = null;
        object? workbooksObject = null;
        object? workbookObject = null;

        try
        {
            excelObject = Activator.CreateInstance(applicationType)!;
            dynamic excel = excelObject;
            excel.Visible = false;
            excel.DisplayAlerts = false;

            workbooksObject = excel.Workbooks;
            dynamic workbooks = workbooksObject;
            workbookObject = workbooks.Open(Filename: inputPath, UpdateLinks: 0, ReadOnly: true);
            dynamic workbook = workbookObject;
            workbook.ExportAsFixedFormat(Type: 0, Filename: tempPdf);
        }
        finally
        {
            CloseComObject(workbookObject, value =>
            {
                dynamic workbook = value;
                workbook.Close(SaveChanges: false);
            });
            ReleaseComObject(workbooksObject);
            CloseComObject(excelObject, value =>
            {
                dynamic excel = value;
                excel.Quit();
            });
        }
    }

    private static void ConvertPowerPoint(Type applicationType, string inputPath, string tempPdf)
    {
        object? powerPointObject = null;
        object? presentationsObject = null;
        object? presentationObject = null;

        try
        {
            powerPointObject = Activator.CreateInstance(applicationType)!;
            dynamic powerPoint = powerPointObject;
            powerPoint.DisplayAlerts = 0;

            presentationsObject = powerPoint.Presentations;
            dynamic presentations = presentationsObject;
            presentationObject = presentations.Open(inputPath, -1, 0, 0);
            dynamic presentation = presentationObject;
            presentation.SaveAs(tempPdf, 32);
        }
        finally
        {
            CloseComObject(presentationObject, value =>
            {
                dynamic presentation = value;
                presentation.Close();
            });
            ReleaseComObject(presentationsObject);
            CloseComObject(powerPointObject, value =>
            {
                dynamic powerPoint = value;
                powerPoint.Quit();
            });
        }
    }

    private static void CloseComObject(object? value, Action<object> close)
    {
        if (value is null)
        {
            return;
        }

        try
        {
            close(value);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            ReleaseComObject(value);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (Marshal.IsComObject(value))
            {
                Marshal.FinalReleaseComObject(value);
            }
        }
        catch (ArgumentException ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private sealed record OfficeApplication(string ProgId, Action<Type, string, string> Convert);
}
