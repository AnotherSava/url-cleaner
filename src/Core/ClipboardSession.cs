namespace UrlCleaner;

/// <summary>
/// What only the platform host can do for a <see cref="ClipboardSession"/>.
/// </summary>
public interface IClipboardHost
{
    /// <summary>
    /// The clipboard's text, or <c>null</c> when it holds none or can't be read right now.
    /// </summary>
    string? TryReadText();

    /// <summary>
    /// Replaces the clipboard's content with <paramref name="text"/>. Returns <c>false</c> when the write failed.
    /// </summary>
    bool TryWriteText(string text);

    /// <summary>
    /// A number that changes whenever the clipboard's content changes.
    /// </summary>
    uint ChangeCount { get; }

    bool IsPaused { get; }

    /// <summary>
    /// Brings forward the confirm window open for this plugin and text. Returns <c>false</c> when none is open.
    /// </summary>
    bool TryBringConfirmForward(string pluginId, string text);

    /// <summary>
    /// Opens a confirm window for a plugin's proposal.
    /// </summary>
    void OpenConfirm(Proposal proposal);
}

/// <summary>
/// Handles each clipboard change: runs the pipeline over the copied text, writes the result back, keeps the history, and
/// reports what happened, one balloon per change. Every member is meant for one thread, the host's UI thread; while a
/// plugin call is awaited that thread keeps pumping, so another change can start a run before the first has finished.
/// </summary>
public sealed class ClipboardSession
{
    private static readonly IReadOnlyList<string> BuiltInIds = BuiltInModules.All.Select(m => m.Id).ToList();

    // Some apps send several clipboard updates per copy. A plugin that acts would act on each, so plugins are skipped
    // for the same text while a run for it is in flight, and for this long after one where a plugin answered notify or
    // confirm. The same span guards against another app putting this session's own write back on the clipboard.
    private const long DuplicateWindowMilliseconds = 2000;

    private static readonly IReadOnlyDictionary<string, PluginManifest> NoPlugins = new Dictionary<string, PluginManifest>();

    private readonly IClipboardHost _host;
    private readonly INotifier _notifier;
    private readonly ConfigFile _configFile;
    private readonly PluginFolder _plugins;
    private readonly ClipboardHistory _history = new();
    private IReadOnlyList<PipelineStep> _steps = [];
    private IReadOnlyDictionary<string, PluginManifest> _pluginsById = new Dictionary<string, PluginManifest>();

    // Order problems already reported. One is reported again only after it has gone away and come back.
    private HashSet<OrderProblem> _reportedProblems = [];

    // The change count right after this session's last write. An event arriving while the count still equals it was
    // caused by that write, however many events the write produced; a later copy of the same text changes the count.
    private uint? _ownWriteChangeCount;

    // The session's last write and when it happened.
    private (string Text, long At)? _lastWrite;

    private readonly HashSet<string> _inFlight = [];
    private readonly Dictionary<string, long> _actedAt = [];

    /// <summary>
    /// Loads the config and the plugins in <paramref name="pluginsFolder"/>, and reports any problem with them through
    /// <paramref name="notifier"/> straight away.
    /// </summary>
    public ClipboardSession(IClipboardHost host, INotifier notifier, string configFilePath, string pluginsFolder)
    {
        _host = host;
        _notifier = notifier;
        var notices = new List<Notice>();
        _configFile = new ConfigFile(configFilePath, notices);
        _plugins = new PluginFolder(pluginsFolder);
        _plugins.RefreshIfChanged(notices);
        ResolveSteps(notices);
        Show(notices);
    }

    /// <summary>
    /// The config in use: the last one that loaded, or the embedded default.
    /// </summary>
    public AppConfig Config => _configFile.Current;

    /// <summary>
    /// The installed plugins that loaded, sorted by id.
    /// </summary>
    public IReadOnlyList<PluginManifest> Plugins => _plugins.Plugins;

    /// <summary>
    /// Picks up edits to the config and the plugins folder now rather than at the next clipboard change, and reports
    /// any problem with them.
    /// </summary>
    public void Refresh()
    {
        var notices = new List<Notice>();
        Refresh(notices);
        Show(notices);
    }

