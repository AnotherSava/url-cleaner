using System.Text;

namespace UrlCleaner;

/// <summary>
/// Appends to <c>url-cleaner.log</c> next to <c>config.json</c>. Writing never throws: a log that can't be written
/// has nowhere to report that, and callers finish off the UI thread too.
/// </summary>
public static class Logger
{
    public const string FileName = "url-cleaner.log";
    public const string PreviousFileName = "url-cleaner.old.log";

    // Replaces what it can't encode, such as half of a surrogate pair in a plugin's stderr, instead of throwing.
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly object Gate = new();
    private static string? _path;

    /// <summary>
    /// Starts a fresh log in <paramref name="folder"/>, first renaming the previous one to
    /// <see cref="PreviousFileName"/> so the log that explains a crash survives one restart.
    /// </summary>
    public static void Start(string folder)
    {
        lock (Gate)
        {
            _path = Path.Combine(folder, FileName);
            try
            {
                if (File.Exists(_path))
                    File.Move(_path, Path.Combine(folder, PreviousFileName), overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Keep appending to the previous log rather than losing this session's lines.
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception == null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            if (_path == null)
                return;

            try
            {
                File.AppendAllText(_path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {level} {message}{Environment.NewLine}", Utf8);
            }
            catch (Exception)
            {
                // Nowhere left to report it, and logging must never be what breaks a caller.
            }
        }
    }
}
