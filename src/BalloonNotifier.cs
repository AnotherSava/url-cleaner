namespace UrlCleaner;

/// <summary>
/// Shows notices as balloons on the tray icon, which Windows 10 and 11 render as toasts.
/// </summary>
public sealed class BalloonNotifier(NotifyIcon icon) : INotifier
{
    // Windows cuts balloon titles at 64 characters and text at 256; shortening earlier keeps the cut on our side.
    private const int TitleLimit = 48;
    private const int TextLimit = 200;

    public void Show(Notice notice)
    {
        var tipIcon = notice.Kind switch
        {
            NoticeKind.Error => ToolTipIcon.Error,
            NoticeKind.Warning => ToolTipIcon.Warning,
            _ => ToolTipIcon.Info
        };
        icon.ShowBalloonTip(10_000, Shorten(notice.Title, TitleLimit), Shorten(notice.Message, TextLimit), tipIcon);
    }

    private static string Shorten(string text, int limit) => text.Length <= limit ? text : text[..(limit - 1)] + "…";
}
