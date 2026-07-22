using PdfRightClickSuite.Core;

namespace PdfRightClickSuite.Tests;

public sealed class PageRangeParserTests
{
    [Theory]
    [InlineData("1", 10, new[] { 1 })]
    [InlineData("1,3,5", 10, new[] { 1, 3, 5 })]
    [InlineData("2-6", 10, new[] { 2, 3, 4, 5, 6 })]
    [InlineData("1,3-5,8", 10, new[] { 1, 3, 4, 5, 8 })]
    [InlineData("1, 3 - 5, 8", 10, new[] { 1, 3, 4, 5, 8 })]
    public void Parse_accepts_valid_page_expressions(string expression, int pageCount, int[] expected)
    {
        var result = PageRangeParser.Parse(expression, pageCount);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(expected, result.Pages);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1,,2")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("3-")]
    [InlineData("7-2")]
    [InlineData("1.5")]
    [InlineData("99")]
    public void Parse_rejects_invalid_page_expressions(string expression)
    {
        var result = PageRangeParser.Parse(expression, pageCount: 8);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Pages);
    }

    [Fact]
    public void Parse_deduplicates_pages_while_preserving_first_occurrence()
    {
        var result = PageRangeParser.Parse("1,3,1,2-3", pageCount: 5);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(new[] { 1, 3, 2 }, result.Pages);
    }
}
