using System.Text.Json;
using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

public sealed class PluginManifestTests : IDisposable
{
    private readonly TempPlugins _plugins = new();

    public void Dispose() => _plugins.Dispose();

    private string FolderOf(string id) => Path.Combine(_plugins.Root, id);

    private PluginManifest Parse(string json, string folder = "acme-notes") => PluginManifest.Parse(json, FolderOf(folder));

    private string ParseError(string json, string folder = "acme-notes") =>
        Assert.Throws<InvalidManifestException>(() => Parse(json, folder)).Message;

    [Fact]
    public void ValidManifest_IsRead()
    {
        var manifest = Parse("""{ "protocol": 1, "id": "acme-notes", "name": " Article notes ", "match": { "patterns": ["^a", "^b"] }, "run": ["node", "dist/plugin.js"], "timeoutSeconds": 30 }""");

        Assert.Equal("acme-notes", manifest.Id);
        Assert.Equal("Article notes", manifest.Name);
        Assert.Equal(2, manifest.Patterns.Count);
        Assert.Equal(["node", "dist/plugin.js"], manifest.Run);
        Assert.Equal(30, manifest.TimeoutSeconds);
        Assert.Equal(FolderOf("acme-notes"), manifest.Folder);
    }

    [Fact]
    public void TimeoutSeconds_DefaultsToTen()
    {
        Assert.Equal(PluginManifest.DefaultTimeoutSeconds, Parse(TempPlugins.Manifest("acme-notes")).TimeoutSeconds);
    }

    [Fact]
    public void HigherProtocol_IsRefusedNamingBothVersions()
    {
        var message = ParseError(TempPlugins.Manifest("acme-notes").Replace("\"protocol\": 1", "\"protocol\": 2"));

        Assert.Contains("protocol 2", message);
        Assert.Contains($"up to {PluginManifest.HostProtocol}", message);
    }

