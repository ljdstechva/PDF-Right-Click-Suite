using System.Diagnostics;
using System.Security;

namespace PdfRightClickSuite.Core;

internal static class AtomicFileWriter
{
    public static string CreateTempPathBeside(string finalPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(finalPath)) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, $".{Guid.NewGuid():N}.tmp");
    }

    public static void MoveIntoPlace(string tempPath, string finalPath)
    {
        if (File.Exists(finalPath))
        {
            throw new IOException($"Output already exists: {finalPath}");
        }

        File.Move(tempPath, finalPath);
    }

    public static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.TraceWarning($"Could not delete temporary file '{path}': {ex.Message}");
        }
    }
}
