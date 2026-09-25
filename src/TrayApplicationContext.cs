namespace UrlCleaner;

/// <summary>
/// Runs the app as a system tray icon with no visible window.
/// ApplicationContext keeps the message loop alive without needing a Form.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly string _configFilePath;
    private readonly NotifyIcon _trayIcon;
    private readonly INotifier _notifier;
    private readonly ClipboardMonitor _clipboardMonitor;
    private readonly ContextMenuStrip _contextMenu;
    private readonly Icon _activeIcon;
    private readonly Icon _pausedIcon;

    // The per-plugin checkboxes, rebuilt each time the menu opens; they sit right after the converter toggles.
    private readonly List<ToolStripMenuItem> _pluginItems = [];
    private readonly ToolStripMenuItem _convertPathsItem;
    private readonly ToolStripMenuItem _convertNumbersItem;
    private readonly ToolStripMenuItem _convertPlaceholdersItem;

    public TrayApplicationContext(string configFilePath)
    {
        _configFilePath = configFilePath;
        _activeIcon = SystemIcons.Shield;    // placeholder — we'll use a custom icon later
        _pausedIcon = CreateGrayscaleIcon(_activeIcon);

        // Create the tray icon first: loading the config can raise a notice, which needs the icon to show on
        _trayIcon = new NotifyIcon
        {
            Icon = _activeIcon,
            Text = "URL Cleaner",            // tooltip on hover
            Visible = true
        };
        // Every notice goes to the log, then to the tray balloons, the one way the app shows notices today.
        _notifier = new LoggedNotifier(new BalloonNotifier(_trayIcon));

        // Start listening for clipboard changes
        _clipboardMonitor = new ClipboardMonitor(configFilePath, _notifier);
        var config = _clipboardMonitor.Config;

        // Build the right-click menu for the tray icon
        var pauseItem = new ToolStripMenuItem("Pause cleaning")
        {
            CheckOnClick = true
        };
        pauseItem.CheckedChanged += OnPauseChanged;

        var autoStartItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = Autostart.IsEnabled(),
            CheckOnClick = true   // WinForms toggles the checkmark automatically on click
        };
        autoStartItem.CheckedChanged += OnAutoStartChanged;

        _convertPathsItem = new ToolStripMenuItem("Convert paths")
        {
            Checked = config.ConvertPaths,
            CheckOnClick = true
        };
        _convertPathsItem.CheckedChanged += OnConvertPathsChanged;

        _convertNumbersItem = new ToolStripMenuItem("Convert numbers")
        {
            Checked = config.ConvertNumbers,
            CheckOnClick = true
        };
        _convertNumbersItem.CheckedChanged += OnConvertNumbersChanged;

        _convertPlaceholdersItem = new ToolStripMenuItem("Convert placeholders")
        {
            Checked = config.ConvertPlaceholders,
            CheckOnClick = true
        };
        _convertPlaceholdersItem.CheckedChanged += OnConvertPlaceholdersChanged;

        _contextMenu = new ContextMenuStrip();
        _contextMenu.Items.Add(pauseItem);
        _contextMenu.Items.Add(_convertPathsItem);
        _contextMenu.Items.Add(_convertNumbersItem);
        _contextMenu.Items.Add(_convertPlaceholdersItem);
        _contextMenu.Items.Add(autoStartItem);
        _contextMenu.Items.Add("Open config location", null, OnOpenConfigLocation);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add("Exit", null, OnExit);
        _contextMenu.Opening += OnMenuOpening;
        _trayIcon.ContextMenuStrip = _contextMenu;
    }

    public void ShowNotice(Notice notice) => _notifier.Show(notice);

    private void OnPauseChanged(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _clipboardMonitor.Paused = item.Checked;
            _trayIcon.Icon = item.Checked ? _pausedIcon : _activeIcon;
        }
    }

    /// <summary>
    /// Sets every checkbox from the plugins folder and config.json as they are now, so the menu matches the files even
    /// after a hand edit or a config that failed to load at startup.
    /// </summary>
    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _clipboardMonitor.Refresh();
        var config = _clipboardMonitor.Config;

        _suppressFlagPersist = true;
        _convertPathsItem.Checked = config.ConvertPaths;
        _convertNumbersItem.Checked = config.ConvertNumbers;
        _convertPlaceholdersItem.Checked = config.ConvertPlaceholders;
        _suppressFlagPersist = false;

        foreach (var item in _pluginItems)
        {
            _contextMenu.Items.Remove(item);
            item.Dispose();
        }
        _pluginItems.Clear();

        var index = _contextMenu.Items.IndexOf(_convertPlaceholdersItem) + 1;
        foreach (var plugin in _clipboardMonitor.Plugins)
        {
            // A menu item takes & as the mark of a keyboard shortcut.
            var item = new ToolStripMenuItem(plugin.Name.Replace("&", "&&"))
            {
                Checked = config.IsPluginEnabled(plugin.Id),
                CheckOnClick = true
            };
            item.CheckedChanged += (_, _) => PersistFlag(item, ["plugins", plugin.Id, "enabled"]);
            _contextMenu.Items.Insert(index++, item);
            _pluginItems.Add(item);
        }
    }

    private void OnConvertPathsChanged(object? sender, EventArgs e) => PersistFlag(sender, "convertPaths");

    private void OnConvertNumbersChanged(object? sender, EventArgs e) => PersistFlag(sender, "convertNumbers");

    private void OnConvertPlaceholdersChanged(object? sender, EventArgs e) => PersistFlag(sender, "convertPlaceholders");

    private bool _suppressFlagPersist;

    private void PersistFlag(object? sender, string configKey) => PersistFlag(sender, [configKey]);

    private void PersistFlag(object? sender, IReadOnlyList<string> keys)
    {
        if (_suppressFlagPersist) return;
        if (sender is not ToolStripMenuItem item) return;
        if (!AppConfig.UpdateConfigValue(_configFilePath, keys, item.Checked))
        {
            _suppressFlagPersist = true;
            item.Checked = !item.Checked; // revert on failure
            _suppressFlagPersist = false;
            _notifier.Show(new Notice("Can't save the setting", "config.json couldn't be updated. The log next to it has the details.", NoticeKind.Error));
        }
    }

    private static void OnAutoStartChanged(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
            Autostart.SetEnabled(item.Checked);
    }

    private void OnOpenConfigLocation(object? sender, EventArgs e)
    {
        // /select, highlights the config file in Explorer
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_configFilePath}\"");
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _clipboardMonitor.Dispose();
        _trayIcon.Visible = false;  // hide icon immediately so it doesn't linger
        _trayIcon.Dispose();
        Application.Exit();
    }

    private static Icon CreateGrayscaleIcon(Icon source)
    {
        using var bitmap = source.ToBitmap();
        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var gray = (int)(pixel.R * 0.3 + pixel.G * 0.59 + pixel.B * 0.11);
                bitmap.SetPixel(x, y, Color.FromArgb(pixel.A, gray, gray, gray));
            }
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
