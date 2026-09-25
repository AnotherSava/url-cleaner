namespace UrlCleaner;

/// <summary>
/// One entry of the resolved order: which entry runs, and whether the run ends once it has matched.
/// </summary>
public sealed record PipelineStep(string Id, bool Stop);

/// <summary>
/// An id in the configured order that couldn't be used, and why.
/// </summary>
public sealed record OrderProblem(string Id, string Problem);

public sealed record ResolvedOrder(IReadOnlyList<PipelineStep> Steps, IReadOnlyList<OrderProblem> Problems);

/// <summary>
/// A plugin's confirm proposal, for the host to open once the run is over. <see cref="Text"/> is the text the plugin was
/// called with, which its action call carries too.
/// </summary>
public sealed record Proposal(PluginManifest Plugin, string Text, ConfirmAnswer Answer);

/// <summary>
/// The outcome of one run.
/// </summary>
/// <param name="Text">The final text.</param>
/// <param name="Changed">Whether <paramref name="Text"/> differs from the copied text.</param>
/// <param name="PlaceholderMatched">Whether the placeholder module produced any of it; such a value is never
/// remembered as a fill candidate.</param>
/// <param name="AwaitedPlugin">Whether the run called a plugin. A run that didn't completed synchronously.</param>
/// <param name="PluginActed">Whether a plugin answered <c>notify</c> or <c>confirm</c>.</param>
public sealed record PipelineResult(
    string Text,
    bool Changed,
    bool PlaceholderMatched,
    bool AwaitedPlugin,
    bool PluginActed,
    IReadOnlyList<Notice> Notices,
    IReadOnlyList<Proposal> Proposals);

/// <summary>
/// What a run needs from the session that started it.
/// </summary>
public interface IPipelineHooks
{
    /// <summary>
    /// Brings forward the confirm window already open for this plugin and text. Returns <c>false</c> when none is open.
    /// </summary>
    bool TryBringConfirmForward(string pluginId, string text);

    /// <summary>
    /// Called once, just before the run first waits on a plugin, with the text as it stands then.
    /// </summary>
    void BeforeFirstPluginCall(string text, bool placeholderMatched);
}

public static class Pipeline
{
    /// <summary>
    /// Turns the configured order into the steps to run. <paramref name="knownIds"/> lists every entry that exists, in
    /// fallback order: the built-ins, then the installed plugins by id. Listed entries come first, in their listed
    /// order; every entry the list doesn't name follows in fallback order with <c>Stop</c> set. Without a list this is
    /// the fallback order with <c>Stop</c> on every entry, which is the original first-match chain for the built-ins.
    /// Unknown and repeated ids are skipped and reported.
    /// </summary>
    public static ResolvedOrder ResolveOrder(IReadOnlyList<PipelineEntry>? order, IReadOnlyList<string> knownIds)
    {
        var steps = new List<PipelineStep>();
        var problems = new List<OrderProblem>();
        var seen = new HashSet<string>();

        foreach (var entry in order ?? [])
        {
            if (!knownIds.Contains(entry.Id))
                problems.Add(new OrderProblem(entry.Id, "no built-in or installed plugin has this id"));
            else if (!seen.Add(entry.Id))
                problems.Add(new OrderProblem(entry.Id, "listed more than once"));
            else
                steps.Add(new PipelineStep(entry.Id, entry.Stop));
        }

        foreach (var id in knownIds)
        {
            if (seen.Add(id))
                steps.Add(new PipelineStep(id, Stop: true));
        }

        return new ResolvedOrder(steps, problems);
    }

    /// <summary>
    /// Runs the enabled entries in order over <paramref name="text"/>. A built-in that returns text has matched. A plugin
    /// is called only when its patterns match, and has matched when it answers <c>rewrite</c>, <c>notify</c> or
    /// <c>confirm</c>; <c>none</c>, <c>error</c> and a failed call are not matches. Later entries see the text as
    /// rewritten, and the run ends at a matched entry whose step has <c>Stop</c> set. A run that calls no plugin
    /// completes synchronously.
    /// </summary>
    public static async Task<PipelineResult> RunAsync(
        string text,
        IReadOnlyList<PipelineStep> steps,
        ModuleContext context,
        IReadOnlyDictionary<string, PluginManifest> plugins,
        IPipelineHooks hooks)
    {
        var current = text;
        var placeholderMatched = false;
        var awaitedPlugin = false;
        var pluginActed = false;
        var notices = new List<Notice>();
        var proposals = new List<Proposal>();

        foreach (var step in steps)
        {
            bool matched;
            if (BuiltInModules.Find(step.Id) is { } module)
            {
                if (!module.IsEnabled(context.Config) || module.TryRewrite(current, context) is not { } rewritten)
                    continue;

                current = rewritten;
                placeholderMatched |= module.Id == BuiltInModules.PlaceholdersId;
                matched = true;
            }
            else if (plugins.TryGetValue(step.Id, out var plugin) && context.Config.IsPluginEnabled(plugin.Id) && plugin.Matches(current))
            {
                // Both calls carry the text the patterns were tested against.
                var callText = current.Trim();
                if (hooks.TryBringConfirmForward(plugin.Id, callText))
                {
                    Logger.Info($"Plugin {plugin.Id}: its confirm window for this text is already open, brought it forward instead of calling it");
                    matched = true;
                }
                else
                {
                    if (!awaitedPlugin)
                    {
                        hooks.BeforeFirstPluginCall(current, placeholderMatched);
                        awaitedPlugin = true;
                    }

                    var outcome = await PluginRunner.CopyAsync(plugin, callText);
                    pluginActed |= outcome is Answered { Answer: NotifyAnswer or ConfirmAnswer };
                    matched = Apply(plugin, callText, outcome, ref current, notices, proposals);
                }
            }
            else
            {
                continue;
            }

            if (matched && step.Stop)
                break;
        }

        return new PipelineResult(current, current != text, placeholderMatched, awaitedPlugin, pluginActed, notices, proposals);
    }

    /// <summary>
    /// Maps a plugin's copy-call outcome onto the run. Returns whether the plugin matched.
    /// </summary>
    private static bool Apply(PluginManifest plugin, string callText, PluginOutcome outcome, ref string current, List<Notice> notices, List<Proposal> proposals)
    {
        switch (outcome)
        {
            case Answered { Answer: RewriteAnswer rewrite }:
                current = rewrite.Text;
                return true;
            case Answered { Answer: NotifyAnswer notify }:
                notices.Add(new Notice(plugin.Name, notify.Message, NoticeKind.Info));
                return true;
            case Answered { Answer: ConfirmAnswer confirm }:
                proposals.Add(new Proposal(plugin, callText, confirm));
                return true;
            case Answered { Answer: ErrorAnswer error }:
                Logger.Warn($"Plugin {plugin.Id} answered error: {error.Message}");
                notices.Add(FailureNotice(plugin, error.Message));
                return false;
            case Failed failed:
                notices.Add(FailureNotice(plugin, Sentence(failed.Reason)));
                return false;
            default:
                return false;
        }
    }

    public static Notice FailureNotice(PluginManifest plugin, string message) => new($"{plugin.Name} failed", message, NoticeKind.Error);

    /// <summary>
    /// Turns a reason fragment such as "exited with code 3" into a sentence.
    /// </summary>
    public static string Sentence(string reason) => $"{char.ToUpperInvariant(reason[0])}{reason[1..]}.";
}
