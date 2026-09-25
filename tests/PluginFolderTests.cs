using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

public sealed class PluginFolderTests : IDisposable
{
    private readonly TempPlugins _plugins = new();
    private readonly List<Notice> _notices = [];

    public void Dispose() => _plugins.Dispose();

    [Fact]
    public void MissingFolder_MeansNoPlugins()
    {
        var folder = new PluginFolder(_plugins.Root);

        folder.RefreshIfChanged(_notices);

        Assert.Empty(folder.Plugins);
        Assert.Empty(_notices);
    }

    [Fact]
    public void ValidPluginsLoadSortedById_AndAnInvalidOneIsReported()
    {
        _plugins.Install("zeta-tool", TempPlugins.Manifest("zeta-tool"));
        _plugins.Install("acme-notes", TempPlugins.Manifest("acme-notes"));
        _plugins.Install("broken", "{ not json");
        var folder = new PluginFolder(_plugins.Root);

        Assert.True(folder.RefreshIfChanged(_notices));

        Assert.Equal(["acme-notes", "zeta-tool"], folder.Plugins.Select(p => p.Id));
        var notice = Assert.Single(_notices);
        Assert.Equal(NoticeKind.Error, notice.Kind);
        Assert.StartsWith("plugins/broken/plugin.json:", notice.Message);
    }

    [Fact]
    public void UnchangedFolder_IsNotReadAgain()
    {
        _plugins.Install("broken", "{ not json");
        var folder = new PluginFolder(_plugins.Root);
        folder.RefreshIfChanged(_notices);

        Assert.False(folder.RefreshIfChanged(_notices));
        Assert.Single(_notices);
    }

    [Fact]
    public void InvalidManifest_IsReportedAgainOnlyWhenItChanges()
    {
        _plugins.Install("broken", "{ not json");
        var folder = new PluginFolder(_plugins.Root);
        folder.RefreshIfChanged(_notices);
        _plugins.Install("acme-notes", TempPlugins.Manifest("acme-notes"));
        folder.RefreshIfChanged(_notices);
        Assert.Single(_notices);

        _plugins.Install("broken", "{ still not json");
        folder.RefreshIfChanged(_notices);
        Assert.Equal(2, _notices.Count);

        _plugins.Install("broken", TempPlugins.Manifest("broken"));
        folder.RefreshIfChanged(_notices);
        Assert.Equal(["acme-notes", "broken"], folder.Plugins.Select(p => p.Id));
        Assert.Equal(2, _notices.Count);
    }

    [Fact]
    public void AddedAndRemovedPlugins_AreNoticed()
    {
        var folder = new PluginFolder(_plugins.Root);
        folder.RefreshIfChanged(_notices);

        _plugins.Install("acme-notes", TempPlugins.Manifest("acme-notes"));
        Assert.True(folder.RefreshIfChanged(_notices));
        Assert.Single(folder.Plugins);

        _plugins.Remove("acme-notes");
        Assert.True(folder.RefreshIfChanged(_notices));
        Assert.Empty(folder.Plugins);
    }

    [Fact]
    public void FolderWithoutManifest_IsIgnored()
    {
        Directory.CreateDirectory(Path.Combine(_plugins.Root, "half-installed"));
        var folder = new PluginFolder(_plugins.Root);

        folder.RefreshIfChanged(_notices);

        Assert.Empty(folder.Plugins);
        Assert.Empty(_notices);
    }
}
