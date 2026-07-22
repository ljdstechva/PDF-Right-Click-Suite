using System.Diagnostics;
using System.Security;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfRightClickSuite.Core;

public sealed class PdfSplitService(PdfPageCountService? pageCountService = null, OutputNameService? outputNameService = null)
{
    private readonly OutputNameService _outputNameService = outputNameService ?? new OutputNameService();
    private readonly PdfPageCountService _pageCountService = pageCountService ?? new PdfPageCountService();

    public Task<IReadOnlyList<string>> SplitAsync(
        string sourcePdf,
        IReadOnlyList<int> pages,
        string outputFolder,
        CancellationToken cancellationToken,
        IProgress<int>? progress = null)
    {
        if (!File.Exists(sourcePdf))
        {
            throw new FileNotFoundException("Source PDF does not exist.", sourcePdf);
        }

        if (pages.Count == 0)
        {
            throw new ArgumentException("At least one page must be selected.", nameof(pages));
        }

        var pageCount = _pageCountService.GetPageCount(sourcePdf);
        var invalidPage = pages.FirstOrDefault(page => page < 1 || page > pageCount);
        if (invalidPage != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pages), $"Page {invalidPage} is outside the PDF page count of {pageCount}.");
        }

        var outputFolderExisted = Directory.Exists(outputFolder);
        Directory.CreateDirectory(outputFolder);
        var outputs = new List<string>();
        string? pendingTempPath = null;
        var completed = false;
        try
        {
            using var input = PdfReader.Open(sourcePdf, PdfDocumentOpenMode.Import);
            for (var i = 0; i < pages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pageNumber = pages[i];
                var outputPath = _outputNameService.GetSplitPageOutputPath(outputFolder, sourcePdf, pageNumber);
                pendingTempPath = AtomicFileWriter.CreateTempPathBeside(outputPath);
                using (var output = new PdfDocument())
                {
                    output.AddPage(input.Pages[pageNumber - 1]);
                    output.Save(pendingTempPath);
                }

                cancellationToken.ThrowIfCancellationRequested();
                AtomicFileWriter.MoveIntoPlace(pendingTempPath, outputPath);
                pendingTempPath = null;
                outputs.Add(outputPath);
                progress?.Report(i + 1);
            }

            completed = true;
            return Task.FromResult<IReadOnlyList<string>>(outputs);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PdfProcessingException("Split failed. The PDF may be encrypted, corrupt, locked, or inaccessible.", ex);
        }
        finally
        {
            AtomicFileWriter.TryDelete(pendingTempPath);
            if (!completed)
            {
                foreach (var output in outputs)
                {
                    AtomicFileWriter.TryDelete(output);
                }

                if (!outputFolderExisted)
                {
                    TryDeleteEmptyDirectory(outputFolder);
                }
            }
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.TraceWarning($"Could not remove empty split output folder '{path}': {ex.Message}");
        }
    }
}
