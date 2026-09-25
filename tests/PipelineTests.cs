using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

public class PipelineTests
{
    private static readonly IReadOnlyList<string> BuiltInIds = BuiltInModules.All.Select(m => m.Id).ToList();

    // Most recent first, so a single placeholder takes the first entry.
    private static readonly string[] History = [@"C:\Users", "older"];

    private static AppConfig AllEnabled(AppConfig source) => new()
    {
        TrimUrl = source.TrimUrl,
        TrackingParams = source.TrackingParams,
        SiteRules = source.SiteRules,
        ConvertPaths = true,
        ConvertNumbers = true,
        ConvertPlaceholders = true
    };

    private static AppConfig LoadDefaultConfig()
    {
        var path = Path.Combine(Path.GetTempPath(), $"url-cleaner-test-{Guid.NewGuid():N}.json");
        try
        {
            return AppConfig.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// For a run that calls no plugin, which never uses them.
    /// </summary>
    private sealed class NoHooks : IPipelineHooks
    {
        public bool TryBringConfirmForward(string pluginId, string text) => throw new InvalidOperationException();

        public void BeforeFirstPluginCall(string text, bool placeholderMatched) => throw new InvalidOperationException();
    }

    /// <summary>
    /// Runs the built-ins only, asserting the run completed synchronously as a run that calls no plugin must.
    /// </summary>
    private static PipelineResult Run(string text, AppConfig config, IReadOnlyList<PipelineEntry>? order = null)
    {
        var steps = Pipeline.ResolveOrder(order, BuiltInIds).Steps;
        var run = Pipeline.RunAsync(text, steps, new ModuleContext(config, History), new Dictionary<string, PluginManifest>(), new NoHooks());

        Assert.True(run.IsCompletedSuccessfully);
        Assert.False(run.Result.AwaitedPlugin);
        return run.Result;
    }

    /// <summary>
    /// The hard-coded first-match chain the pipeline replaces, kept here as the oracle for its fallback order.
    /// </summary>
    private static string? OriginalChain(string text, AppConfig config)
    {
        var cleaned = UrlSanitizer.TryClean(text, config);
        if (cleaned == null && config.ConvertPaths)
            cleaned = PathConverter.TryConvert(text);
        if (cleaned == null && config.ConvertNumbers)
            cleaned = NumberConverter.TryConvert(text);
        if (cleaned == null && config.ConvertPlaceholders)
            cleaned = PlaceholderConverter.TryConvert(text, History);
        return cleaned;
    }

    // ── Fallback order ────────────────────────────────────────────────

    [Theory]
    [InlineData("https://example.com/page?utm_source=news&id=1")]
    [InlineData("  https://example.com/page?utm_source=news  ")]
    [InlineData("https://example.com/plain")]
    [InlineData("https://www.youtube.com/watch?v=abc123&si=xyz&feature=share")]
    [InlineData("https://www.amazon.com/Some-Product/dp/B000123/ref=sr_1_8?keywords=x&qid=1")]
    [InlineData("ftp://example.com/file?utm_source=x")]
    [InlineData(@"C:\Users\foo")]
    [InlineData(@"src\components\App.tsx")]
    [InlineData(@"\\server\share")]
    [InlineData("10,871.69")]
    [InlineData("-1,234,567.89")]
    [InlineData("1,00")]
    [InlineData("{{name}}")]
    [InlineData("a {{x}} b {{y}}")]
    [InlineData(@"{{dir}}\sub")]
    [InlineData("plain text")]
    [InlineData("")]
    public void FallbackOrder_MatchesOriginalChain(string text)
    {
        var defaults = LoadDefaultConfig();
        foreach (var config in new[] { defaults, AllEnabled(defaults) })
        {
            var result = Run(text, config);
            Assert.Equal(OriginalChain(text, config), result.Changed ? result.Text : null);
        }
    }

    [Fact]
    public void ResolveOrder_WithoutList_IsFallbackOrderWithStopEverywhere()
    {
        var resolved = Pipeline.ResolveOrder(null, BuiltInIds);

        Assert.Equal(BuiltInIds, resolved.Steps.Select(s => s.Id));
        Assert.All(resolved.Steps, s => Assert.True(s.Stop));
        Assert.Empty(resolved.Problems);
    }

    // ── Configured order ──────────────────────────────────────────────

    [Fact]
    public void StopFalse_NextEntrySeesRewrittenText()
    {
        var order = new List<PipelineEntry>
        {
            new() { Id = "convertPlaceholders", Stop = false },
            new() { Id = "convertPaths" }
        };

        var result = Run(@"{{dir}}\sub", AllEnabled(new AppConfig()), order);

        Assert.Equal("C:/Users/sub", result.Text);
        Assert.True(result.PlaceholderMatched);
    }

    [Fact]
    public void OmittedStop_EndsTheRun()
    {
        var order = new List<PipelineEntry> { new() { Id = "convertPlaceholders" }, new() { Id = "convertPaths" } };

        var result = Run(@"{{dir}}\sub", AllEnabled(new AppConfig()), order);

        Assert.Equal(@"C:\Users\sub", result.Text);
    }

    [Fact]
    public void DisabledBuiltIn_IsSkipped()
    {
        var result = Run(@"C:\Users\foo", new AppConfig { ConvertPaths = false });

        Assert.False(result.Changed);
        Assert.Equal(@"C:\Users\foo", result.Text);
    }

    [Fact]
    public void UnlistedEntries_FollowInFallbackOrderWithStop()
    {
        var order = new List<PipelineEntry> { new() { Id = "convertNumbers", Stop = false } };

        var steps = Pipeline.ResolveOrder(order, BuiltInIds).Steps;

        Assert.Equal(["convertNumbers", "urlCleaner", "convertPaths", "convertPlaceholders"], steps.Select(s => s.Id));
        Assert.Equal([false, true, true, true], steps.Select(s => s.Stop));
    }

    [Fact]
    public void UnknownAndDuplicateIds_AreSkippedAndReported()
    {
        var order = new List<PipelineEntry>
        {
            new() { Id = "noSuchModule" },
            new() { Id = "convertPaths", Stop = false },
            new() { Id = "convertPaths" }
        };

        var resolved = Pipeline.ResolveOrder(order, BuiltInIds);

        Assert.Equal(["convertPaths", "urlCleaner", "convertNumbers", "convertPlaceholders"], resolved.Steps.Select(s => s.Id));
        Assert.False(resolved.Steps[0].Stop);
        Assert.Equal([new OrderProblem("noSuchModule", "no built-in or installed plugin has this id"), new OrderProblem("convertPaths", "listed more than once")], resolved.Problems);
    }
}
