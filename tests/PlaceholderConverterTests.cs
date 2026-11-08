using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

public class PlaceholderConverterTests
{
    // ── ContainsPlaceholder ───────────────────────────────────────────

    [Theory]
    [InlineData("key={{tvdb-api-key}}")]
    [InlineData("{{a}}")]
    [InlineData("{{step-2}} then {{step-3}}")]
    public void ContainsPlaceholder_True_ForKebabCaseLabels(string input)
    {
        Assert.True(PlaceholderConverter.ContainsPlaceholder(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no placeholder here")]
    [InlineData("{{TvdbKey}}")]
    [InlineData("{{snake_case}}")]
    [InlineData("{{-leading}}")]
    [InlineData("{{double--hyphen}}")]
    [InlineData("{{}}")]
    [InlineData("{single}")]
    public void ContainsPlaceholder_False_WithoutKebabCasePlaceholder(string? input)
    {
        Assert.False(PlaceholderConverter.ContainsPlaceholder(input));
    }

    // ── A single placeholder takes the most recent copy ───────────────

    [Fact]
    public void SinglePlaceholder_TakesMostRecentCopy()
    {
        Assert.Equal("key=newest", PlaceholderConverter.TryConvert("key={{api-key}}", ["newest", "older"]));
    }

    [Fact]
    public void RepeatedPlaceholder_GetsTheSameValueEverywhere()
    {
        Assert.Equal("a-x-a", PlaceholderConverter.TryConvert("{{v}}-x-{{v}}", ["a", "b"]));
    }

    // ── Several placeholders: first copied fills first appearing ──────

    [Fact]
    public void SeveralPlaceholders_FillInCopyOrder()
    {
        // History is most-recent first: "user" was copied before "pass".
        Assert.Equal("login user:pass", PlaceholderConverter.TryConvert("login {{name}}:{{secret}}", ["pass", "user", "unrelated"]));
    }

    [Fact]
    public void SeveralPlaceholders_RepeatsCountOnce()
    {
        Assert.Equal("1 2 1", PlaceholderConverter.TryConvert("{{a}} {{b}} {{a}}", ["2", "1"]));
    }

    [Fact]
    public void ShortHistory_LeavesEarliestPlaceholdersUnfilled()
    {
        Assert.Equal("{{first}} only", PlaceholderConverter.TryConvert("{{first}} {{second}}", ["only"]));
    }

    // ── Values are inserted literally ─────────────────────────────────

    [Fact]
    public void Value_IsInsertedLiterally()
    {
        Assert.Equal("price=$1 $0 {{x}}", PlaceholderConverter.TryConvert("price={{p}}", ["$1 $0 {{x}}"]));
    }

    // ── Nothing to fill → null ────────────────────────────────────────

    [Theory]
    [InlineData("no placeholder")]
    [InlineData("{{NotKebab}}")]
    [InlineData("")]
    public void ReturnsNull_WithoutPlaceholder(string input)
    {
        Assert.Null(PlaceholderConverter.TryConvert(input, ["value"]));
    }

    [Fact]
    public void ReturnsNull_WithEmptyHistory()
    {
        Assert.Null(PlaceholderConverter.TryConvert("key={{api-key}}", []));
    }

    [Fact]
    public void ReturnsNull_WhenValueEqualsPlaceholder()
    {
        Assert.Null(PlaceholderConverter.TryConvert("{{same}}", ["{{same}}"]));
    }
}
