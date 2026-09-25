using System.Text.Json;

namespace UrlCleaner.Tests;

/// <summary>
/// A plugins folder in a temp folder of its own, deleted on dispose.
/// </summary>
public sealed class TempPlugins : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"url-cleaner-test-{Guid.NewGuid():N}", "plugins");

    /// <summary>
    /// A minimal valid manifest for <paramref name="id"/>, with <paramref name="extra"/> appended as further fields.
    /// </summary>
    public static string Manifest(string id, string extra = "") =>
        $$"""{ "protocol": 1, "id": "{{id}}", "name": "Test plugin", "match": { "patterns": ["^https://example\\.com/"] }, "run": ["node", "plugin.js"]{{extra}} }""";

    /// <summary>
    /// Writes <paramref name="json"/> as <c>&lt;folder&gt;/plugin.json</c>, moving its mtime forward on a rewrite so a
    /// refresh sees a new version even within the clock's resolution.
    /// </summary>
    public string Install(string folder, string json)
    {
        var path = Path.Combine(Root, folder, PluginFolder.ManifestFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var next = File.Exists(path) ? File.GetLastWriteTimeUtc(path).AddSeconds(1) : DateTime.UtcNow;
        File.WriteAllText(path, json);
        File.SetLastWriteTimeUtc(path, next);
        return path;
    }

    /// <summary>
    /// fixtures/plugins/fixture.js, the test plugin whose first argument picks what it does.
    /// </summary>
    public static readonly string FixtureScript = Path.Combine(AppContext.BaseDirectory, "fixtures", "plugins", "fixture.js");

    /// <summary>
    /// Installs a plugin that runs the fixture script with <paramref name="arguments"/>, the mode first.
    /// </summary>
    public PluginManifest InstallFixture(string id, string pattern, params string[] arguments) => InstallFixture(id, pattern, 10, arguments);

    public PluginManifest InstallFixture(string id, string pattern, int timeoutSeconds, params string[] arguments)
    {
        string[] run = ["node", FixtureScript, ..arguments];
        var json = JsonSerializer.Serialize(new { protocol = 1, id, name = $"Fixture {id}", match = new { patterns = new[] { pattern } }, run, timeoutSeconds });
        return PluginManifest.Parse(json, Path.GetDirectoryName(Install(id, json))!);
    }

    public void Remove(string folder) => Directory.Delete(Path.Combine(Root, folder), recursive: true);

    public void Dispose()
    {
        var parent = Path.GetDirectoryName(Root)!;
        if (Directory.Exists(parent))
            Directory.Delete(parent, recursive: true);
    }
}
