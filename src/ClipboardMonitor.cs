using System.Runtime.InteropServices;

namespace UrlCleaner;

/// <summary>
/// Listens for clipboard changes via the Win32 clipboard format listener API.
/// When a URL with tracking parameters is detected, it replaces the clipboard
/// content with the cleaned URL.
/// </summary>
public class ClipboardMonitor : NativeWindow, IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private AppConfig _config;
    private readonly string _configFilePath;
    private DateTime _configLastModified;
    private string? _lastCleanedResult;
    private readonly List<string> _history = [];
    private bool _disposed;

    // How many recent distinct clipboard values to keep for placeholder filling.
    private const int HistoryLimit = 10;

    public bool Paused { get; set; }

    public ClipboardMonitor(AppConfig config, string configFilePath)
    {
        _config = config;
        _configFilePath = configFilePath;
        _configLastModified = File.GetLastWriteTimeUtc(configFilePath);

        // NativeWindow needs a window handle to receive messages.
        // CreateHandle() makes an invisible message-only window for us.
        CreateHandle(new CreateParams());

        // Tell Windows: "send me WM_CLIPBOARDUPDATE whenever the clipboard changes"
        AddClipboardFormatListener(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_CLIPBOARDUPDATE)
            OnClipboardChanged();

        base.WndProc(ref m);
    }

    private void ReloadConfigIfChanged()
    {
        try
        {
            var lastWrite = File.GetLastWriteTimeUtc(_configFilePath);
            if (lastWrite <= _configLastModified)
                return;

            _config = AppConfig.Load(_configFilePath);
            _configLastModified = lastWrite;
        }
        catch
        {
            // File may be mid-write or locked — keep using the current config.
        }
    }

    private void OnClipboardChanged()
    {
        if (Paused)
            return;

        ReloadConfigIfChanged();

        try
        {
            if (!Clipboard.ContainsText())
                return;

            var text = Clipboard.GetText();
            if (text == _lastCleanedResult)
                return;

            var cleaned = UrlSanitizer.TryClean(text, _config);

            if (cleaned == null && _config.ConvertPaths)
                cleaned = PathConverter.TryConvert(text);

            if (cleaned == null && _config.ConvertNumbers)
                cleaned = NumberConverter.TryConvert(text);

            // _history holds values copied before this change (most-recent first), so the
            // template's own text is never one of its fill candidates.
            var filledPlaceholder = false;
            if (cleaned == null && _config.ConvertPlaceholders)
            {
                cleaned = PlaceholderConverter.TryConvert(text, _history);
                filledPlaceholder = cleaned != null;
            }

            // Record the resulting clipboard content as a future fill candidate — but never a
            // placeholder template, nor a value produced by placeholder filling. Either would
            // let a template fill itself (yielding output that still contains the placeholder),
            // especially since some apps emit several clipboard updates per copy and we would
            // otherwise re-process our own output.
            var result = cleaned ?? text;
            if (!filledPlaceholder && !PlaceholderConverter.ContainsPlaceholder(result))
                Remember(result);

            if (cleaned == null)
                return;

            _lastCleanedResult = cleaned;
            Clipboard.SetText(cleaned);
        }
        catch (ExternalException)
        {
            // Another process has the clipboard locked — nothing we can do, skip this event.
        }
    }

    /// <summary>
    /// Pushes a clipboard value to the front of the history buffer (most-recent first),
    /// de-duplicating so a repeated copy doesn't consume multiple slots.
    /// </summary>
    private void Remember(string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        _history.Remove(value);
        _history.Insert(0, value);
        if (_history.Count > HistoryLimit)
            _history.RemoveRange(HistoryLimit, _history.Count - HistoryLimit);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        RemoveClipboardFormatListener(Handle);
        DestroyHandle();
        _disposed = true;
    }
}
