using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

public class NumberConverterTests
{
    // ── Comma-grouped numbers ─────────────────────────────────────────

    [Theory]
    [InlineData("10,871.69", "10871.69")]
    [InlineData("1,000", "1000")]
    [InlineData("1,234,567", "1234567")]
    [InlineData("-1,234,567.89", "-1234567.89")]
    public void Converts_CommaGroupedNumbers(string input, string expected)
    {
        Assert.Equal(expected, NumberConverter.TryConvert(input));
    }

    [Theory]
    [InlineData("  1,000  ", "1000")]
    [InlineData("10,871.69\r\n", "10871.69")]
    public void Trims_WhitespaceBeforeConverting(string input, string expected)
    {
        Assert.Equal(expected, NumberConverter.TryConvert(input));
    }

    // ── European decimals and badly grouped numbers → null ────────────

    [Theory]
    [InlineData("10,5")]
    [InlineData("1,00")]
    [InlineData("1,0000")]
    [InlineData("1234,567")]
    [InlineData(",123")]
    [InlineData("1,234.")]
    public void ReturnsNull_ForImproperGrouping(string input)
    {
        Assert.Null(NumberConverter.TryConvert(input));
    }

    // ── Nothing to strip → null ───────────────────────────────────────

    [Theory]
    [InlineData("1000")]
    [InlineData("10871.69")]
    [InlineData("")]
    public void ReturnsNull_WhenNothingChanges(string input)
    {
        Assert.Null(NumberConverter.TryConvert(input));
    }

    // ── Numbers inside other text → null ──────────────────────────────

    [Theory]
    [InlineData("Total: 1,000")]
    [InlineData("1,000 items")]
    [InlineData("1,000 and 2,000")]
    [InlineData("$1,000")]
    public void ReturnsNull_WhenNotWholeValue(string input)
    {
        Assert.Null(NumberConverter.TryConvert(input));
    }
}
