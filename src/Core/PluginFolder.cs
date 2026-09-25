using System.Text.Json;

namespace UrlCleaner;

/// <summary>
/// The installed plugins: every <c>plugins/&lt;id&gt;/plugin.json</c> under the host's folder. The manifests are read
/// again only when the set of manifest files or one of their mtimes changes, which a clipboard change checks. An invalid
/// manifest leaves its plugin out and is reported once per mtime.
/// </summary>
public sealed class PluginFolder(string root)
{
    public const string ManifestFileName = "plugin.json";

    // Each manifest's mtime at the last read. A manifest that couldn't be read is left out, so the next check retries it.
    private Dictionary<string, DateTime> _read = [];
    private readonly Dictionary<string, DateTime> _reportedInvalid = [];

    /// <summary>
    /// The valid plugins, sorted by id.
    /// </summary>
    public IReadOnlyList<PluginManifest> Plugins { get; private set; } = [];

    /// <summary>
    /// Reads the manifests again if any changed, appeared or disappeared. Returns <c>true</c> when it did.
    /// </summary>
    public bool RefreshIfChanged(ICollection<Notice> notices)
    {
        var found = FindManifests();
        if (found.Count == _read.Count && found.All(f => _read.TryGetValue(f.Key, out var seen) && seen == f.Value))
            return false;

        _read = [];
        var plugins = new List<PluginManifest>();
        foreach (var (path, writeTime) in found.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            try
            {
                plugins.Add(PluginManifest.Parse(File.ReadAllText(path), Path.GetDirectoryName(path)!));
                _read[path] = writeTime;
            }
            catch (IOException e)
            {
                Logger.Warn($"Can't read {path}, retrying at the next clipboard change: {e.Message}");
            }
            catch (Exception e) when (e is InvalidManifestException or JsonException or UnauthorizedAccessException)
            {
                _read[path] = writeTime;
                if (_reportedInvalid.TryGetValue(path, out var reported) && reported == writeTime)
                    continue;

                _reportedInvalid[path] = writeTime;
                var folderName = Path.GetFileName(Path.GetDirectoryName(path));
                Logger.Error($"Plugin in {Path.GetDirectoryName(path)} not loaded: {e.Message}");
                notices.Add(new Notice("Plugin not loaded", $"plugins/{folderName}/{ManifestFileName}: {e.Message}", NoticeKind.Error));
            }
        }

        Plugins = plugins.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        return true;
    }

    private Dictionary<string, DateTime> FindManifests()
    {
        if (!Directory.Exists(root))
            return [];

        try
        {
            return Directory.EnumerateDirectories(root)
                .Select(folder => Path.Combine(folder, ManifestFileName))
                .Where(File.Exists)
                .ToDictionary(path => path, File.GetLastWriteTimeUtc);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Reported through the log only: the folder is checked at every change, and a balloon each time would bury the rest.
            Logger.Warn($"Can't list {root}: {e.Message}");
            return _read;
        }
    }
}
