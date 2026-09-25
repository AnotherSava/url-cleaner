using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

public sealed class ConfigFileTests : IDisposable
{
    private readonly TempConfig _config = new();

    public void Dispose() => _config.Dispose();

    [Fact]
    public void InvalidFileAtStartup_UsesTheEmbeddedDefaultAndLeavesTheFileAlone()
    {
        _config.Write("""{ "convertPaths": true, """);
        var before = File.ReadAllBytes(_config.FilePath);
        var notices = new List<Notice>();

        var file = new ConfigFile(_config.FilePath, notices);

        Assert.Equal(AppConfig.Default().SiteRules.Count, file.Current.SiteRules.Count);
        Assert.False(file.Current.ConvertPaths);
        Assert.Equal(NoticeKind.Error, Assert.Single(notices).Kind);
        Assert.Equal(before, File.ReadAllBytes(_config.FilePath));
    }

    [Fact]
    public void MissingFileAtStartup_IsWrittenFromTheDefault()
    {
        var file = new ConfigFile(_config.FilePath, []);

        Assert.True(File.Exists(_config.FilePath));
        Assert.Equal(AppConfig.Default().SiteRules.Count, file.Current.SiteRules.Count);
    }

    [Fact]
    public void BrokenEdit_KeepsTheLastGoodConfigAndIsReportedOncePerVersion()
    {
        _config.Write("""{ "convertPaths": true }""");
        var file = new ConfigFile(_config.FilePath, []);
        var notices = new List<Notice>();

        _config.Write("{ broken");
        Assert.False(file.ReloadIfChanged(notices));
        Assert.False(file.ReloadIfChanged(notices));
        Assert.True(file.Current.ConvertPaths);
        Assert.Single(notices);

        _config.Write("{ broken again");
        file.ReloadIfChanged(notices);
        Assert.Equal(2, notices.Count);

        _config.Write("""{ "convertPaths": false }""");
        Assert.True(file.ReloadIfChanged(notices));
        Assert.False(file.Current.ConvertPaths);
        Assert.Equal(2, notices.Count);
    }

    [Fact]
    public void LockedFile_IsRetriedSilently()
    {
        _config.Write("""{ "convertPaths": false }""");
        var file = new ConfigFile(_config.FilePath, []);
        _config.Write("""{ "convertPaths": true }""");
        var notices = new List<Notice>();

        using (new FileStream(_config.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(file.ReloadIfChanged(notices));

        Assert.Empty(notices);
        Assert.True(file.ReloadIfChanged(notices));
        Assert.True(file.Current.ConvertPaths);
    }

    [Fact]
    public void UnchangedFile_IsNotReadAgain()
    {
        _config.Write("""{ "convertPaths": true }""");
        var file = new ConfigFile(_config.FilePath, []);

        Assert.False(file.ReloadIfChanged([]));
    }
}
