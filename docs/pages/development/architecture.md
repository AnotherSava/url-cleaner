---
layout: default
title: Architecture
parent: Development
nav_order: 1
---

# Architecture

## Overview

Another URL Cleaner is a .NET WinForms app that runs as a system tray icon using the `ApplicationContext` pattern — no main window, just a `NotifyIcon` with a context menu. Every clipboard change goes through one pipeline of entries: the built-in features, and plugins that run as separate programs.

The code is split in two projects:

- **`src/Core/`** (`UrlCleaner.Core`, plain `net10.0`) holds everything that doesn't need Windows: the config, the features, the pipeline, the clipboard session's rules and the plugin runner. The tests reference only this project.
- **`src/`** (`UrlCleaner`, `net10.0-windows`) holds the Windows parts: the clipboard listener, the tray, the balloons, the confirm window and autostart.

| Component | Files | Responsibility |
|---|---|---|
| **Tray application** | `TrayApplicationContext.cs` | Tray icon, context menu, the feature and plugin checkboxes |
| **Clipboard monitor** | `ClipboardMonitor.cs` | Win32 clipboard listener; reads and writes the clipboard for the session |
| **Clipboard session** | `Core/ClipboardSession.cs` | Handles each change: runs the pipeline, writes the result, keeps the history |
| **Pipeline** | `Core/Pipeline.cs`, `Core/BuiltInModules.cs` | Resolves the order and runs the entries over the copied text |
| **Features** | `Core/UrlSanitizer.cs`, `Core/PathConverter.cs`, `Core/NumberConverter.cs`, `Core/PlaceholderConverter.cs` | The built-in rewrites |
| **Plugins** | `Core/PluginFolder.cs`, `Core/PluginManifest.cs`, `Core/PluginRunner.cs`, `Core/PluginProtocol.cs` | Finds and validates plugins, runs a plugin call as a process |
| **Confirm window** | `ConfirmForm.cs`, `ConfirmWindows.cs` | Renders a plugin's proposal and sends the user's choice back |
| **Config** | `Core/AppConfig.cs`, `Core/ConfigFile.cs` | Config model, loading and hot reload |
| **Notices and log** | `Core/Notices.cs`, `Core/Logger.cs`, `BalloonNotifier.cs` | What the user is told, how it's shown, and `url-cleaner.log` |

## Clipboard monitoring

The app registers as a clipboard format listener using `AddClipboardFormatListener` (Win32 API). When any application changes the clipboard, Windows sends a `WM_CLIPBOARDUPDATE` message to the monitor's invisible `NativeWindow`, which hands it to the session without awaiting it.

### Flow

