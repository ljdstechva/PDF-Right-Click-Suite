using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfRightClickSuite.Core;

public sealed class PdfMergeService(PdfPageCountService? pageCountService = null)
{
    private readonly PdfPageCountService _pageCountService = pageCountService ?? new PdfPageCountService();

    public Task MergeAsync(
        IReadOnlyList<string> inputFiles,
        string outputPath,
        CancellationToken cancellationToken,
        IProgress<int>? progress = null)
    {
        if (inputFiles.Count < 2)
        {
            throw new ArgumentException("Merge requires at least two PDF files.", nameof(inputFiles));
        }

        foreach (var file in inputFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidatePdfFile(file);
            _pageCountService.GetPageCount(file);
        }

        var tempPath = AtomicFileWriter.CreateTempPathBeside(outputPath);
        try
        {
            using var output = new PdfDocument();
            for (var i = 0; i < inputFiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var input = PdfReader.Open(inputFiles[i], PdfDocumentOpenMode.Import);
                foreach (var page in input.Pages)
                {
                    output.AddPage(page);
                }

                progress?.Report(i + 1);
            }

            cancellationToken.ThrowIfCancellationRequested();
            output.Save(tempPath);
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFileWriter.MoveIntoPlace(tempPath, outputPath);
            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PdfProcessingException("Merge failed. One or more PDFs may be encrypted, corrupt, locked, or inaccessible.", ex);
        }
        finally
        {
            AtomicFileWriter.TryDelete(tempPath);
        }
    }

    private static void ValidatePdfFile(string file)
    {
        if (!File.Exists(file))
        {
            throw new FileNotFoundException("Selected PDF does not exist.", file);
        }

        if (!string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Not a PDF file: {file}");
        }
    }
}
