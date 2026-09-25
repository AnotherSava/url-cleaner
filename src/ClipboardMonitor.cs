using System.Runtime.InteropServices;

namespace UrlCleaner;

/// <summary>
/// Listens for clipboard changes via the Win32 clipboard format listener API and hands each one to a
/// <see cref="ClipboardSession"/>, acting as its Windows host.
/// </summary>
public class ClipboardMonitor : NativeWindow, IClipboardHost, IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    private readonly ClipboardSession _session;
    private readonly ConfirmWindows _confirmWindows;
    private bool _disposed;

    public bool Paused { get; set; }

    public ClipboardMonitor(string configFilePath, INotifier notifier)
    {
        _session = new ClipboardSession(this, notifier, configFilePath, Path.Combine(AppContext.BaseDirectory, "plugins"));
        _confirmWindows = new ConfirmWindows(notifier, _session.CurrentPlugin);

        // NativeWindow needs a window handle to receive messages.
        // CreateHandle() makes an invisible top-level window for us.
        CreateHandle(new CreateParams());

        // Tell Windows: "send me WM_CLIPBOARDUPDATE whenever the clipboard changes"
        AddClipboardFormatListener(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        // Not awaited: a run that calls a plugin continues on this thread when the plugin answers, and never throws.
        if (m.Msg == WM_CLIPBOARDUPDATE)
            _ = _session.HandleChangeAsync();

        base.WndProc(ref m);
    }

    public string? TryReadText()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (ExternalException e)
        {
            // Another process has the clipboard locked — nothing we can do, skip this event.
            Logger.Warn($"Can't read the clipboard, skipping this change: {e.Message}");
            return null;
        }
    }

    public bool TryWriteText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (ExternalException e)
        {
            Logger.Warn($"Can't write the clipboard, leaving the copied text: {e.Message}");
            return false;
        }
    }

    public uint ChangeCount => GetClipboardSequenceNumber();

    public bool IsPaused => Paused;

    public bool TryBringConfirmForward(string pluginId, string text) => _confirmWindows.TryBringForward(pluginId, text);

    public void OpenConfirm(Proposal proposal) => _confirmWindows.Open(proposal);

    /// <summary>
    /// The config in use: the last one that loaded, or the embedded default.
    /// </summary>
    public AppConfig Config => _session.Config;

    public IReadOnlyList<PluginManifest> Plugins => _session.Plugins;

    public void Refresh() => _session.Refresh();

    public void Dispose()
    {
        if (_disposed)
            return;

        RemoveClipboardFormatListener(Handle);
        DestroyHandle();
        _disposed = true;
    }
}
