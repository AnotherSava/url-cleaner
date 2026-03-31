using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

public class PathConverterTests
{
    // ── Drive-letter paths ────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Users\foo", "C:/Users/foo")]
    [InlineData(@"D:\projects\url-cleaner\src", "D:/projects/url-cleaner/src")]
    [InlineData(@"E:\", "E:/")]
    public void Converts_DriveLetterPaths(string input, string expected)
    {
        Assert.Equal(expected, PathConverter.TryConvert(input));
    }

    // ── Relative paths ────────────────────────────────────────────────

    [Theory]
    [InlineData(@"src\components\App.tsx", "src/components/App.tsx")]
    [InlineData(@"folder\subfolder\file.txt", "folder/subfolder/file.txt")]
    public void Converts_RelativePaths(string input, string expected)
    {
        Assert.Equal(expected, PathConverter.TryConvert(input));
    }

    // ── UNC paths → null ──────────────────────────────────────────────

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\192.168.1.1\files\doc.txt")]
    public void ReturnsNull_ForUncPaths(string input)
    {
        Assert.Null(PathConverter.TryConvert(input));
    }

    // ── URLs → null ───────────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path")]
    [InlineData("https://example.com/foo\\bar")]
    public void ReturnsNull_ForUrls(string input)
    {
        Assert.Null(PathConverter.TryConvert(input));
    }

    // ── Plain text without backslashes → null ─────────────────────────

    [Theory]
    [InlineData("hello world")]
    [InlineData("just some text")]
    [InlineData("src/components/App.tsx")]
    public void ReturnsNull_ForTextWithoutBackslashes(string input)
    {
        Assert.Null(PathConverter.TryConvert(input));
    }

    // ── Multiline text → null ─────────────────────────────────────────

    [Theory]
    [InlineData("C:\\Users\\foo\nC:\\Users\\bar")]
    [InlineData("line1\r\nline2")]
    public void ReturnsNull_ForMultilineText(string input)
    {
        Assert.Null(PathConverter.TryConvert(input));
    }

    // ── Already-forward-slash paths → null ────────────────────────────

    [Theory]
    [InlineData("C:/Users/foo")]
    [InlineData("src/components/App.tsx")]
    public void ReturnsNull_ForForwardSlashPaths(string input)
    {
        Assert.Null(PathConverter.TryConvert(input));
    }

    // ── Whitespace trimming ───────────────────────────────────────────

    [Theory]
    [InlineData("  C:\\foo  ", "C:/foo")]
    [InlineData("\tD:\\bar\t", "D:/bar")]
    public void Trims_WhitespaceBeforeConverting(string input, string expected)
    {
        Assert.Equal(expected, PathConverter.TryConvert(input));
    }

    // ── Null and empty input → null ───────────────────────────────────

    [Fact]
    public void ReturnsNull_ForNullInput()
    {
        Assert.Null(PathConverter.TryConvert(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsNull_ForEmptyOrWhitespaceInput(string input)
    {
        Assert.Null(PathConverter.TryConvert(input));
    }

    // ── Trailing backslash ─────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Users\foo\", "C:/Users/foo/")]
    [InlineData(@"src\components\", "src/components/")]
    public void Converts_PathsWithTrailingBackslash(string input, string expected)
    {
        Assert.Equal(expected, PathConverter.TryConvert(input));
    }

    // ── Consecutive backslashes (empty segments) → null ────────────────

    [Theory]
    [InlineData(@"C:\foo\\bar")]
    [InlineData(@"foo\\bar")]
    public void ReturnsNull_ForConsecutiveBackslashes(string input)
    {
        Assert.Null(PathConverter.TryConvert(input));
    }

    // ── Invalid segment characters → null ─────────────────────────────

    [Theory]
    [InlineData("C:\\foo<bar")]
    [InlineData("C:\\foo>bar")]
    [InlineData("C:\\foo:bar")]
    [InlineData("C:\\foo\"bar")]
    [InlineData("C:\\foo|bar")]
    [InlineData("C:\\foo?bar")]
    [InlineData("C:\\foo*bar")]
    public void ReturnsNull_ForInvalidSegmentCharacters(string input)
    {
        Assert.Null(PathConverter.TryConvert(input));
    }

    // ── Invalid segment characters in relative paths → null ────────────

    [Theory]
    [InlineData("src\\foo<bar")]
    [InlineData("src\\foo|bar")]
    [InlineData("src\\foo:bar")]
    public void ReturnsNull_ForInvalidSegmentCharactersInRelativePaths(string input)
    {
        Assert.Null(PathConverter.TryConvert(input));
    }
}
