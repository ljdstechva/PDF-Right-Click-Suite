using System.Diagnostics;

namespace PdfRightClickSuite.Core;

public sealed class LoggerService
{
    private readonly string _logFolder;

    public LoggerService(string? logFolder = null)
    {
        _logFolder = logFolder
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PdfRightClickSuite", "logs");
    }

    public bool Info(string message) => Write("INFO", message);

    public bool Error(Exception exception, string context) => Write("ERROR", $"{context}{Environment.NewLine}{exception}");

    private bool Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(_logFolder);
            var path = Path.Combine(_logFolder, $"{DateTimeOffset.Now:yyyyMMdd}.log");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceError($"PdfRightClickSuite logging failed: {ex.Message}");
            return false;
        }
    }
}
