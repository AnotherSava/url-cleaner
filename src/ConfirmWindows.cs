namespace UrlCleaner;

/// <summary>
/// The open confirm windows, at most one per plugin and text.
/// </summary>
public sealed class ConfirmWindows(INotifier notifier, Func<string, PluginManifest?> currentPlugin)
{
    private readonly Dictionary<(string PluginId, string Text), ConfirmForm> _open = [];

    public bool TryBringForward(string pluginId, string text)
    {
        if (!_open.TryGetValue((pluginId, text), out var form))
            return false;

        form.BringForward();
        return true;
    }

    public void Open(Proposal proposal)
    {
        var key = (proposal.Plugin.Id, proposal.Text);
        if (TryBringForward(key.Id, key.Text))
            return;

        var form = new ConfirmForm(proposal, currentPlugin, notifier);
        _open[key] = form;
        form.FormClosed += (_, _) => _open.Remove(key);
        form.ShowInFront();
    }
}