    [Theory]
    [InlineData("\"protocol\": 0", "protocol")]
    [InlineData("\"protocol\": 1, \"timeoutSeconds\": 0", "timeoutSeconds")]
    [InlineData("\"protocol\": 1, \"timeoutSeconds\": 301", "timeoutSeconds")]
    [InlineData("\"protocol\": 1, \"timeoutSeconds\": -1", "timeoutSeconds")]
    public void OutOfRangeNumbers_FailNamingTheField(string replacement, string field)
    {
        Assert.Contains(field, ParseError(TempPlugins.Manifest("acme-notes").Replace("\"protocol\": 1", replacement)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(300)]
    public void TimeoutSeconds_AcceptsTheRangeEnds(int seconds)
    {
        Assert.Equal(seconds, Parse(TempPlugins.Manifest("acme-notes", $", \"timeoutSeconds\": {seconds}")).TimeoutSeconds);
    }

    [Fact]
    public void FractionalTimeout_FailsNamingTheField()
    {
        var error = Assert.Throws<JsonException>(() => Parse(TempPlugins.Manifest("acme-notes", ", \"timeoutSeconds\": 2.5")));

        Assert.Contains("timeoutSeconds", error.Message);
    }

    [Theory]
    [InlineData("Acme-notes")]
    [InlineData("acme_notes")]
    [InlineData("acme--notes")]
    [InlineData("-acme")]
    public void IdMustBeKebabCase(string id)
    {
        Assert.Contains("kebab-case", ParseError(TempPlugins.Manifest(id), folder: id));
    }

    [Fact]
    public void IdMustEqualTheFolderName()
    {
        Assert.Contains("folder name", ParseError(TempPlugins.Manifest("acme-notes"), folder: "other-name"));
    }

    [Theory]
    [InlineData("\"name\": \"Test plugin\"", "\"name\": \"  \"", "name")]
    [InlineData("\"patterns\": [\"^https://example\\\\.com/\"]", "\"patterns\": []", "match.patterns")]
    [InlineData("\"patterns\": [\"^https://example\\\\.com/\"]", "\"patterns\": [\"(unclosed\"]", "doesn't compile")]
    [InlineData("\"run\": [\"node\", \"plugin.js\"]", "\"run\": []", "run")]
    [InlineData("\"run\": [\"node\", \"plugin.js\"]", "\"run\": [\"\"]", "run")]
    [InlineData("\"run\": [\"node\", \"plugin.js\"]", "\"run\": [\"tool.cmd\"]", "batch file")]
    [InlineData("\"run\": [\"node\", \"plugin.js\"]", "\"run\": [\"scripts\\\\tool.BAT\"]", "batch file")]
    [InlineData("\"run\": [\"node\", \"plugin.js\"]", "\"run\": [\"tool.cmd \"]", "batch file")]
    [InlineData("\"run\": [\"node\", \"plugin.js\"]", "\"run\": [\"tool.bat.\"]", "batch file")]
    [InlineData("\"run\": [\"node\", \"plugin.js\"]", "\"run\": [\"node\", null]", "null element")]
    [InlineData("\"run\": [\"node\", \"plugin.js\"]", "\"run\": [\"node\", \"a\\u0000b\"]", "NUL character")]
    public void BrokenFields_FailNamingTheField(string field, string replacement, string expected)
    {
        var json = TempPlugins.Manifest("acme-notes");
        Assert.Contains(field, json);

        Assert.Contains(expected, ParseError(json.Replace(field, replacement)));
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("https://example.com/a\r\n")]
    [InlineData("  https://example.com/a  ")]
    public void Matches_TrimmedText(string text)
    {
        var manifest = Parse(TempPlugins.Manifest("acme-notes").Replace("^https://example\\\\.com/", "^https://example\\\\.com/a$"));

        Assert.True(manifest.Matches(text));
    }

    [Fact]
    public void Matches_ReturnsFalseForOtherText()
    {
        Assert.False(Parse(TempPlugins.Manifest("acme-notes")).Matches("https://other.org/"));
    }

    [Fact]
    public void PatternThatRunsOutOfTime_CountsAsNoMatch()
    {
        // Nested quantifiers backtrack exponentially on a near-miss, far past the 100 ms limit.
        var manifest = Parse(TempPlugins.Manifest("acme-notes").Replace("^https://example\\\\.com/", "^(\\\\w+\\\\s?)*$"));

        Assert.False(manifest.Matches(new string('a', 40) + "!"));
    }

    [Fact]
    public void FindExecutable_ResolvesAPathRelativeToThePluginFolder()
    {
        var folder = FolderOf("acme-notes");
        Directory.CreateDirectory(Path.Combine(folder, "bin"));
        File.WriteAllText(Path.Combine(folder, "bin", "tool.exe"), "");
        var manifest = Parse(TempPlugins.Manifest("acme-notes").Replace("[\"node\", \"plugin.js\"]", "[\"bin/tool.exe\"]"));

        Assert.Equal(Path.Combine(folder, "bin", "tool.exe"), manifest.FindExecutable(pathVariable: null));
    }

    [Fact]
    public void FindExecutable_SearchesPathAndAddsExe()
    {
        var bin = Path.Combine(Path.GetDirectoryName(_plugins.Root)!, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "node.exe"), "");
        var manifest = Parse(TempPlugins.Manifest("acme-notes"));

        Assert.Equal(Path.Combine(bin, "node.exe"), manifest.FindExecutable($"{Path.Combine(bin, "missing")};{bin}"));
    }

    [Fact]
    public void FindExecutable_AcceptsAQuotedPathEntry()
    {
        var bin = Path.Combine(Path.GetDirectoryName(_plugins.Root)!, "program files");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "node.exe"), "");

        Assert.Equal(Path.Combine(bin, "node.exe"), Parse(TempPlugins.Manifest("acme-notes")).FindExecutable($"\"{bin}\""));
    }

    [Fact]
    public void FindExecutable_ReturnsNullWhenNotFound()
    {
        Assert.Null(Parse(TempPlugins.Manifest("acme-notes")).FindExecutable(Path.GetDirectoryName(_plugins.Root)));
    }
}
