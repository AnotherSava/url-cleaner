using System.Diagnostics;
using System.Text.Json;
using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

/// <summary>
/// Runs fixtures/plugins/fixture.js through the real runner, one mode per plugin. These tests need Node on PATH.
/// </summary>
public sealed class PluginRunnerTests : IDisposable
{
    private static readonly string FixturesRoot = Path.GetDirectoryName(TempPlugins.FixtureScript)!;

    private readonly TempPlugins _plugins = new();
    private int _count;

    public void Dispose() => _plugins.Dispose();

    /// <summary>
    /// Installs a plugin that runs the fixture script in <paramref name="mode"/>.
    /// </summary>
    private PluginManifest Fixture(string mode, string? parameter = null, int timeoutSeconds = 10) =>
        _plugins.InstallFixture($"fixture-{++_count}", ".", timeoutSeconds, parameter == null ? [mode] : [mode, parameter]);

    private static T AnswerOf<T>(PluginOutcome outcome) where T : PluginAnswer =>
        Assert.IsType<T>(Assert.IsType<Answered>(outcome).Answer);

    private static string ReasonOf(PluginOutcome outcome) => Assert.IsType<Failed>(outcome).Reason;

    // ── Answers ───────────────────────────────────────────────────────

    [Fact]
    public async Task None()
    {
        AnswerOf<NoneAnswer>(await PluginRunner.CopyAsync(Fixture("none"), "text"));
    }

    [Fact]
    public async Task Rewrite_CarriesCyrillicAndEmojiBothWays()
    {
        var answer = AnswerOf<RewriteAnswer>(await PluginRunner.CopyAsync(Fixture("rewrite"), "Терапия 🎬"));

        Assert.Equal("Терапия 🎬 (rewritten)", answer.Text);
    }

    [Fact]
    public async Task Notify()
    {
        Assert.Equal("Noted text", AnswerOf<NotifyAnswer>(await PluginRunner.CopyAsync(Fixture("notify"), "text")).Message);
    }

    [Fact]
    public async Task Error()
    {
        Assert.Equal("The fixture refused", AnswerOf<ErrorAnswer>(await PluginRunner.CopyAsync(Fixture("error"), "text")).Message);
    }

    [Fact]
    public async Task Confirm_ThenTheActionCallCarriesFieldsAndState()
    {
        var plugin = Fixture("confirm");

        var proposal = AnswerOf<ConfirmAnswer>(await PluginRunner.CopyAsync(plugin, "https://example.com/articles/1"));
        Assert.Equal("Save article", proposal.Title);
        Assert.Equal("https://example.com/articles/1", proposal.Message);
        Assert.Equal(["folder", "tags"], proposal.Fields.Select(f => f.Id));
        Assert.Equal(["Reading list", "Archive"], proposal.Fields[0].Options);
        Assert.Null(proposal.Fields[1].Options);
        Assert.Equal("save", Assert.Single(proposal.Actions).Id);

        var fields = new Dictionary<string, string> { ["folder"] = "Archive", ["tags"] = "" };
        var outcome = await PluginRunner.ActionAsync(plugin, "https://example.com/articles/1", "save", fields, proposal.State);

        Assert.Equal("save: Archive (1234)", AnswerOf<NotifyAnswer>(outcome).Message);
    }

    // ── Invalid answers ───────────────────────────────────────────────

    [Theory]
    [InlineData("", "wrote nothing")]
    [InlineData("hello", "isn't one JSON value")]
    [InlineData("""{"type":"none"}{"type":"none"}""", "isn't one JSON value")]
    [InlineData("[]", "isn't a JSON object")]
    [InlineData("""{"text":"x"}""", "no type")]
    [InlineData("""{"type":"launch"}""", "unknown type")]
    [InlineData("""{"type":"rewrite","text":""}""", "\"text\"")]
    [InlineData("""{"type":"rewrite","text":42}""", "\"text\"")]
    [InlineData("""{"type":"notify","message":""}""", "\"message\"")]
    [InlineData("""{"type":"error"}""", "\"message\"")]
    [InlineData("""{"type":"confirm","title":"T","fields":[],"actions":[]}""", "no actions")]
    [InlineData("""{"type":"confirm","title":"","fields":[],"actions":[{"id":"a","label":"A"}]}""", "\"title\"")]
    [InlineData("""{"type":"confirm","title":"T","actions":[{"id":"a","label":"A"}]}""", "\"fields\"")]
    [InlineData("""{"type":"confirm","title":"T","fields":[{"id":"","label":"F"}],"actions":[{"id":"a","label":"A"}]}""", "\"id\"")]
    [InlineData("""{"type":"confirm","title":"T","fields":[{"id":"f","label":"F"},{"id":"f","label":"G"}],"actions":[{"id":"a","label":"A"}]}""", "two fields")]
    [InlineData("""{"type":"confirm","title":"T","fields":[],"actions":[{"id":"a","label":"A"},{"id":"a","label":"B"}]}""", "two actions")]
    [InlineData("""{"type":"confirm","title":"T","fields":[{"id":"f","label":"F","options":[1]}],"actions":[{"id":"a","label":"A"}]}""", "\"options\"")]
    public async Task InvalidAnswer_FailsWithTheRuleItBroke(string output, string expected)
    {
        var reason = ReasonOf(await PluginRunner.CopyAsync(Fixture("raw", output), "text"));

        Assert.StartsWith("gave an invalid answer", reason);
        Assert.Contains(expected, reason);
    }