    /// <summary>
    /// The plugin's manifest as installed now, or <c>null</c> when it has been removed or turned off. A confirm window
    /// asks before its action call, since the window can stay open for as long as the user likes.
    /// </summary>
    public PluginManifest? CurrentPlugin(string id)
    {
        Refresh();
        return _pluginsById.TryGetValue(id, out var plugin) && Config.IsPluginEnabled(id) ? plugin : null;
    }

    /// <summary>
    /// Handles one clipboard change. The host starts it for every change without awaiting it, so it never throws: an
    /// exception is logged and shown. A change that reaches no plugin completes synchronously.
    /// </summary>
    public async Task HandleChangeAsync()
    {
        var notices = new List<Notice>();
        try
        {
            await HandleChangeAsync(notices);
        }
        catch (Exception e)
        {
            Logger.Error("Handling a clipboard change failed", e);
            notices.Add(Notices.UnexpectedError(e));
        }

        Show(notices);
    }

    private async Task HandleChangeAsync(List<Notice> notices)
    {
        if (_host.IsPaused || _host.ChangeCount == _ownWriteChangeCount)
            return;

        // Shown now rather than with the run's results, which can take as long as a plugin's deadline.
        var reloadNotices = new List<Notice>();
        Refresh(reloadNotices);
        Show(reloadNotices);

        var text = _host.TryReadText();
        if (text == null)
            return;

        // A repeated copy, or this session's own write put back by another app, still gets the built-ins, which are
        // idempotent and keep the history current; only the plugins are skipped, which would act twice or be fed their
        // own output.
        var repeated = IsRepeatedCopy(text) || IsOwnWriteReturned(text);
        if (!repeated)
            _inFlight.Add(text);
        try
        {
            var run = new Run(this, text);
            var result = await Pipeline.RunAsync(text, _steps, new ModuleContext(Config, _history.Snapshot()), repeated ? NoPlugins : _pluginsById, run);

            if (!result.AwaitedPlugin)
            {
                // Record the resulting content as a future fill candidate, before this run's own write can raise another event.
                _history.Remember(result.Text, result.PlaceholderMatched);
                if (result.Changed)
                    Write(result.Text);
            }
            else if (result.Text != run.LastApplied)
            {
                ApplyLate(result, text, run.LastApplied);
            }

            notices.AddRange(result.Notices);
            if (result.Proposals.Count > 0)
                OpenProposals(result.Proposals);
            if (result.PluginActed)
                _actedAt[text] = Environment.TickCount64;
        }
        finally
        {
            if (!repeated)
                _inFlight.Remove(text);
        }
    }

    private bool IsRepeatedCopy(string text)
    {
        var now = Environment.TickCount64;
        foreach (var expired in _actedAt.Where(a => now - a.Value >= DuplicateWindowMilliseconds).Select(a => a.Key).ToList())
            _actedAt.Remove(expired);

        if (!_inFlight.Contains(text) && !_actedAt.ContainsKey(text))
            return false;

        Logger.Info($"Skipped the plugins for a copy of text a plugin is handling or has just handled ({text.Length} characters); some apps send several clipboard updates per copy");
        return true;
    }

    /// <summary>
    /// Whether <paramref name="text"/> is this session's last write, put back on the clipboard shortly after by another
    /// app. Processing it again would feed a plugin its own output.
    /// </summary>
    private bool IsOwnWriteReturned(string text)
    {
        if (_lastWrite is not { } last || text != last.Text || Environment.TickCount64 - last.At >= DuplicateWindowMilliseconds)
            return false;

        Logger.Info("Skipped the plugins for a clipboard change that put back what the app had just written");
        return true;
    }

