using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

public sealed class ClipboardSessionTests : IDisposable
{
    /// <summary>
    /// A clipboard in memory. Every change, the session's writes included, moves the change count, as on Windows.
    /// </summary>
    private sealed class FakeHost : IClipboardHost, INotifier
    {
        public string? Text { get; private set; }
        public uint ChangeCount { get; private set; }
        public bool IsPaused { get; set; }
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public bool FailNextWrite { get; set; }
        public List<Notice> Shown { get; } = [];
        public List<Proposal> Opened { get; } = [];
        public HashSet<(string PluginId, string Text)> OpenWindows { get; } = [];
        public List<(string PluginId, string Text)> BroughtForward { get; } = [];

        public string? TryReadText()
        {
            Reads++;
            return Text;
        }

        public bool TryWriteText(string text)
        {
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new InvalidOperationException("the clipboard broke");
            }

            Text = text;
            ChangeCount++;
            Writes++;
            return true;
        }

        public void Show(Notice notice) => Shown.Add(notice);

        public bool TryBringConfirmForward(string pluginId, string text)
        {
            if (!OpenWindows.Contains((pluginId, text)))
                return false;

            BroughtForward.Add((pluginId, text));
            return true;
        }

        public void OpenConfirm(Proposal proposal) => Opened.Add(proposal);

