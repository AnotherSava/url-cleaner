using UrlCleaner;
using Xunit;

namespace UrlCleaner.Tests;

public class NoticesTests
{
    [Fact]
    public void NoNotices_ShowNothing()
    {
        Assert.Null(Notices.Summarize([]));
    }

    [Fact]
    public void SingleNotice_ShowsAsItself()
    {
        var notice = new Notice("Title", "Message.", NoticeKind.Info);

        Assert.Equal(notice, Notices.Summarize([notice]));
    }

    [Fact]
    public void SeveralNotices_LeadWithTheFirstErrorAndCountTheRest()
    {
        var summary = Notices.Summarize(
        [
            new Notice("Saved", "Article saved.", NoticeKind.Info),
            new Notice("Can't load config.json", "Bad JSON.", NoticeKind.Error),
            new Notice("Later error", "Another.", NoticeKind.Error)
        ]);

        Assert.Equal(new Notice("Can't load config.json", "Bad JSON. (and 2 more, see the log)", NoticeKind.Error), summary);
    }

    [Fact]
    public void SeveralNotices_WithoutAnError_LeadWithTheFirst()
    {
        var summary = Notices.Summarize(
        [
            new Notice("First", "One.", NoticeKind.Warning),
            new Notice("Second", "Two.", NoticeKind.Info)
        ]);

        Assert.Equal(new Notice("First", "One. (and 1 more, see the log)", NoticeKind.Warning), summary);
    }
}
