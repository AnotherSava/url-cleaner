namespace UrlCleaner;

public enum NoticeKind
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Something to tell the user. <see cref="Title"/> and <see cref="Message"/> are never empty.
/// </summary>
public sealed record Notice(string Title, string Message, NoticeKind Kind);

/// <summary>
/// Shows notices to the user. The app's tray balloons are the one implementation today; another app that shows
/// notifications can take over by implementing this.
/// </summary>
public interface INotifier
{
    void Show(Notice notice);
}

/// <summary>
/// Logs every notice before handing it on, so the log records what the user was told whichever notifier shows it, and
/// even when nothing shows it at all: Windows 11 shows no balloon under Do Not Disturb.
/// </summary>
public sealed class LoggedNotifier(INotifier inner) : INotifier
{
    public void Show(Notice notice)
    {
        Logger.Info($"Notice ({notice.Kind}): {notice.Title}: {notice.Message}");
        inner.Show(notice);
    }
}

public static class Notices
{
    /// <summary>
    /// The notice for an exception nothing expected; its details are in the log.
    /// </summary>
    public static Notice UnexpectedError(Exception exception) =>
        new("URL Cleaner hit an error", $"{exception.Message} The log next to config.json has the details.", NoticeKind.Error);

    /// <summary>
    /// Combines everything one clipboard change raised into a single notice. Windows 11 queues balloons, showing each
    /// only after the previous one has gone, so separate ones would arrive seconds apart and late. The first error
    /// leads, or the first notice when none is an error, and the rest are counted. Returns <c>null</c> when there is
    /// nothing to show.
    /// </summary>
    public static Notice? Summarize(IReadOnlyList<Notice> notices)
    {
        if (notices.Count == 0)
            return null;

        var lead = notices.FirstOrDefault(n => n.Kind == NoticeKind.Error) ?? notices[0];
        return notices.Count == 1 ? lead : lead with { Message = $"{lead.Message} (and {notices.Count - 1} more, see the log)" };
    }
}