1. Skip the change while the app is paused, or while the clipboard's change count still equals the count right after the app's own last write (see [Skipping the app's own writes](#skipping-the-apps-own-writes)).
2. Reload `config.json` and rescan `plugins/` if either changed, and show any problem found straight away.
3. Read the clipboard text and run the pipeline over it.
4. Write the result back if it changed, and remember it as a placeholder fill candidate.

A change that reaches no plugin completes synchronously, within the message handler.

### Skipping the app's own writes

Every write the app makes raises at least one `WM_CLIPBOARDUPDATE` of its own, and some writes raise several. Right after each write the session records the clipboard's change count (`GetClipboardSequenceNumber`), and it skips every change arriving while the count still equals it. A later copy of the same text moves the count and is processed normally, so re-copying a cleaned URL puts it back at the front of the placeholder history.

Text that equals the app's last write and arrives within 2 seconds, after another app put it back, runs the built-in features only. They change nothing on a second pass, and a plugin never sees its own output.

## Pipeline

The pipeline's entries are the built-in features and the installed plugins. By default they run in that order, the plugins sorted by id, and the first entry that matches ends the run. The `pipeline` block in `config.json` reorders them and can let a matched entry pass its result on (see [Feature order](../configuration#feature-order)).

- **A built-in matches** when it returns rewritten text. Built-ins are synchronous and pure.
- **A plugin is called** only when one of its manifest's patterns matches the copied text, trimmed. It matches when it answers with a rewrite, a notification or a confirm proposal. An answer of "nothing to do", an error, a timeout or a crash are not matches, and the next entry runs.
- **Later entries see the rewritten text**, and the run ends at a matched entry whose step has `stop` set.

Order resolution, in `Pipeline.ResolveOrder`, turns the configured order into steps: listed entries first, then the unlisted ones in the default order with `stop` set. Unknown and repeated ids are skipped and reported once, until they go away and come back.

## Plugins

A plugin is a separate program in any language, installed by its own repository into `plugins/<id>/` next to the executable with a `plugin.json` manifest. The manifest names the plugin, lists the patterns that select its text, and gives the command to run. The request and answer format is not documented publicly yet and may still change.

- **Discovery**: the session rereads the manifests when one is added, removed or changed. An invalid manifest leaves its plugin out and is reported once per version of the file.
- **A call is one process**, started with no console window and fed a JSON request on stdin; it answers with one JSON object on stdout. The call has a deadline from the manifest, after which the plugin and everything it started are ended. Stdout is capped at 1 MB.
- **Processes end with the app**: on Windows every plugin process joins a job object that kills its members when the app exits, however it exits. Each call also has a job of its own, which ends a process the plugin left holding the call's pipes.
- **Turning a plugin off**: the tray menu has a checkbox per installed plugin, which writes `plugins.<id>.enabled` in `config.json`. A plugin missing from the `plugins` block is enabled.

### Rules while a plugin runs

While a plugin call is awaited, the UI thread keeps pumping messages, so another clipboard change can start a second run before the first has finished. The session keeps these rules:

- **Built-in rewrites are written first**, before the run waits on a plugin, so a paste made meanwhile gets the cleaned text.
- **The history records the text** when the run first waits, so a placeholder template copied meanwhile can already use it.
- **A late plugin rewrite is written only where it belongs**: when the clipboard still holds what the run last put there, or the copied text, and the app isn't paused.
- **A repeated copy skips the plugins**: the same text while its run is in flight, or within 2 seconds after a plugin notified or proposed, runs the built-in features only. Some apps send several clipboard updates per copy, and a plugin that acts would otherwise act twice.
- **One confirm window per plugin and text**: copying the same text again brings the open window forward instead of calling the plugin.
- **Proposals are checked before they open**: a proposal is dropped when the app was paused or the plugin was removed or turned off while it ran.
- **Nothing escapes a run**: an exception is logged and shown, and the run's in-flight entry is cleared.

### Confirm window

A plugin can answer with a proposal instead of acting: a title, a message, editable fields and action buttons. The confirm window renders it with the host's own Cancel button. Pressing an action looks the plugin up again, then sends a second call with the action and the field values; the window closes when the plugin notifies and shows an error in place otherwise. It can't be closed while that call runs.

## Notices and the log

Notices reach the user through `INotifier`. Tray balloons are its one implementation today, and every notice passes through `LoggedNotifier` first, so the log records it whichever notifier shows it. Another app that shows notifications can take over by implementing the interface.

Everything one clipboard change raises becomes a single notice: the first error leads, and the rest are counted. Windows 11 queues balloons, showing each only after the previous one has gone, so separate ones would arrive seconds late. Problems with `config.json` or `plugins/` are shown as soon as the change that found them arrives. Windows 11 doesn't show balloons at all while Do Not Disturb is on.

The log, `url-cleaner.log` next to `config.json`, has the full detail behind every notice, a plugin's stderr, and every unhandled exception. At startup the previous log is renamed to `url-cleaner.old.log`, so the log that explains a crash survives one restart. Writing to the log never throws.

## How a URL is cleaned

The cleaner, `UrlSanitizer.TryClean`, processes a URL through these stages in order:

### 1. Validation

- Parse with `Uri.TryCreate`
- Reject non-HTTP(S) schemes
- Match domain against site rules by suffix

### 2. Path cleaning

Applied in order when the matching site rule specifies them:

| Rule | Effect |
|---|---|
| `keepPathFrom` | Discard path segments before the anchor |
| `stripPathSegments` | Remove segments matching prefix patterns |
| `stripSlugs` | Strip `{id}-{slug}` down to `{id}` |
| `stripPathIndex` | Remove segments at specific indices |

### 3. Query cleaning

Either strip all params (`stripAllParams`) keeping only `excludedParams`, or strip only known tracking params plus `additionalParams` minus `excludedParams`.

### 4. Fragment cleaning

Remove the `#...` fragment if `stripFragment` is set.

### 5. Result

If any stage changed the URL, return the rebuilt URL. Otherwise return `null` (no change).

## Configuration

Config is loaded from `config.json` next to the executable. If the file doesn't exist, it's generated from `default.json`, embedded as a .NET resource in `UrlCleaner.Core.dll`. The session checks the file's last-write time at every clipboard change and reloads it when it changed.

A version of the file that doesn't load, including one holding a null list or a null inside one, is reported once and leaves the last good config in use: the embedded default when it happens at startup. The file is tried again at every change until it loads.

## Autostart

The "Start with Windows" option writes to `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` from `Autostart.cs` — no config file field, registry-only.

## Single instance

A named mutex lets only one instance run: a second one would handle every copy twice.
