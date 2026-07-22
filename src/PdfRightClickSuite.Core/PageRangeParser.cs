namespace PdfRightClickSuite.Core;

public static class PageRangeParser
{
    public static PageRangeParseResult Parse(string expression, int pageCount)
    {
        if (pageCount < 1)
        {
            return PageRangeParseResult.Failure("The PDF has no pages to select.");
        }

        if (string.IsNullOrWhiteSpace(expression))
        {
            return PageRangeParseResult.Failure("Enter page numbers, comma-separated pages, or ranges.");
        }

        var pages = new List<int>();
        var seen = new HashSet<int>();
        var parts = expression.Split(',');
        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            if (part.Length == 0)
            {
                return PageRangeParseResult.Failure("Empty page entries are not allowed.");
            }

            if (part.Contains('-', StringComparison.Ordinal))
            {
                var rangeParts = part.Split('-');
                if (rangeParts.Length != 2)
                {
                    return PageRangeParseResult.Failure($"Invalid range '{part}'.");
                }

                if (!TryParsePositivePage(rangeParts[0], out var start)
                    || !TryParsePositivePage(rangeParts[1], out var end))
                {
                    return PageRangeParseResult.Failure($"Invalid range '{part}'.");
                }

                if (end < start)
                {
                    return PageRangeParseResult.Failure($"Range '{part}' is reversed.");
                }

                for (var page = start; page <= end; page++)
                {
                    if (page > pageCount)
                    {
                        return PageRangeParseResult.Failure($"Page {page} is beyond the PDF page count of {pageCount}.");
                    }

                    AddPage(page, pages, seen);
                }
            }
            else
            {
                if (!TryParsePositivePage(part, out var page))
                {
                    return PageRangeParseResult.Failure($"Invalid page '{part}'.");
                }

                if (page > pageCount)
                {
                    return PageRangeParseResult.Failure($"Page {page} is beyond the PDF page count of {pageCount}.");
                }

                AddPage(page, pages, seen);
            }
        }

        return pages.Count == 0
            ? PageRangeParseResult.Failure("No valid pages were selected.")
            : PageRangeParseResult.Success(pages);
    }

    private static bool TryParsePositivePage(string value, out int page)
    {
        value = value.Trim();
        if (value.Length == 0 || value.Any(ch => !char.IsDigit(ch)))
        {
            page = 0;
            return false;
        }

        return int.TryParse(value, out page) && page > 0;
    }

    private static void AddPage(int page, List<int> pages, HashSet<int> seen)
    {
        if (seen.Add(page))
        {
            pages.Add(page);
        }
    }
}
