using System.Diagnostics;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfRightClickSuite.Core;

public sealed class RequestFileService
{
    public const long MaxRequestBytes = 1_048_576;
    public const int MaxSelectedFiles = 2_048;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public RequestFileService(string? shellRequestDirectory = null)
    {
        ShellRequestDirectory = Path.GetFullPath(
            shellRequestDirectory ?? Path.Combine(Path.GetTempPath(), "PdfRightClickSuite"));
    }

    public string ShellRequestDirectory { get; }

    public void Write(string path, PdfRequest request)
    {
        Validate(request, path);

        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var json = JsonSerializer.Serialize(request, Options);
        if (Encoding.UTF8.GetByteCount(json) > MaxRequestBytes)
        {
            throw new InvalidDataException($"Request file exceeds the {MaxRequestBytes:N0}-byte limit: {path}");
        }

        File.WriteAllText(path, json);
    }

    public PdfRequest Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length == 0)
        {
            throw new InvalidDataException($"Request file is empty: {path}");
        }

        if (stream.Length > MaxRequestBytes)
        {
            throw new InvalidDataException($"Request file exceeds the {MaxRequestBytes:N0}-byte limit: {path}");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        PdfRequest request;
        try
        {
            request = JsonSerializer.Deserialize<PdfRequest>(reader.ReadToEnd(), Options)
                ?? throw new InvalidDataException($"Request file is invalid: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Request file is invalid: {path}", ex);
        }

        Validate(request, path);
        return request;
    }

    public PdfRequest ConsumeShellRequest(string path)
    {
        if (!IsShellRequestPath(path))
        {
            throw new ArgumentException("Only PdfRightClickSuite shell request files can be consumed.", nameof(path));
        }

        try
        {
            return Read(path);
        }
        finally
        {
            TryDelete(path);
        }
    }

    public bool IsShellRequestPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var parent = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);
            return string.Equals(parent, ShellRequestDirectory, StringComparison.OrdinalIgnoreCase)
                && fileName.StartsWith("request-", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public int CleanupStaleShellRequests(TimeSpan maximumAge)
    {
        if (maximumAge < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAge));
        }

        if (!Directory.Exists(ShellRequestDirectory))
        {
            return 0;
        }

        string[] paths;
        try
        {
            paths = Directory.GetFiles(ShellRequestDirectory, "request-*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.TraceWarning($"Could not enumerate stale shell requests: {ex.Message}");
            return 0;
        }

        var cutoff = DateTime.UtcNow - maximumAge;
        var deleted = 0;
        foreach (var path in paths)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) <= cutoff)
                {
                    File.Delete(path);
                    deleted++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                Trace.TraceWarning($"Could not delete stale shell request '{path}': {ex.Message}");
            }
        }

        return deleted;
    }

    private static void Validate(PdfRequest request, string path)
    {
        if (!Enum.IsDefined(request.Action))
        {
            throw new InvalidDataException($"Request file contains an unsupported action: {path}");
        }

        if (request.SelectedFiles is null || request.SelectedFiles.Count == 0)
        {
            throw new InvalidDataException($"Request file does not contain any selected files: {path}");
        }

        if (request.SelectedFiles.Count > MaxSelectedFiles)
        {
            throw new InvalidDataException($"Request file exceeds the {MaxSelectedFiles:N0}-file limit: {path}");
        }

        if (request.SelectedFiles.Any(static file => string.IsNullOrWhiteSpace(file)))
        {
            throw new InvalidDataException($"Request file contains an empty selected-file path: {path}");
        }

        if (string.IsNullOrWhiteSpace(request.RequestId) || request.RequestId.Length > 128)
        {
            throw new InvalidDataException($"Request file contains an invalid request identifier: {path}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.TraceWarning($"Could not delete consumed shell request '{path}': {ex.Message}");
        }
    }
}
