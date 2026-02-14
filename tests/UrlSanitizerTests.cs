using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

public class UrlSanitizerTests
{
    /// <summary>
    /// Minimal config with one tracking param for most tests.
    /// </summary>
    private static AppConfig SimpleConfig(params string[] trackingParams) => new()
    {
        TrackingParams =
        [
            new TrackingParamGroup { Params = [..trackingParams] }
        ]
    };

    // ── 1. Returns null when nothing to clean ────────────────────────

    [Theory]
    [InlineData("hello world")]
    [InlineData("not a url at all")]
    public void ReturnsNull_WhenNotAUrl(string input)
    {
        var config = SimpleConfig("utm_source");
        Assert.Null(UrlSanitizer.TryClean(input, config));
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("mailto:user@example.com")]
    public void ReturnsNull_WhenNonHttpScheme(string input)
    {
        var config = SimpleConfig("utm_source");
        Assert.Null(UrlSanitizer.TryClean(input, config));
    }

    [Theory]
    [InlineData("https://example.com?foo=bar")]
    [InlineData("https://example.com/page")]
    [InlineData("https://example.com")]
    public void ReturnsNull_WhenNoTrackingParams(string input)
    {
        var config = SimpleConfig("utm_source");
        Assert.Null(UrlSanitizer.TryClean(input, config));
    }

    // ── 2. Disabled site rule ────────────────────────────────────────

    [Fact]
    public void ReturnsNull_WhenSiteRuleDisabled()
    {
        var config = new AppConfig
        {
            TrackingParams = [new TrackingParamGroup { Params = ["utm_source"] }],
            SiteRules =
            [
                new SiteRule { Suffix = ["example.com"], Enabled = false }
            ]
        };
        Assert.Null(UrlSanitizer.TryClean("https://example.com?utm_source=x", config));
    }

    // ── 3. Strip global tracking params ──────────────────────────────

    [Theory]
    [InlineData("https://example.com?utm_source=x", "https://example.com")]
    [InlineData("https://example.com?v=abc&utm_source=x", "https://example.com?v=abc")]
    [InlineData("https://example.com?utm_source=x&fbclid=y", "https://example.com")]
    public void StripsGlobalTrackingParams(string input, string expected)
    {
        var config = SimpleConfig("utm_source", "fbclid");
        Assert.Equal(expected, UrlSanitizer.TryClean(input, config));
    }

    // ── 4. Additional params from site rule ──────────────────────────

    [Fact]
    public void StripsAdditionalParams()
    {
        var config = new AppConfig
        {
            TrackingParams = [new TrackingParamGroup { Params = ["utm_source"] }],
            SiteRules =
            [
                new SiteRule
                {
                    Suffix = ["shop.com"],
                    AdditionalParams = ["ref_id"]
                }
            ]
        };
        Assert.Equal(
            "https://shop.com/item",
            UrlSanitizer.TryClean("https://shop.com/item?ref_id=abc", config));
    }

    // ── 5. Excluded params ───────────────────────────────────────────

    [Fact]
    public void KeepsExcludedParams()
    {
        var config = new AppConfig
        {
            TrackingParams = [new TrackingParamGroup { Params = ["utm_source", "pp"] }],
            SiteRules =
            [
                new SiteRule
                {
                    Suffix = ["youtube.com"],
                    ExcludedParams = ["pp"]
                }
            ]
        };
        Assert.Equal(
            "https://youtube.com/watch?pp=keep",
            UrlSanitizer.TryClean("https://youtube.com/watch?utm_source=x&pp=keep", config));
    }

    // ── 6. StripAllParams removes everything ─────────────────────────

    [Fact]
    public void StripAllParams_RemovesEverything()
    {
        var config = new AppConfig
        {
            SiteRules =
            [
                new SiteRule
                {
                    Suffix = ["amazon.com"],
                    StripAllParams = true
                }
            ]
        };
        Assert.Equal(
            "https://amazon.com/dp/B123",
            UrlSanitizer.TryClean("https://amazon.com/dp/B123?tag=abc&ref=sr", config));
    }

    // ── 7. StripAllParams keeps excluded params ──────────────────────

    [Fact]
    public void StripAllParams_KeepsExcludedParams()
    {
        var config = new AppConfig
        {
            SiteRules =
            [
                new SiteRule
                {
                    Suffix = ["amazon.com"],
                    StripAllParams = true,
                    ExcludedParams = ["variant"]
                }
            ]
        };
        Assert.Equal(
            "https://amazon.com/dp/B123?variant=blue",
            UrlSanitizer.TryClean("https://amazon.com/dp/B123?tag=abc&variant=blue", config));
    }

    // ── 8. StripPathSegments removes matching segments ───────────────

    [Fact]
    public void StripPathSegments_RemovesMatchingSegments()
    {
        var config = new AppConfig
        {
            TrackingParams = [new TrackingParamGroup { Params = ["utm_source"] }],
            SiteRules =
            [
                new SiteRule
                {
                    Suffix = ["amazon.com"],
                    StripPathSegments = ["ref="]
                }
            ]
        };
        Assert.Equal(
            "https://amazon.com/dp/B123?color=red",
            UrlSanitizer.TryClean("https://amazon.com/dp/B123/ref=sr_1_8?color=red", config));
    }

    // ── 9. StripPathSegments with no query string ────────────────────

    [Fact]
    public void StripPathSegments_NoQueryString()
    {
        var config = new AppConfig
        {
            SiteRules =
            [
                new SiteRule
                {
                    Suffix = ["amazon.com"],
                    StripPathSegments = ["ref="]
                }
            ]
        };
        Assert.Equal(
            "https://amazon.com/dp/B123",
            UrlSanitizer.TryClean("https://amazon.com/dp/B123/ref=sr_1_8", config));
    }

    // ── 10. Combined path + query cleaning ───────────────────────────

    [Fact]
    public void CombinedPathAndQueryCleaning()
    {
        var config = new AppConfig
        {
            SiteRules =
            [
                new SiteRule
                {
                    Suffix = ["amazon.com"],
                    StripAllParams = true,
                    StripPathSegments = ["ref="]
                }
            ]
        };
        Assert.Equal(
            "https://amazon.com/dp/B123",
            UrlSanitizer.TryClean("https://amazon.com/dp/B123/ref=sr_1_8?tag=abc&camp=123", config));
    }

    // ── 11. Preserves fragment ───────────────────────────────────────

    [Fact]
    public void PreservesFragment()
    {
        var config = SimpleConfig("utm_source");
        Assert.Equal(
            "https://example.com/page#section",
            UrlSanitizer.TryClean("https://example.com/page?utm_source=x#section", config));
    }

    // ── 12. Trims whitespace ─────────────────────────────────────────

    [Fact]
    public void TrimsWhitespace()
    {
        var config = SimpleConfig("utm_source");
        Assert.Equal(
            "https://example.com",
            UrlSanitizer.TryClean("  https://example.com?utm_source=x  ", config));
    }
}
