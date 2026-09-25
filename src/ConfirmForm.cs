using System.Runtime.InteropServices;

namespace UrlCleaner;

/// <summary>
/// Renders a plugin's confirm proposal: its message, one editable box per field (with a drop-down when the field offers
/// options), the plugin's actions and the host's Cancel. Pressing an action sends the action call; the window closes on
/// a <c>notify</c> answer and shows an <c>error</c> answer in place so the user can correct the fields.
/// </summary>
public sealed class ConfirmForm : Form
{
    private const int InputWidth = 360;
    private const int TextWidth = 480;

    private readonly Proposal _proposal;
    private readonly Func<string, PluginManifest?> _currentPlugin;
    private readonly INotifier _notifier;
    private readonly List<(ConfirmField Field, Control Input)> _inputs = [];
    private readonly List<Button> _actionButtons = [];
    private readonly Button _cancelButton;
    private readonly Label _status;
    private bool _callRunning;

    /// <param name="proposal">What to render.</param>
    /// <param name="currentPlugin">Looks the plugin up again by id: its manifest if it is still installed and enabled,
    /// otherwise <c>null</c>.</param>
    /// <param name="notifier">Shows the plugin's <c>notify</c> message once the window has closed.</param>
    public ConfirmForm(Proposal proposal, Func<string, PluginManifest?> currentPlugin, INotifier notifier)
    {
        _proposal = proposal;
        _currentPlugin = currentPlugin;
        _notifier = notifier;

        // Sizes below are at 96 DPI; the form scales them to the monitor it opens on. Layout stays suspended until every
        // control is added, as in designer code: a scaling pass that runs sooner scales an empty form and nothing after it.
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = SystemFonts.MessageBoxFont ?? Font;
        Text = proposal.Answer.Title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        if (!string.IsNullOrEmpty(proposal.Answer.Message))
            AddSpanning(layout, WrappingLabel(proposal.Answer.Message, bottomMargin: 12));

        foreach (var field in proposal.Answer.Fields)
        {
            var label = new Label { Text = field.Label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 12, 3) };
            Control input = field.Options is { Count: > 0 } options
                ? CreateComboBox(field.Value, options)
                : new TextBox { Text = field.Value, Width = InputWidth };
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            input.Margin = new Padding(0, 3, 0, 3);

            layout.RowCount++;
            layout.Controls.Add(label, 0, layout.RowCount - 1);
            layout.Controls.Add(input, 1, layout.RowCount - 1);
            _inputs.Add((field, input));
        }

        _status = WrappingLabel("", bottomMargin: 0);
        _status.ForeColor = Color.Firebrick;
        _status.Visible = false;
        AddSpanning(layout, _status);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Anchor = AnchorStyles.Right, Margin = new Padding(0, 12, 0, 0) };
        _cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(88, 0) };
        _cancelButton.Click += (_, _) => Close();
        buttons.Controls.Add(_cancelButton);

        // Right to left, so the actions go in reverse and read in the plugin's order, left of Cancel.
        foreach (var action in proposal.Answer.Actions.Reverse())
        {
            var button = new Button { Text = action.Label, AutoSize = true, MinimumSize = new Size(88, 0) };
            button.Click += (_, _) => OnActionClick(action);
            buttons.Controls.Add(button);
            _actionButtons.Insert(0, button);
        }

        AddSpanning(layout, buttons);
        Controls.Add(layout);

        ResumeLayout(false);
        PerformLayout();

        AcceptButton = _actionButtons[0];
        CancelButton = _cancelButton;
        Shown += (_, _) => (_inputs.Count > 0 ? _inputs[0].Input : _actionButtons[0]).Focus();
        FormClosing += OnFormClosing;
    }

    /// <summary>
    /// Shows the window and tries to bring it to the front. Windows usually refuses a background app the foreground after
    /// a copy made in another app, so when the window didn't get it, its taskbar button flashes instead.
    /// </summary>
    public void ShowInFront()
    {
        TopMost = true;
        ShowInTaskbar = true;
        Show();
        BringForward();
    }

    public void BringForward()
    {
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        Activate();
        if (GetForegroundWindow() != Handle)
            Flash();
    }

    private async void OnActionClick(ConfirmAction action)
    {
        try
        {
            var plugin = _currentPlugin(_proposal.Plugin.Id);
            if (plugin == null)
            {
                Logger.Info($"Plugin {_proposal.Plugin.Id} was removed or turned off while its confirm window was open; no action call");
                ShowStatus("The plugin is no longer installed, or is turned off.");
                _actionButtons.ForEach(b => b.Enabled = false);
                return;
            }

            SetCallRunning(true);
            var fields = _inputs.ToDictionary(i => i.Field.Id, i => i.Input.Text);
            var outcome = await PluginRunner.ActionAsync(plugin, _proposal.Text, action.Id, fields, _proposal.Answer.State);
            SetCallRunning(false);

            switch (outcome)
            {
                case Answered { Answer: NotifyAnswer notify }:
                    _notifier.Show(new Notice(plugin.Name, notify.Message, NoticeKind.Info));
                    Close();
                    break;
                case Answered { Answer: ErrorAnswer error }:
                    Logger.Warn($"Plugin {plugin.Id} answered its {action.Id} action with an error: {error.Message}");
                    ShowError(Pipeline.FailureNotice(plugin, error.Message));
                    break;
                case Failed failed:
                    ShowError(Pipeline.FailureNotice(plugin, Pipeline.Sentence(failed.Reason)));
                    break;
            }
        }
        catch (Exception e)
        {
            Logger.Error("A confirm window's action failed", e);
            SetCallRunning(false);
            ShowError(Notices.UnexpectedError(e));
        }
    }

    /// <summary>
    /// Shows an error in the window, or as a balloon when the window is already gone.
    /// </summary>
    private void ShowError(Notice notice)
    {
        if (IsDisposed)
            _notifier.Show(notice);
        else
            ShowStatus(notice.Message);
    }

    private void ShowStatus(string message)
    {
        _status.Text = message;
        _status.Visible = true;
    }

    private void SetCallRunning(bool running)
    {
        _callRunning = running;
        if (IsDisposed)
            return;

        UseWaitCursor = running;
        _actionButtons.ForEach(b => b.Enabled = !running);
        _cancelButton.Enabled = !running;
        if (running)
            _status.Visible = false;
    }

    // While the action call runs, the plugin may be acting; closing would hide its answer. Only the app exiting closes it then.
    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_callRunning && e.CloseReason == CloseReason.UserClosing)
            e.Cancel = true;
    }

    private static ComboBox CreateComboBox(string value, IReadOnlyList<string> options)
    {
        var comboBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = InputWidth };
        comboBox.Items.AddRange([.. options]);
        comboBox.Text = value;
        return comboBox;
    }

    private static Label WrappingLabel(string text, int bottomMargin) =>
        new() { Text = text, AutoSize = true, MaximumSize = new Size(TextWidth, 0), Margin = new Padding(0, 0, 0, bottomMargin) };

    private static void AddSpanning(TableLayoutPanel layout, Control control)
    {
        layout.RowCount++;
        layout.Controls.Add(control, 0, layout.RowCount - 1);
        layout.SetColumnSpan(control, 2);
    }

    private void Flash()
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = Handle,
            Flags = FlashAll | FlashUntilForeground
        };
        FlashWindowEx(ref info);
    }

    private const uint FlashAll = 0x3;
    private const uint FlashUntilForeground = 0xC;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