    [Theory]
    [InlineData("""{"type":"none"}""")]
    [InlineData("""{"type":"rewrite","text":"x"}""")]
    [InlineData("""{"type":"confirm","title":"T","fields":[],"actions":[{"id":"a","label":"A"}]}""")]
    public async Task ActionCall_AcceptsOnlyNotifyOrError(string output)
    {
        var reason = ReasonOf(await PluginRunner.ActionAsync(Fixture("raw", output), "text", "a", new Dictionary<string, string>(), null));

        Assert.Contains("only notify or error", reason);
    }

    // ── Failures ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExitWithoutReadingStdin_IsReportedByItsExitCode()
    {
        Assert.Equal("exited with code 3", ReasonOf(await PluginRunner.CopyAsync(Fixture("exit-3"), "text")));
    }

    // The child's end doesn't prove the runner killed the whole tree: libuv puts a Node plugin's children in a job that
    // ends them when Node dies. The tree kill is for plugins in other languages.
    [Fact]
    public async Task Hang_IsKilledAtTheDeadlineAndItsChildEnds()
    {
        var pidFile = Path.Combine(Path.GetDirectoryName(_plugins.Root)!, "hang.pid");
        var plugin = Fixture("hang", pidFile, timeoutSeconds: 1);
        var clock = Stopwatch.StartNew();

        var reason = ReasonOf(await PluginRunner.CopyAsync(plugin, "text"));

        Assert.Equal("didn't answer within 1 second", reason);
        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        var pids = File.ReadAllText(pidFile).Split(' ').Select(int.Parse).ToList();
        Assert.True(await HasExited(pids[0]), $"plugin process {pids[0]} is still running");
        Assert.True(await HasExited(pids[1]), $"child process {pids[1]} is still running");
    }

    [Fact]
    public async Task StderrFloodBeforeReadingStdin_DoesNotDeadlock()
    {
        var clock = Stopwatch.StartNew();

        AnswerOf<NoneAnswer>(await PluginRunner.CopyAsync(Fixture("stderr-flood"), "text"));

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task StdoutFlood_IsCutOffAtTheLimit()
    {
        var clock = Stopwatch.StartNew();

        Assert.Equal("wrote more than 1 MB to stdout", ReasonOf(await PluginRunner.CopyAsync(Fixture("stdout-flood"), "text")));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task LoneSurrogateInAnAnswer_IsAnInvalidAnswer()
    {
        var reason = ReasonOf(await PluginRunner.CopyAsync(Fixture("raw", """{"type":"notify","message":"Saved \ud83c"}"""), "text"));

        Assert.Contains("\"message\" isn't valid text", reason);
    }

    [Fact]
    public async Task EmojiInStderr_DoesNotSpoilAValidAnswer()
    {
        AnswerOf<NoneAnswer>(await PluginRunner.CopyAsync(Fixture("emoji-stderr"), "text"));
    }

    // cmd exits at once, leaving ping holding its pipes. Node can't be the plugin here: a Node process's children, and
    // theirs, end when it does.
    [Fact]
    public async Task ProcessThatKeepsThePipesOpenAfterThePluginExits_IsEndedWithinTheGrace()
    {
        var started = DateTime.Now;
        var json = JsonSerializer.Serialize(new { protocol = 1, id = "pipe-holder", name = "Pipe holder", match = new { patterns = new[] { "." } }, run = new[] { "cmd.exe", "/c", "start /b ping -n 30 127.0.0.1 & exit /b 0" } });
        var plugin = PluginManifest.Parse(json, Path.GetDirectoryName(_plugins.Install("pipe-holder", json))!);
        var clock = Stopwatch.StartNew();

        // Bigger than the pipe's buffer, so the write can't finish while ping holds stdin without reading it.
        var reason = ReasonOf(await PluginRunner.CopyAsync(plugin, new string('x', 200_000)));

        Assert.StartsWith("its output didn't end within 2 seconds", reason);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(6), $"took {clock.Elapsed}");
        await Task.Delay(500);
        Assert.DoesNotContain(Process.GetProcessesByName("PING"), p => p.StartTime >= started.AddSeconds(-1));
    }

    [Fact]
    public async Task MissingProgram_IsNamed()
    {
        var json = JsonSerializer.Serialize(new { protocol = 1, id = "no-program", name = "No program", match = new { patterns = new[] { "." } }, run = new[] { "no-such-program-for-url-cleaner" } });
        var plugin = PluginManifest.Parse(json, Path.GetDirectoryName(_plugins.Install("no-program", json))!);

        Assert.Contains("can't find \"no-such-program-for-url-cleaner\"", ReasonOf(await PluginRunner.CopyAsync(plugin, "text")));
    }

    // ── The installable fixtures ──────────────────────────────────────

    [Theory]
    [InlineData("fixture-rewrite")]
    [InlineData("fixture-notify")]
    [InlineData("fixture-confirm")]
    [InlineData("fixture-error")]
    [InlineData("fixture-hang")]
    public void InstallableFixture_IsAValidPlugin(string id)
    {
        var folder = Path.Combine(FixturesRoot, id);
        var plugin = PluginManifest.Parse(File.ReadAllText(Path.Combine(folder, PluginFolder.ManifestFileName)), folder);

        Assert.True(plugin.Matches($"https://fixture.example/{id["fixture-".Length..]}"));
        Assert.NotNull(plugin.FindExecutable(Environment.GetEnvironmentVariable("PATH")));
    }

    [Fact]
    public async Task InstallableRewriteFixture_RunsFromItsOwnFolder()
    {
        var folder = Path.Combine(FixturesRoot, "fixture-rewrite");
        var plugin = PluginManifest.Parse(File.ReadAllText(Path.Combine(folder, PluginFolder.ManifestFileName)), folder);

        Assert.Equal("x (rewritten)", AnswerOf<RewriteAnswer>(await PluginRunner.CopyAsync(plugin, "x")).Text);
    }

    private static async Task<bool> HasExited(int pid)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                    return true;
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }
}
