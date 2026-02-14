namespace UrlCleaner;

/// <summary>
/// Runs the app as a system tray icon with no visible window.
/// ApplicationContext keeps the message loop alive without needing a Form.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ClipboardMonitor _clipboardMonitor;

    public TrayApplicationContext(string? configPath = null)
    {
        var config = AppConfig.Load(configPath);

        // Build the right-click menu for the tray icon
        var autoStartItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = AppConfig.GetAutoStart(),
            CheckOnClick = true   // WinForms toggles the checkmark automatically on click
        };
        autoStartItem.CheckedChanged += OnAutoStartChanged;

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(autoStartItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, OnExit);

        // Create the tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,       // placeholder — we'll use a custom icon later
            Text = "URL Cleaner",            // tooltip on hover
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        // Start listening for clipboard changes
        _clipboardMonitor = new ClipboardMonitor(config);
    }

    private static void OnAutoStartChanged(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
            AppConfig.SetAutoStart(item.Checked);
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _clipboardMonitor.Dispose();
        _trayIcon.Visible = false;  // hide icon immediately so it doesn't linger
        _trayIcon.Dispose();
        Application.Exit();
    }
}
