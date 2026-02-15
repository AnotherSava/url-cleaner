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
    private bool _isUpdatingClipboard;
    private bool _disposed;

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
        if (m.Msg == WM_CLIPBOARDUPDATE && !_isUpdatingClipboard)
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
            var cleaned = UrlSanitizer.TryClean(text, _config);

            if (cleaned == null)
                return;

            // Set the flag BEFORE writing to the clipboard so our own
            // WM_CLIPBOARDUPDATE message gets ignored (prevents infinite loop).
            _isUpdatingClipboard = true;
            try
            {
                Clipboard.SetText(cleaned);
            }
            finally
            {
                _isUpdatingClipboard = false;
            }
        }
        catch (ExternalException)
        {
            // Another process has the clipboard locked — nothing we can do, skip this event.
        }
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
