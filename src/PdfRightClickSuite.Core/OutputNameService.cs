namespace PdfRightClickSuite.Core;

public sealed class OutputNameService(IClock? clock = null)
{
    private readonly IClock _clock = clock ?? new SystemClock();

    public string GetMergeOutputPath(string folder)
        => GetUniqueFilePath(folder, $"Merged_{Timestamp()}", ".pdf");

    public string GetConvertMergedOutputPath(string folder)
        => GetUniqueFilePath(folder, $"Converted_Merged_{Timestamp()}", ".pdf");

    public string GetConvertSingleOutputPath(string sourcePath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Directory.GetCurrentDirectory();
        return GetUniqueFilePath(folder, Path.GetFileNameWithoutExtension(sourcePath), ".pdf");
    }

    public string GetScanOutputPath(string sourcePdf)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(sourcePdf)) ?? Directory.GetCurrentDirectory();
        return GetUniqueFilePath(folder, $"{Path.GetFileNameWithoutExtension(sourcePdf)}_scanned", ".pdf");
    }

    public string GetColoredScanOutputPath(string sourcePdf)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(sourcePdf)) ?? Directory.GetCurrentDirectory();
        return GetUniqueFilePath(folder, $"{Path.GetFileNameWithoutExtension(sourcePdf)}_scanned_colored", ".pdf");
    }

    public string GetSplitOutputFolder(string sourcePdf)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(sourcePdf)) ?? Directory.GetCurrentDirectory();
        return GetUniqueFolderPath(folder, $"{Path.GetFileNameWithoutExtension(sourcePdf)}_split");
    }

    public string GetSplitPageOutputPath(string outputFolder, string sourcePdf, int pageNumber)
        => GetUniqueFilePath(outputFolder, $"{Path.GetFileNameWithoutExtension(sourcePdf)}_p{pageNumber:000}", ".pdf");

    public string GetUniqueFilePath(string folder, string stem, string extension)
    {
        Directory.CreateDirectory(folder);
        var candidate = Path.Combine(folder, $"{stem}{extension}");
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 1; i < int.MaxValue; i++)
        {
            candidate = Path.Combine(folder, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not create a unique file name in {folder}.");
    }

    public string GetUniqueFolderPath(string parentFolder, string folderName)
    {
        Directory.CreateDirectory(parentFolder);
        var candidate = Path.Combine(parentFolder, folderName);
        if (!Directory.Exists(candidate) && !File.Exists(candidate))
        {
            return candidate;
        }

        for (var i = 1; i < int.MaxValue; i++)
        {
            candidate = Path.Combine(parentFolder, $"{folderName} ({i})");
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not create a unique folder name in {parentFolder}.");
    }

    private string Timestamp() => _clock.Now.DateTime.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
}
