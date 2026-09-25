using System.Text.Json;

namespace UrlCleaner.Tests;

/// <summary>
/// A config file in a folder of its own, deleted with the folder on dispose.
/// </summary>
public sealed class TempConfig : IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"url-cleaner-test-{Guid.NewGuid():N}");

    public string FilePath { get; }

    public TempConfig()
    {
        Directory.CreateDirectory(_folder);
        FilePath = Path.Combine(_folder, "config.json");
    }

    public TempConfig(AppConfig config) : this() => Write(config);

    public TempConfig(string json) : this() => Write(json);

    public void Write(AppConfig config) => Write(JsonSerializer.Serialize(config, Options));

    /// <summary>
    /// Replaces the file and moves its mtime forward, so a reload sees a new version even within the clock's resolution.
    /// </summary>
    public void Write(string json)
    {
        var next = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath).AddSeconds(1) : DateTime.UtcNow;
        File.WriteAllText(FilePath, json);
        File.SetLastWriteTimeUtc(FilePath, next);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
