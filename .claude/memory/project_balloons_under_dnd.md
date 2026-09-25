---
name: project-balloons-under-dnd
description: Tray balloons vanish under Windows 11 Do Not Disturb; the user chose to leave them as they are
metadata:
  type: project
---

While Windows 11's Do Not Disturb is on, the app's tray balloons (`NotifyIcon.ShowBalloonTip`) are not shown and are not kept in the notification center, so every notice, errors included, is lost; only `url-cleaner.log` has it. The user's machine runs with Do Not Disturb on (`ToastNotificationManager.GetDefault().NotificationMode` read `PriorityOnly` on 2026-09-24), which is why a plugin's `notify` from a confirm window showed nothing.

On 2026-09-24 the user was offered two fixes and chose neither: showing an action's result in the confirm window with an OK button, and a red error badge on the tray icon until the menu is opened. They chose to leave balloons as they are.

**Why:** Do Not Disturb is the user's own choice, and the log keeps everything.

**How to apply:** don't re-propose in-window results or a tray badge as a fix for missing balloons. When the user reports "no balloon", check Do Not Disturb first, as above, before debugging the notifier.