    /// <summary>
    /// Writes a result that arrived after a plugin call, but only where it belongs: the clipboard must still hold what
    /// the run last put there, or the copied text in case the source app sent it again, and the app mustn't be paused.
    /// </summary>
    private void ApplyLate(PipelineResult result, string copiedText, string lastApplied)
    {
        var current = _host.TryReadText();
        if (current == result.Text)
            return;

        if (_host.IsPaused || (current != lastApplied && current != copiedText))
        {
            Logger.Warn("Dropped a plugin's rewrite: the clipboard changed or the app was paused while the plugin ran");
            return;
        }

        _history.Remember(result.Text, result.PlaceholderMatched);
        Write(result.Text);
    }

    /// <summary>
    /// Opens the run's confirm windows, except for a plugin that was removed or disabled while it ran, or when the app
    /// was paused meanwhile.
    /// </summary>
    private void OpenProposals(IReadOnlyList<Proposal> proposals)
    {
        var reloadNotices = new List<Notice>();
        Refresh(reloadNotices);
        Show(reloadNotices);
        foreach (var proposal in proposals)
        {
            var id = proposal.Plugin.Id;
            if (_host.IsPaused || !_pluginsById.ContainsKey(id) || !Config.IsPluginEnabled(id))
            {
                Logger.Info($"Dropped {id}'s confirm proposal: the app was paused, or the plugin was removed or disabled while it ran");
                continue;
            }

            _host.OpenConfirm(proposal);
        }
    }

    private bool Write(string text)
    {
        if (!_host.TryWriteText(text))
            return false;

        _ownWriteChangeCount = _host.ChangeCount;
        _lastWrite = (text, Environment.TickCount64);
        return true;
    }

    /// <summary>
    /// Reloads the config and rereads the plugins folder if either changed. Both checks run every time: a removed plugin
    /// that the order names is reported even with config.json unchanged.
    /// </summary>
    private void Refresh(List<Notice> notices)
    {
        var configChanged = _configFile.ReloadIfChanged(notices);
        var pluginsChanged = _plugins.RefreshIfChanged(notices);
        if (configChanged || pluginsChanged)
            ResolveSteps(notices);
    }

    private void ResolveSteps(ICollection<Notice> notices)
    {
        _pluginsById = _plugins.Plugins.ToDictionary(p => p.Id);
        var knownIds = BuiltInIds.Concat(_plugins.Plugins.Select(p => p.Id)).ToList();
        var resolved = Pipeline.ResolveOrder(Config.Pipeline?.Order, knownIds);
        _steps = resolved.Steps;

        foreach (var problem in resolved.Problems.Where(p => !_reportedProblems.Contains(p)))
        {
            Logger.Warn($"pipeline.order in config.json: skipped \"{problem.Id}\", {problem.Problem}");
            notices.Add(new Notice("Check pipeline.order in config.json", $"Skipped \"{problem.Id}\": {problem.Problem}.", NoticeKind.Warning));
        }

        _reportedProblems = resolved.Problems.ToHashSet();
    }

    private void Show(IReadOnlyList<Notice> notices)
    {
        var summary = Notices.Summarize(notices);
        if (summary != null)
            _notifier.Show(summary);
    }

    /// <summary>
    /// One run's view of the session.
    /// </summary>
    private sealed class Run : IPipelineHooks
    {
        private readonly ClipboardSession _session;
        private readonly string _copiedText;

        public Run(ClipboardSession session, string copiedText)
        {
            _session = session;
            _copiedText = copiedText;
            LastApplied = copiedText;
        }

        /// <summary>
        /// What this run last put on the clipboard: the copied text until it writes.
        /// </summary>
        public string LastApplied { get; private set; }

        public bool TryBringConfirmForward(string pluginId, string text) => _session._host.TryBringConfirmForward(pluginId, text);

        /// <summary>
        /// Records the text as it stands as a fill candidate, and writes a built-in's rewrite before the wait: otherwise a
        /// paste made while the plugin works gets the unrewritten text, and a later copy would drop the rewrite too.
        /// </summary>
        public void BeforeFirstPluginCall(string text, bool placeholderMatched)
        {
            _session._history.Remember(text, placeholderMatched);
            if (text != _copiedText && _session.Write(text))
                LastApplied = text;
        }
    }
}