        /// <summary>
        /// Another app puts <paramref name="text"/> on the clipboard.
        /// </summary>
        public void Copy(string text)
        {
            Text = text;
            ChangeCount++;
        }
    }

    private static readonly AppConfig DefaultTestConfig = new()
    {
        TrackingParams = [new TrackingParamGroup { Params = ["utm_source"] }],
        ConvertPaths = true,
        ConvertPlaceholders = true
    };

    private readonly TempConfig _config = new(DefaultTestConfig);
    private readonly TempPlugins _plugins = new();
    private readonly FakeHost _host = new();

    public void Dispose()
    {
        _config.Dispose();
        _plugins.Dispose();
    }

    private ClipboardSession Start() => new(_host, _host, _config.FilePath, _plugins.Root);

    private string MarkerFile(string name) => Path.Combine(Path.GetDirectoryName(_config.FilePath)!, $"{name}.marker");

    private static int Runs(string markerFile) => File.Exists(markerFile) ? File.ReadAllLines(markerFile).Length : 0;

    /// <summary>
    /// Delivers a change that reaches no plugin, which must complete synchronously.
    /// </summary>
    private static void Notify(ClipboardSession session)
    {
        var run = session.HandleChangeAsync();
        Assert.True(run.IsCompletedSuccessfully, "a change that reaches no plugin must complete synchronously");
    }

    /// <summary>
    /// Copies <paramref name="text"/> in another app, then delivers the change, which must reach no plugin.
    /// </summary>
    private void CopyAndNotify(ClipboardSession session, string text)
    {
        _host.Copy(text);
        Notify(session);
    }

    /// <summary>
    /// Copies <paramref name="text"/> in another app, then starts handling the change without awaiting it.
    /// </summary>
    private Task CopyAndStart(ClipboardSession session, string text)
    {
        _host.Copy(text);
        return session.HandleChangeAsync();
    }

    // ── Built-ins ─────────────────────────────────────────────────────

    [Fact]
    public void RewritesTheCopiedText()
    {
        var session = Start();

        CopyAndNotify(session, "https://example.com/?utm_source=x&id=1");

        Assert.Equal("https://example.com/?id=1", _host.Text);
        Assert.Equal(1, _host.Writes);
    }

    [Fact]
    public void SkipsEveryEventItsOwnWriteRaises()
    {
        var session = Start();
        CopyAndNotify(session, @"C:\Users\foo");

        Notify(session);
        Notify(session);

        // The converters are idempotent, so only the untouched clipboard shows the events were skipped.
        Assert.Equal(1, _host.Reads);
        Assert.Equal(1, _host.Writes);
        Assert.Equal("C:/Users/foo", _host.Text);
    }

    [Fact]
    public void LaterCopyOfItsOwnOutput_IsProcessedAndMovesToTheFrontOfTheHistory()
    {
        var session = Start();
        CopyAndNotify(session, "https://example.com/?utm_source=x&id=1");
        Notify(session);
        CopyAndNotify(session, "something else");

        CopyAndNotify(session, "https://example.com/?id=1");
        CopyAndNotify(session, "{{link}}");

        Assert.Equal("https://example.com/?id=1", _host.Text);
    }

    [Fact]
    public void NeitherTemplatesNorFilledValues_BecomeFillCandidates()
    {
        var session = Start();
        CopyAndNotify(session, "alpha");
        CopyAndNotify(session, "x={{a}}");
        Assert.Equal("x=alpha", _host.Text);
        Notify(session);

        CopyAndNotify(session, "{{b}}");

        Assert.Equal("alpha", _host.Text);
    }

    [Fact]
    public void WhilePaused_NothingIsRewrittenOrRemembered()
    {
        var session = Start();
        _host.IsPaused = true;
        CopyAndNotify(session, "https://example.com/?utm_source=x");
        Assert.Equal("https://example.com/?utm_source=x", _host.Text);

        _host.IsPaused = false;
        CopyAndNotify(session, "{{a}}");

        Assert.Equal("{{a}}", _host.Text);
        Assert.Equal(0, _host.Writes);
    }

    // ── Config and order ──────────────────────────────────────────────

    [Fact]
    public void PicksUpAnEditedConfigFile()
    {
        _config.Write("""{ "convertPaths": false }""");
        var session = Start();
        CopyAndNotify(session, @"C:\Users\foo");
        Assert.Equal(0, _host.Writes);

        _config.Write("""{ "convertPaths": true }""");
        CopyAndNotify(session, @"C:\Users\bar");

        Assert.Equal("C:/Users/bar", _host.Text);
    }

    [Fact]
    public void BrokenConfigAtStartup_IsShownAndTheDefaultsRun()
    {
        _config.Write("{ not json");

        var session = Start();
        CopyAndNotify(session, "https://example.com/?utm_source=x&id=1");

        var notice = Assert.Single(_host.Shown);
        Assert.Equal(NoticeKind.Error, notice.Kind);
        Assert.Equal("https://example.com/?id=1", _host.Text);
    }

    [Fact]
    public void RemovedPluginNamedInTheOrder_IsShownOnceWithConfigUnchanged()
    {
        _plugins.Install("acme-notes", TempPlugins.Manifest("acme-notes"));
        _config.Write("""{ "pipeline": { "order": [ { "id": "acme-notes" } ] } }""");
        var session = Start();
        Assert.Empty(_host.Shown);

        _plugins.Remove("acme-notes");
        CopyAndNotify(session, "one");
        CopyAndNotify(session, "two");

        var notice = Assert.Single(_host.Shown);
        Assert.Contains("acme-notes", notice.Message);
    }

    [Fact]
    public void OrderProblem_IsShownOnceUntilItGoesAwayAndComesBack()
    {
        const string withProblem = """{ "pipeline": { "order": [ { "id": "noSuchModule" } ] } }""";
        _config.Write(withProblem);
        var session = Start();
        Assert.Equal(NoticeKind.Warning, Assert.Single(_host.Shown).Kind);

        CopyAndNotify(session, "one");
        _config.Write(withProblem);
        CopyAndNotify(session, "two");
        Assert.Single(_host.Shown);

        _config.Write("{}");
        CopyAndNotify(session, "three");
        _config.Write(withProblem);
        CopyAndNotify(session, "four");

        Assert.Equal(2, _host.Shown.Count);
    }

    // ── Plugins in the pipeline ───────────────────────────────────────

    [Fact]
    public void PluginRewrite_IsFollowedByABuiltIn() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("acme-rewrite", "^C:", "rewrite");
        _config.Write("""{ "convertPaths": true, "pipeline": { "order": [ { "id": "acme-rewrite", "stop": false }, { "id": "convertPaths" } ] } }""");
        var session = Start();

        await CopyAndStart(session, @"C:\a\b");

        Assert.Equal("C:/a/b (rewritten)", _host.Text);
    });

    [Fact]
    public void Notify_EndsTheRunAtAStop() => UiThread.Run(async () =>
    {
        var marker = MarkerFile("b");
        _plugins.InstallFixture("a-notify", "^go", "notify");
        _plugins.InstallFixture("b-marker", "^go", "marker", marker);
        var session = Start();

        await CopyAndStart(session, "go");

        Assert.Equal(0, Runs(marker));
        var notice = Assert.Single(_host.Shown);
        Assert.Equal(new Notice("Fixture a-notify", "Noted go", NoticeKind.Info), notice);
    });

    [Theory]
    [InlineData("none")]
    [InlineData("error")]
    public void NoneAndError_LetLaterEntriesRun(string mode) => UiThread.Run(async () =>
    {
        var marker = MarkerFile("b");
        _plugins.InstallFixture("a-first", "^go", mode);
        _plugins.InstallFixture("b-marker", "^go", "marker", marker);
        var session = Start();

        await CopyAndStart(session, "go");

        Assert.Equal(1, Runs(marker));
        if (mode == "error")
            Assert.Contains(_host.Shown, n => n.Kind == NoticeKind.Error);
    });

    [Fact]
    public void PluginWhosePatternsDontMatch_IsNeverStarted()
    {
        var marker = MarkerFile("m");
        _plugins.InstallFixture("m-marker", "^never", "marker", marker);
        var session = Start();

        CopyAndNotify(session, "text");

        Assert.Equal(0, Runs(marker));
    }

    [Fact]
    public void DisabledPlugin_IsNeverStarted_AndOneMissingFromTheBlockIs() => UiThread.Run(async () =>
    {
        var marker = MarkerFile("m");
        _plugins.InstallFixture("m-marker", "^go", "marker", marker);
        _config.Write("""{ "plugins": { "m-marker": { "enabled": false } } }""");
        var session = Start();
        CopyAndNotify(session, "go");
        Assert.Equal(0, Runs(marker));

        _config.Write("""{ "plugins": { "other-plugin": { "enabled": false } } }""");
        await CopyAndStart(session, "go again");

        Assert.Equal(1, Runs(marker));
    });

    [Fact]
    public void BuiltInRewrite_IsOnTheClipboardBeforeASlowPluginAnswers() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("slow-notify", "example\\.com", "slow", "500");
        _config.Write("""{ "trackingParams": [ { "params": ["utm_source"] } ], "pipeline": { "order": [ { "id": "urlCleaner", "stop": false } ] } }""");
        var session = Start();

        var run = CopyAndStart(session, "https://example.com/?utm_source=x&id=1");
        Assert.False(run.IsCompleted);
        Assert.Equal("https://example.com/?id=1", _host.Text);

        await run;
        Assert.Equal("Done slowly", Assert.Single(_host.Shown).Message);
    });

    [Fact]
    public void TemplateCopiedWhileASlowRunIsInFlight_FillsFromThatRunsText() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("slow-notify", "^value", "slow", "500");
        var session = Start();

        var run = CopyAndStart(session, "value-A");
        CopyAndNotify(session, "{{t}}");
        Assert.Equal("value-A", _host.Text);

        await run;
    });

    [Fact]
    public void SameTextAgainShortlyAfterAPluginActed_IsSkipped_AndRunsAfterTheWindow() => UiThread.Run(async () =>
    {
        var marker = MarkerFile("m");
        _plugins.InstallFixture("m-marker", "^mark", "marker", marker);
        var session = Start();
        await CopyAndStart(session, "mark-1");

        await Task.Delay(200);
        await CopyAndStart(session, "mark-1");
        Assert.Equal(1, Runs(marker));

        await Task.Delay(2100);
        await CopyAndStart(session, "mark-1");
        Assert.Equal(2, Runs(marker));
    });

    [Fact]
    public void SameTextWhileARunIsInFlight_IsSkipped() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("slow-confirm", "^article", "slow", "300", """{"type":"confirm","title":"T","fields":[],"actions":[{"id":"a","label":"A"}]}""");
        var session = Start();

        var first = CopyAndStart(session, "article-1");
        var duplicate = CopyAndStart(session, "article-1");
        Assert.True(duplicate.IsCompleted);
        await first;

        var proposal = Assert.Single(_host.Opened);
        Assert.Equal("article-1", proposal.Text);
    });

    [Fact]
    public void PluginRewrite_IsDroppedWhenTheClipboardChangedMeanwhile() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("slow-rewrite", "^abc", "slow", "300", """{"type":"rewrite","text":"rewritten"}""");
        var session = Start();

        var run = CopyAndStart(session, "abc");
        _host.Copy("something else");
        await run;

        Assert.Equal("something else", _host.Text);
        Assert.Equal(0, _host.Writes);
    });

    [Fact]
    public void PluginRewrite_IsWrittenWhenTheClipboardStillHoldsTheCopiedText() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("slow-rewrite", "^abc", "slow", "300", """{"type":"rewrite","text":"rewritten"}""");
        var session = Start();

        await CopyAndStart(session, "abc");

        Assert.Equal("rewritten", _host.Text);
    });

    [Fact]
    public void RunThatThrows_IsShownAndLeavesNothingInFlight() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("acme-rewrite", "^boom", "rewrite");
        var session = Start();
        _host.FailNextWrite = true;

        await CopyAndStart(session, "boom");
        Assert.Equal("URL Cleaner hit an error", Assert.Single(_host.Shown).Title);

        await CopyAndStart(session, "boom");
        Assert.Equal("boom (rewritten)", _host.Text);
    });

    [Fact]
    public void Proposal_IsOpenedWithTheTrimmedText() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("acme-confirm", "^https://example\\.com/", "confirm");
        var session = Start();

        await CopyAndStart(session, "https://example.com/articles/1\r\n");

        var proposal = Assert.Single(_host.Opened);
        Assert.Equal("acme-confirm", proposal.Plugin.Id);
        Assert.Equal("https://example.com/articles/1", proposal.Text);
        Assert.Equal("Save article", proposal.Answer.Title);
    });

    [Fact]
    public void ProposalIsDropped_WhenTheAppWasPausedDuringTheCall() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("slow-confirm", "^article", "slow", "300", """{"type":"confirm","title":"T","fields":[],"actions":[{"id":"a","label":"A"}]}""");
        var session = Start();

        var run = CopyAndStart(session, "article-1");
        _host.IsPaused = true;
        await run;

        Assert.Empty(_host.Opened);
    });

    [Fact]
    public void NotifyIsStillShown_WhenTheAppWasPausedDuringTheCall() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("slow-notify", "^article", "slow", "300");
        var session = Start();

        var run = CopyAndStart(session, "article-1");
        _host.IsPaused = true;
        await run;

        Assert.Equal("Done slowly", Assert.Single(_host.Shown).Message);
    });

    [Fact]
    public void NullOrderEntryAtStartup_IsShownAndTheDefaultsRun()
    {
        _config.Write("""{ "pipeline": { "order": [ { "id": "urlCleaner" }, null ] } }""");

        var session = Start();
        CopyAndNotify(session, "https://example.com/?utm_source=x&id=1");

        Assert.Equal("Can't load config.json", Assert.Single(_host.Shown).Title);
        Assert.Equal("https://example.com/?id=1", _host.Text);
    }

    [Fact]
    public void RepeatedCopyShortlyAfterAPluginActed_StillGetsTheBuiltIns() => UiThread.Run(async () =>
    {
        var marker = MarkerFile("m");
        _plugins.InstallFixture("m-marker", "example\\.com", "marker", marker);
        _config.Write("""{ "trackingParams": [ { "params": ["utm_source"] } ], "pipeline": { "order": [ { "id": "urlCleaner", "stop": false } ] } }""");
        var session = Start();
        await CopyAndStart(session, "https://example.com/?utm_source=x&id=1");

        CopyAndNotify(session, "https://example.com/?utm_source=x&id=1");

        Assert.Equal("https://example.com/?id=1", _host.Text);
        Assert.Equal(1, Runs(marker));
    });

    [Fact]
    public void RepeatedCopyWhileAPluginRuns_StillGetsTheBuiltIns() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("slow-notify", "example\\.com", "slow", "400");
        _config.Write("""{ "trackingParams": [ { "params": ["utm_source"] } ], "pipeline": { "order": [ { "id": "urlCleaner", "stop": false } ] } }""");
        var session = Start();

        var run = CopyAndStart(session, "https://example.com/?utm_source=x&id=1");
        CopyAndNotify(session, "https://example.com/?utm_source=x&id=1");
        Assert.Equal("https://example.com/?id=1", _host.Text);
        await run;

        Assert.Equal("https://example.com/?id=1", _host.Text);
    });

    [Fact]
    public void ReloadProblem_IsShownBeforeASlowPluginAnswers() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("slow-notify", "^article", "slow", "400");
        var session = Start();
        _config.Write("{ broken");

        var run = CopyAndStart(session, "article-1");
        Assert.Equal("Can't load config.json", Assert.Single(_host.Shown).Title);
        await run;

        Assert.Equal(["Can't load config.json", "Fixture slow-notify"], _host.Shown.Select(n => n.Title));
    });

    [Fact]
    public void OwnWritePutBackByAnotherApp_IsNotProcessedAgain() => UiThread.Run(async () =>
    {
        _plugins.InstallFixture("acme-rewrite", "^go", "rewrite");
        var session = Start();
        await CopyAndStart(session, "go");
        Assert.Equal("go (rewritten)", _host.Text);

        await CopyAndStart(session, "go (rewritten)");

        Assert.Equal("go (rewritten)", _host.Text);
        Assert.Equal(1, _host.Writes);
    });

    [Fact]
    public void OpenConfirmWindow_IsBroughtForwardInsteadOfCallingThePlugin()
    {
        var marker = MarkerFile("m");
        _plugins.InstallFixture("m-marker", "^mark", "marker", marker);
        var session = Start();
        _host.OpenWindows.Add(("m-marker", "mark-1"));

        CopyAndNotify(session, "mark-1");

        Assert.Equal(0, Runs(marker));
        Assert.Equal([("m-marker", "mark-1")], _host.BroughtForward);
    }
}
