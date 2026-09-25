using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

// The logger is process-wide, so these tests run alone: a test elsewhere writing to it mid-test would land in the
// file these assertions read.
[CollectionDefinition(nameof(LoggerTests), DisableParallelization = true)]
public class LoggerCollection;

[Collection(nameof(LoggerTests))]
public sealed class LoggerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"url-cleaner-test-{Guid.NewGuid():N}");

    public LoggerTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string LogPath => Path.Combine(_folder, Logger.FileName);

    private string PreviousLogPath => Path.Combine(_folder, Logger.PreviousFileName);

    [Fact]
    public void Start_MovesThePreviousLogAsideAndBeginsAnEmptyOne()
    {
        Logger.Start(_folder);
        Logger.Info("first session");

        Logger.Start(_folder);

        Assert.Contains("first session", File.ReadAllText(PreviousLogPath));
        Assert.False(File.Exists(LogPath));

        Logger.Warn("second session");
        var log = File.ReadAllText(LogPath);
        Assert.Contains("WARN second session", log);
        Assert.DoesNotContain("first session", log);
    }

    [Fact]
    public void Error_IncludesTheException()
    {
        Logger.Start(_folder);

        Logger.Error("Something failed", new InvalidOperationException("the detail"));

        Assert.Contains("the detail", File.ReadAllText(LogPath));
    }

    [Fact]
    public void HalfASurrogatePair_IsWrittenReplaced()
    {
        Logger.Start(_folder);

        Logger.Info("cut emoji \ud83c here");

        Assert.Contains("cut emoji", File.ReadAllText(LogPath));
    }

    [Fact]
    public void WritingNeverThrows_WhenTheFolderIsGone()
    {
        Logger.Start(Path.Combine(_folder, "missing"));

        Logger.Info("nowhere to go");
    }
}
