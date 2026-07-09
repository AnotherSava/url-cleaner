namespace UrlCleaner;

/// <summary>
/// Runs the app as a system tray icon with no visible window.
/// ApplicationContext keeps the message loop alive without needing a Form.
/// </summary>
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly ClipboardMonitor _clipboardMonitor;
    private readonly Icon _activeIcon;
    private readonly Icon _pausedIcon;

    public TrayApplicationContext(string? configPath = null)
    {
        var config = AppConfig.Load(configPath);

        _activeIcon = SystemIcons.Shield;    // placeholder — we'll use a custom icon later
        _pausedIcon = CreateGrayscaleIcon(_activeIcon);

        // Build the right-click menu for the tray icon
        var pauseItem = new ToolStripMenuItem("Pause cleaning")
        {
            CheckOnClick = true
        };
        pauseItem.CheckedChanged += OnPauseChanged;

        var autoStartItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = AppConfig.GetAutoStart(),
            CheckOnClick = true   // WinForms toggles the checkmark automatically on click
        };
        autoStartItem.CheckedChanged += OnAutoStartChanged;

        var convertPathsItem = new ToolStripMenuItem("Convert paths")
        {
            Checked = config.ConvertPaths,
            CheckOnClick = true
        };
        convertPathsItem.CheckedChanged += OnConvertPathsChanged;

        var convertNumbersItem = new ToolStripMenuItem("Convert numbers")
        {
            Checked = config.ConvertNumbers,
            CheckOnClick = true
        };
        convertNumbersItem.CheckedChanged += OnConvertNumbersChanged;

        var convertPlaceholdersItem = new ToolStripMenuItem("Convert placeholders")
        {
            Checked = config.ConvertPlaceholders,
            CheckOnClick = true
        };
        convertPlaceholdersItem.CheckedChanged += OnConvertPlaceholdersChanged;

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(pauseItem);
        contextMenu.Items.Add(convertPathsItem);
        contextMenu.Items.Add(convertNumbersItem);
        contextMenu.Items.Add(convertPlaceholdersItem);
        contextMenu.Items.Add(autoStartItem);
        contextMenu.Items.Add("Open config location", null, OnOpenConfigLocation);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, OnExit);

        // Create the tray icon
        _trayIcon = new NotifyIcon
        {
            Icon = _activeIcon,
            Text = "URL Cleaner",            // tooltip on hover
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        // Start listening for clipboard changes
        _clipboardMonitor = new ClipboardMonitor(config, AppConfig.ConfigFilePath);
    }

    private void OnPauseChanged(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
        {
            _clipboardMonitor.Paused = item.Checked;
            _trayIcon.Icon = item.Checked ? _pausedIcon : _activeIcon;
        }
    }

    private static void OnConvertPathsChanged(object? sender, EventArgs e) => PersistFlag(sender, "convertPaths");

    private static void OnConvertNumbersChanged(object? sender, EventArgs e) => PersistFlag(sender, "convertNumbers");

    private static void OnConvertPlaceholdersChanged(object? sender, EventArgs e) => PersistFlag(sender, "convertPlaceholders");

    private static bool _suppressFlagPersist;

    private static void PersistFlag(object? sender, string configKey)
    {
        if (_suppressFlagPersist) return;
        if (sender is not ToolStripMenuItem item) return;
        if (!AppConfig.UpdateConfigValue(configKey, item.Checked))
        {
            _suppressFlagPersist = true;
            item.Checked = !item.Checked; // revert on failure
            _suppressFlagPersist = false;
        }
    }

    private static void OnAutoStartChanged(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem item)
            AppConfig.SetAutoStart(item.Checked);
    }

    private static void OnOpenConfigLocation(object? sender, EventArgs e)
    {
        // /select, highlights the config file in Explorer
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{AppConfig.ConfigFilePath}\"");
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
