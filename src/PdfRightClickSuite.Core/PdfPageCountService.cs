using PdfSharp.Pdf.IO;

namespace PdfRightClickSuite.Core;

public sealed class PdfPageCountService
{
    public int GetPageCount(string pdfPath)
    {
        try
        {
            using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
            return document.PageCount;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PdfReaderException)
        {
            throw new PdfProcessingException(
                $"Could not read '{pdfPath}'. The PDF may be encrypted, password-protected, corrupt, locked, or inaccessible.",
                ex);
        }
    }
}
