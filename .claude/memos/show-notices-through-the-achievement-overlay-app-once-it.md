---
created: 2026-09-24 23:07:57
---

# Show notices through the achievement-overlay app once it accepts requests from other apps

The user's plan (2026-09-24): url-cleaner's notices move off Windows tray balloons and onto the popup of the achievement-overlay app (the games/achievement-overlay repo). That app is already a small WPF tray app with its own Steam-style popup: NotificationWindow, NotificationQueue, placement across monitors, DPI scaling, sound. It has to learn to accept notification requests from other running apps first. That is achievement-overlay's own work, to be done in that repo; no session for it was live when this was captured.

Why: on Windows 11 the balloons fall short. A second balloon queues until the first has gone (seen 3 to 5 seconds late), and under Do Not Disturb balloons are not shown at all and not kept in the notification center. The popup is the app's own window, so neither limit applies.

url-cleaner's side is ready for it: notices go through the INotifier interface in src/Core/Notices.cs, composed in TrayApplicationContext as new LoggedNotifier(new BalloonNotifier(icon)). The work here is one more INotifier that sends each Notice (title, message, kind) to the running achievement-overlay, swapped in at that one line.

To settle when the work starts: how the two apps talk (a named pipe, a localhost port or WM_COPYDATA, whichever achievement-overlay exposes), what url-cleaner does when achievement-overlay isn't running (show balloons instead, or report that notices can't be shown), and how the notice kinds (info, warning, error) map onto the popup's look.
