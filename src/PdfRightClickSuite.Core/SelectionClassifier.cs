namespace PdfRightClickSuite.Core;

public sealed class SelectionClassifier
{
    public static readonly ISet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".tif",
        ".tiff",
        ".webp"
    };

    public SelectionClassification Classify(IEnumerable<string> selectedPaths)
    {
        var paths = selectedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToArray();

        if (paths.Length == 0)
        {
            return Disallow("No files were selected.", []);
        }

        if (paths.Any(Directory.Exists))
        {
            return Disallow("A selected path is a directory. Directories are not supported by PdfRightClickSuite.", []);
        }

        var missing = paths.FirstOrDefault(path => !File.Exists(path));
        if (missing is not null)
        {
            return Disallow($"The selected path is not a file: {missing}", []);
        }

        var files = paths
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new SelectedFileInfo(
                    info.FullName,
                    info.Name,
                    info.DirectoryName ?? string.Empty,
                    info.Extension.ToLowerInvariant(),
                    info.Length);
            })
            .ToArray();

        var pdfCount = files.Count(file => IsPdf(file.Extension));
        var allPdf = pdfCount == files.Length;
        var allNonPdf = pdfCount == 0;
        var canMerge = files.Length >= 2 && allPdf;
        var canSplit = files.Length == 1 && allPdf;
        var canScan = files.Length == 1 && allPdf;
        var canConvert = allNonPdf && IsConvertibleSelection(files);

        var reason = "Selection is supported.";
        if (!canMerge && !canSplit && !canScan && !canConvert)
        {
            reason = BuildReason(files, pdfCount);
        }

        return new SelectionClassification(canMerge, canSplit, canConvert, canScan, reason, files);
    }

    private static SelectionClassification Disallow(string reason, IReadOnlyList<SelectedFileInfo> files)
        => new(false, false, false, false, reason, files);

    private static bool IsPdf(string extension) => string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsConvertibleSelection(IReadOnlyList<SelectedFileInfo> files)
    {
        if (files.Count == 1)
        {
            return true;
        }

        var firstExtension = files[0].Extension;
        return files.All(file => string.Equals(file.Extension, firstExtension, StringComparison.OrdinalIgnoreCase))
            || files.All(file => ImageExtensions.Contains(file.Extension));
    }

    private static string BuildReason(IReadOnlyList<SelectedFileInfo> files, int pdfCount)
    {
        if (pdfCount > 0 && pdfCount < files.Count)
        {
            return "PDF files cannot be mixed with non-PDF files.";
        }

        if (pdfCount == 0 && files.Count > 1)
        {
            return "Multiple non-PDF files must be similar: the same extension or all image-family files.";
        }

        return "The selected files do not match a PDF command.";
    }
}
