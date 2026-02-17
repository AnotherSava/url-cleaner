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

    /// <summary>
    /// Config with a single site rule. Named optional params keep call sites short.
    /// </summary>
    private static AppConfig RuleConfig(
        string domain,
        string[]? trackingParams = null,
        string[]? additionalParams = null,
        string[]? excludedParams = null,
        bool enabled = true,
        bool stripAllParams = false,
        string[]? keepPathFrom = null,
        string[]? stripPathSegments = null,
        bool stripSlugs = false,
        bool stripFragment = false) => new()
    {
        TrackingParams = trackingParams != null
            ? [new TrackingParamGroup { Params = [..trackingParams] }]
            : [],
        SiteRules =
        [
            new SiteRule
            {
                Suffix = [domain],
                Enabled = enabled,
                AdditionalParams = additionalParams != null ? [..additionalParams] : [],
                ExcludedParams = excludedParams != null ? [..excludedParams] : [],
                StripAllParams = stripAllParams,
                KeepPathFrom = keepPathFrom != null ? [..keepPathFrom] : [],
                StripPathSegments = stripPathSegments != null ? [..stripPathSegments] : [],
                StripSlugs = stripSlugs,
                StripFragment = stripFragment
            }
        ]
    };

    private static void AssertClean(string input, string? expected, AppConfig config)
    {
        Assert.Equal(expected, UrlSanitizer.TryClean(input, config));
    }

    // ── 1. Returns null when nothing to clean ────────────────────────

    [Theory]
    [InlineData("hello world")]
    [InlineData("not a url at all")]
    public void ReturnsNull_WhenNotAUrl(string input)
    {
        AssertClean(input, null, SimpleConfig("utm_source"));
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("mailto:user@example.com")]
    public void ReturnsNull_WhenNonHttpScheme(string input)
    {
        AssertClean(input, null, SimpleConfig("utm_source"));
    }

    [Theory]
    [InlineData("https://example.com?foo=bar")]
    [InlineData("https://example.com/page")]
    [InlineData("https://example.com")]
    public void ReturnsNull_WhenNoTrackingParams(string input)
    {
        AssertClean(input, null, SimpleConfig("utm_source"));
    }

    // ── 2. Disabled site rule ────────────────────────────────────────

    [Fact]
    public void ReturnsNull_WhenSiteRuleDisabled()
    {
        var config = RuleConfig("example.com", trackingParams: ["utm_source"], enabled: false);
        AssertClean("https://example.com?utm_source=x", null, config);
    }

    // ── 3. Strip global tracking params ──────────────────────────────

    [Theory]
    [InlineData("https://example.com?utm_source=x", "https://example.com")]
    [InlineData("https://example.com?v=abc&utm_source=x", "https://example.com?v=abc")]
    [InlineData("https://example.com?utm_source=x&fbclid=y", "https://example.com")]
    public void StripsGlobalTrackingParams(string input, string expected)
    {
        AssertClean(input, expected, SimpleConfig("utm_source", "fbclid"));
    }

    // ── 4. Additional params from site rule ──────────────────────────

    [Fact]
    public void StripsAdditionalParams()
    {
        var config = RuleConfig("shop.com", trackingParams: ["utm_source"], additionalParams: ["ref_id"]);
        AssertClean("https://shop.com/item?ref_id=abc", "https://shop.com/item", config);
    }

    // ── 5. Excluded params ───────────────────────────────────────────

    [Fact]
    public void KeepsExcludedParams()
    {
        var config = RuleConfig("youtube.com", trackingParams: ["utm_source", "pp"], excludedParams: ["pp"]);
        AssertClean("https://youtube.com/watch?utm_source=x&pp=keep", "https://youtube.com/watch?pp=keep", config);
    }

    // ── 6. StripAllParams removes everything ─────────────────────────

    [Fact]
    public void StripAllParams_RemovesEverything()
    {
        var config = RuleConfig("amazon.com", stripAllParams: true);
        AssertClean("https://amazon.com/dp/B123?tag=abc&ref=sr", "https://amazon.com/dp/B123", config);
    }

    // ── 7. StripAllParams keeps excluded params ──────────────────────

    [Fact]
    public void StripAllParams_KeepsExcludedParams()
    {
        var config = RuleConfig("amazon.com", stripAllParams: true, excludedParams: ["variant"]);
        AssertClean("https://amazon.com/dp/B123?tag=abc&variant=blue", "https://amazon.com/dp/B123?variant=blue", config);
    }

    // ── 8. KeepPathFrom trims path before anchor ────────────────────

    [Fact]
    public void KeepPathFrom_TrimsBeforeAnchor()
    {
        var config = RuleConfig("amazon.com", keepPathFrom: ["dp"]);
        AssertClean("https://amazon.com/Enchanti-Removable-Magnetic/dp/B0DPKB2ZMF", "https://amazon.com/dp/B0DPKB2ZMF", config);
    }

    [Fact]
    public void KeepPathFrom_WorksWithGp()
    {
        var config = RuleConfig("amazon.com", keepPathFrom: ["dp", "gp"]);
        AssertClean("https://amazon.com/Some-Name/gp/product/B0DPKB2ZMF", "https://amazon.com/gp/product/B0DPKB2ZMF", config);
    }

    [Fact]
    public void KeepPathFrom_NoMatch_LeavesPathUnchanged()
    {
        var config = RuleConfig("example.com", trackingParams: ["utm_source"], keepPathFrom: ["dp"]);
        AssertClean("https://example.com/page/stuff?utm_source=x", "https://example.com/page/stuff", config);
    }

    [Fact]
    public void KeepPathFrom_AnchorAlreadyFirst()
    {
        var config = RuleConfig("amazon.com", trackingParams: ["utm_source"], keepPathFrom: ["dp"]);
        AssertClean("https://amazon.com/dp/B123?utm_source=x", "https://amazon.com/dp/B123", config);
    }

    [Fact]
    public void KeepPathFrom_ComposesWithStripPathSegments()
    {
        var config = RuleConfig("amazon.com", keepPathFrom: ["dp"], stripPathSegments: ["ref="]);
        AssertClean("https://amazon.com/Slug-Name/dp/B123/ref=sr_1_8", "https://amazon.com/dp/B123", config);
    }

    [Fact]
    public void KeepPathFrom_FullAmazonCleaning()
    {
        var config = RuleConfig("amazon.com", keepPathFrom: ["dp"], stripAllParams: true, stripPathSegments: ["ref="]);
        AssertClean("https://amazon.com/Enchanti-Removable-Magnetic/dp/B0DPKB2ZMF/ref=sr_1_8?tag=abc&camp=123", "https://amazon.com/dp/B0DPKB2ZMF", config);
    }

    [Fact]
    public void KeepPathFrom_EarliestAnchorWins()
    {
        var config = RuleConfig("amazon.com", keepPathFrom: ["dp", "gp"]);
        AssertClean("https://amazon.com/slug/gp/foo/dp/B123", "https://amazon.com/gp/foo/dp/B123", config);
    }

    // ── 9. StripPathSegments removes matching segments ───────────────

    [Theory]
    [InlineData("https://amazon.com/dp/B123/ref=sr_1_8?color=red", "https://amazon.com/dp/B123?color=red")]
    [InlineData("https://amazon.com/dp/B123/ref=sr_1_8", "https://amazon.com/dp/B123")]
    public void StripPathSegments_RemovesMatchingSegments(string input, string expected)
    {
        AssertClean(input, expected, RuleConfig("amazon.com", stripPathSegments: ["ref="]));
    }

    // ── 10. Combined path + query cleaning ───────────────────────────

    [Fact]
    public void CombinedPathAndQueryCleaning()
    {
        var config = RuleConfig("amazon.com", stripAllParams: true, stripPathSegments: ["ref="]);
        AssertClean("https://amazon.com/dp/B123/ref=sr_1_8?tag=abc&camp=123", "https://amazon.com/dp/B123", config);
    }

    // ── 11. Preserves fragment ───────────────────────────────────────

    [Fact]
    public void PreservesFragment()
    {
        AssertClean("https://example.com/page?utm_source=x#section", "https://example.com/page#section", SimpleConfig("utm_source"));
    }

    // ── 12. Trims whitespace ─────────────────────────────────────────

    [Fact]
    public void TrimsWhitespace()
    {
        AssertClean("  https://example.com?utm_source=x  ", "https://example.com", SimpleConfig("utm_source"));
    }

    // ── 13. StripSlugs removes SEO slugs from numeric-prefix segments ─

    [Fact]
    public void StripSlugs_MakerWorldFullUrl()
    {
        var config = RuleConfig("makerworld.com", stripSlugs: true, stripFragment: true);
        AssertClean("https://makerworld.com/en/models/2409726-travel-power-adapter-storage-box-12-socket-types#profileId-2642005", "https://makerworld.com/en/models/2409726", config);
    }

    [Theory]
    [InlineData("https://example.com/en/models/2409726-slug", "https://example.com/en/models/2409726")]
    [InlineData("https://example.com/models/2409726", null)]
    [InlineData("https://example.com/items/abc-123", null)]
    [InlineData("https://example.com/cat/12-foo/item/34-bar", "https://example.com/cat/12/item/34")]
    public void StripSlugs_BasicBehavior(string input, string? expected)
    {
        AssertClean(input, expected, RuleConfig("example.com", stripSlugs: true));
    }

    [Fact]
    public void StripSlugs_ComposesWithKeepPathFrom()
    {
        var config = RuleConfig("example.com", keepPathFrom: ["models"], stripSlugs: true);
        AssertClean("https://example.com/prefix/models/2409726-slug", "https://example.com/models/2409726", config);
    }

    // ── 14. StripFragment removes URL fragment ────────────────────────

    [Theory]
    [InlineData("https://example.com/page#section", "https://example.com/page")]
    [InlineData("https://example.com/page", null)]
    public void StripFragment_BasicBehavior(string input, string? expected)
    {
        AssertClean(input, expected, RuleConfig("example.com", stripFragment: true));
    }

    [Fact]
    public void StripFragment_ComposesWithQueryCleaning()
    {
        var config = RuleConfig("example.com", trackingParams: ["utm_source"], stripFragment: true);
        AssertClean("https://example.com/page?utm_source=x#frag", "https://example.com/page", config);
    }

    [Fact]
    public void StripFragment_FragmentOnlyChange()
    {
        AssertClean("https://site.com/page#track", "https://site.com/page", RuleConfig("site.com", stripFragment: true));
    }
}
