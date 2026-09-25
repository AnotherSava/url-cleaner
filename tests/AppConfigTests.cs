using System.Text.Json;
using System.Text.Json.Serialization;
using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

public class AppConfigTests
{
    private static AppConfig LoadFrom(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"url-cleaner-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            return AppConfig.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PipelineBlock_IsOptional()
    {
        Assert.Null(LoadFrom("{}").Pipeline);
    }

    [Fact]
    public void PipelineEntry_OmittedStopMeansSet()
    {
        var config = LoadFrom("""{ "pipeline": { "order": [ { "id": "convertPaths" }, { "id": "urlCleaner", "stop": false } ] } }""");

        var order = config.Pipeline!.Order!;
        Assert.Equal("convertPaths", order[0].Id);
        Assert.True(order[0].Stop);
        Assert.False(order[1].Stop);
    }

    [Fact]
    public void UpdateConfigValue_WritesANestedValueAndLeavesTheRestAlone()
    {
        using var config = new TempConfig("""{ "convertPaths": true, "siteRules": [ { "suffix": "example.com", "stripFragment": true } ] }""");

        Assert.True(AppConfig.UpdateConfigValue(config.FilePath, ["plugins", "acme-notes", "enabled"], false));

        var reloaded = AppConfig.Load(config.FilePath);
        Assert.False(reloaded.IsPluginEnabled("acme-notes"));
        Assert.True(reloaded.IsPluginEnabled("other-plugin"));
        Assert.True(reloaded.ConvertPaths);
        Assert.True(Assert.Single(reloaded.SiteRules).StripFragment);
        Assert.Null(reloaded.Pipeline);
    }

    [Fact]
    public void UpdateConfigValue_RefusesToReplaceANonObject()
    {
        using var config = new TempConfig("""{ "plugins": 5 }""");

        Assert.False(AppConfig.UpdateConfigValue(config.FilePath, ["plugins", "acme-notes", "enabled"], false));
        Assert.Equal("""{ "plugins": 5 }""", File.ReadAllText(config.FilePath));
    }

    [Theory]
    [InlineData("""{ "pipeline": { "order": [ { "id": "urlCleaner" }, null ] } }""", "pipeline.order")]
    [InlineData("""{ "pipeline": { "order": [ { "stop": false } ] } }""", "pipeline.order")]
    [InlineData("""{ "trackingParams": null }""", "trackingParams")]
    [InlineData("""{ "trackingParams": [ null ] }""", "trackingParams")]
    [InlineData("""{ "trackingParams": [ { "params": [ "utm_source", null ] } ] }""", "trackingParams[].params")]
    [InlineData("""{ "siteRules": [ null ] }""", "siteRules")]
    [InlineData("""{ "siteRules": [ { "suffix": "example.com", "additionalParams": null } ] }""", "siteRules[].additionalParams")]
    public void NullListsAndEntries_AreRefusedNamingTheField(string json, string field)
    {
        var error = Assert.Throws<JsonException>(() => LoadFrom(json));

        Assert.Contains(field, error.Message);
    }

    [Fact]
    public void UpdateConfigValue_FailsCleanlyOnAPropertyNamedTwice()
    {
        using var config = new TempConfig("""{ "convertPaths": true, "convertPaths": false }""");

        Assert.False(AppConfig.UpdateConfigValue(config.FilePath, ["convertNumbers"], true));
    }

    [Fact]
    public void FalseValuesOfTrueDefaults_SurviveTheConfigWriter()
    {
        // The same ignore condition AppConfig writes config.json with.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };
        var config = new AppConfig
        {
            TrimUrl = false,
            SiteRules = [new SiteRule { Suffix = ["example.com"], Enabled = false }],
            Pipeline = new PipelineConfig { Order = [new PipelineEntry { Id = "urlCleaner", Stop = false }] }
        };

        var reloaded = LoadFrom(JsonSerializer.Serialize(config, options));

        Assert.False(reloaded.TrimUrl);
        Assert.False(reloaded.SiteRules[0].Enabled);
        Assert.False(reloaded.Pipeline!.Order![0].Stop);
    }
}
