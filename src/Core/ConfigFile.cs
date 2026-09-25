using System.Text.Json;

namespace UrlCleaner;

/// <summary>
/// The config file behind a session, reloaded when its mtime changes. A version that doesn't load leaves the last good
/// config in use (the embedded default at startup) and is reported once; the file is tried again at every change
/// until it loads. A file locked mid-write is retried silently.
/// </summary>
public sealed class ConfigFile
{
    private readonly string _path;

    // The mtime of the version in use; MinValue until one has loaded, so every check retries.
    private DateTime _loadedWriteTime = DateTime.MinValue;
    private DateTime? _reportedWriteTime;

    public AppConfig Current { get; private set; } = AppConfig.Default();

    public ConfigFile(string path, ICollection<Notice> notices)
    {
        _path = path;
        TryLoad(File.GetLastWriteTimeUtc(path), notices, "The default settings are in use until it's fixed.");
    }

    /// <summary>
    /// Loads the file again if it changed since the version in use. Returns <c>true</c> when <see cref="Current"/> changed.
    /// </summary>
    public bool ReloadIfChanged(ICollection<Notice> notices)
    {
        var writeTime = File.GetLastWriteTimeUtc(_path);
        return writeTime > _loadedWriteTime && TryLoad(writeTime, notices, "The last good settings stay in use until it's fixed.");
    }

    private bool TryLoad(DateTime writeTime, ICollection<Notice> notices, string consequence)
    {
        try
        {
            Current = AppConfig.Load(_path);
            _loadedWriteTime = writeTime;
            return true;
        }
        catch (Exception e) when (e is JsonException or UnauthorizedAccessException)
        {
            if (_reportedWriteTime != writeTime)
            {
                _reportedWriteTime = writeTime;
                Logger.Error($"Can't load {_path}: {e.Message} {consequence}");
                notices.Add(new Notice("Can't load config.json", $"{e.Message} {consequence}", NoticeKind.Error));
            }

            return false;
        }
        catch (IOException e)
        {
            Logger.Warn($"Can't read {_path}, retrying at the next clipboard change: {e.Message}");
            return false;
        }
    }
}
