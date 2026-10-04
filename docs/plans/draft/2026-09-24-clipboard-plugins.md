# Clipboard plugins

## Overview

Every clipboard feature is wired into the app by hand in two places: a branch in the monitor's first-match chain and a checkbox in the tray menu. That works for small in-process text rewrites. The next feature doesn't fit it. It belongs to another project, What's Next: it acts on text copied from a web page, calls that project's server, has side effects, and has to ask before acting.

This plan turns the app into a host that runs two kinds of entries through one pipeline:

- **Built-in modules**: the existing C# features, running in-process behind one internal interface.
- **Process plugins**: external programs in any language, installed into the host's `plugins/` folder by their own repo. Each one declares in a JSON manifest which clipboard text it wants, and the host talks to it with JSON over stdin/stdout.

The user sets the order of all entries, and for each one whether processing stops once it has matched.

This plan covers the host only. The first plugin, a download plugin, has its own plan, which belongs in the What's Next repo; that repo builds and owns the plugin.

Unverified claims are marked *(unverified)*. Every other claim about an external system was checked during research on 2026-09-24, and the sources are listed under Context.

## Context

- Files involved:
  - Create: `src/Core/UrlCleaner.Core.csproj`, a `net10.0` class library holding everything that doesn't need Windows
  - Move to `src/Core/`: `AppConfig.cs` (without the autostart methods), `UrlSanitizer.cs`, `PathConverter.cs`, `NumberConverter.cs`, `PlaceholderConverter.cs`
  - Create in `src/Core/`: `BuiltInModules.cs`, `Pipeline.cs`, `ClipboardHistory.cs`, `ClipboardSession.cs`, `ConfigFile.cs`, `Notices.cs`, `PluginManifest.cs`, `PluginFolder.cs`, `PluginProtocol.cs`, `PluginRunner.cs`, `Logger.cs`
  - Create: `src/Autostart.cs` (the registry helpers moved out of `AppConfig`), `src/ConfirmForm.cs`, `src/Notifier.cs`
  - Modify: `src/Program.cs`, `src/ClipboardMonitor.cs`, `src/TrayApplicationContext.cs`, `src/UrlCleaner.csproj`, `url-cleaner.slnx`, `tests/UrlCleaner.Tests.csproj`, `.github/workflows/build.yml`
  - Create: `tests/PipelineTests.cs`, `tests/ClipboardSessionTests.cs`, `tests/AppConfigTests.cs`, `tests/LoggerTests.cs`, `tests/PluginManifestTests.cs`, `tests/PluginRunnerTests.cs`, `tests/fixtures/plugins/`
  - Modify: `docs/pages/configuration.md`, `docs/pages/development.md`, `docs/pages/development/architecture.md`
- Related patterns: `UrlSanitizer.TryClean` and the converters return `null` when nothing changed. `ClipboardMonitor.ReloadConfigIfChanged` hot-reloads the config by checking its mtime.
- Dependencies: none new in the host. Node 24 on PATH for the plugin tests.

### Decisions this plan implements

The user made these decisions on 2026-09-24. The plan builds them as stated.

- **D1** Built-in modules behind one internal interface, plus external process plugins. The host evaluates each plugin's match rules, so nothing is spawned for a copy that doesn't match. Host and plugin exchange JSON over stdin/stdout.
- **D2** Plugins are headless and platform-neutral, and the protocol carries no Win32 concepts. The built-in logic moves into a plain `net10.0` library that a future macOS host could reuse.
- **D3** Text content only. The request carries a `kind` field so file lists can be added later without breaking plugins.
- **D4** Each plugin decides whether to confirm before acting. When it does, it returns a proposal and the host draws the window, then calls the plugin a second time with the chosen action and the field values.
- **D5** The only user is the owner for now, but others may extend it later. The manifest carries a protocol version from day one. There are no public protocol docs and no compatibility promise yet.
- **D6** The user orders all entries and sets a per-entry stop flag. "Matched" means the entry produced a rewrite, an action or a confirm window. Later entries see rewritten text. A config without the order list falls back to today's order with stop set on every built-in.
- **D7** Plugins live in `<install>/plugins/<id>/plugin.json`, and each plugin's repo deploys into that folder.
- **D8** Today's `config.json` keys stay as they are, and a new `plugins` block holds each plugin's enabled flag. Each plugin gets a tray checkbox that writes its flag, like the converter toggles. A plugin's own settings live in its folder.

D9 onward settle the download plugin and are recorded in its plan.

### Sources

- `Process.Kill`: https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.kill
- `CreateProcessW`: https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessw
- BatBadBut: https://flatt.tech/research/posts/batbadbut-you-cant-securely-execute-commands-on-windows/
- Job objects: https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_limit_information
- `NotifyIcon.ShowBalloonTip` (remarks on a second balloon, and the empty-text exception): https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon.showballoontip
- `Clipboard.SetText` (exceptions): https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.clipboard.settext
- `NOTIFYICONDATAW`: https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw
- `SetForegroundWindow`: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow
- `GetClipboardSequenceNumber`: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardsequencenumber
- Node child processes: https://nodejs.org/api/child_process.html
- Node process I/O: https://github.com/nodejs/node/blob/v24.x/doc/api/process.md#a-note-on-process-io
- A scratch WinForms and Node probe run on this machine on 2026-09-24, since deleted
- This repo's code, and the global deploy script (read only)

## Development Approach

- Testing: xUnit in `tests/`
- Gate: run `bash .claude/commit-checks.sh` after each task. It runs the conventions checker and the same restore, build and test steps as CI. From Task 6 onward the tests start `node`, so a missing Node fails the gate instead of skipping it
- Complete each task fully before moving to the next
- The download plugin's plan needs protocol v1 from Tasks 6–8 before it can test end to end

## Design Notes

### Built-in modules

Built-ins are rewrites: synchronous, pure, and fast. Every built-in is an instance of one record type, and a single list in pipeline order holds all of them. The fallback order is read from that list:

```csharp
public sealed record ModuleContext(AppConfig Config, IReadOnlyList<string> History);

public sealed record BuiltInModule(
    string Id,
    Func<AppConfig, bool> IsEnabled,
    Func<string, ModuleContext, string?> TryRewrite);   // null: not matched

public static class BuiltInModules
{
    public static readonly IReadOnlyList<BuiltInModule> All =
    [
        new("urlCleaner", _ => true, (text, ctx) => UrlSanitizer.TryClean(text, ctx.Config)),
        new("convertPaths", c => c.ConvertPaths, (text, _) => PathConverter.TryConvert(text)),
        new("convertNumbers", c => c.ConvertNumbers, (text, _) => NumberConverter.TryConvert(text)),
        new("convertPlaceholders", c => c.ConvertPlaceholders, (text, ctx) => PlaceholderConverter.TryConvert(text, ctx.History)),
    ];
}
```

- Each converter id is also its config key, so the existing tray toggles keep writing the keys they write today (D8).
- The converter toggles stay hand-written. Each installed plugin gets a checkbox of its own (see "Plugin toggles"), and "Pause cleaning" still turns everything off, plugins included.
- The URL cleaner has no enable key and no tray toggle today, and it gets neither.
- The placeholder converter needs the clipboard history, which reaches it through `ModuleContext.History`: a snapshot taken when the run starts, so a run finishing in between can't change it. The history buffer and its rule move into `ClipboardHistory` in Core: it keeps the last 10 distinct values, most recent first, and never stores a placeholder template or a value produced by filling one.

### The pipeline

The pipeline runs the enabled entries in order over the copied text. A plugin is enabled unless the `plugins` block sets its `enabled` to `false`:

1. A built-in whose `TryRewrite` returns text has **matched**. The text passes on in rewritten form.
2. For a plugin, the host checks the manifest's match patterns first. When none fits, the plugin is skipped and nothing is spawned (D1). When one fits, the host calls the plugin. The plugin has matched if it answers `rewrite`, `notify` or `confirm`. An answer of `none` or `error`, a timeout, or a crash counts as not matched. An error is still reported to the user (see "Notifications and the log").
3. When an entry has matched and its `stop` flag is set, the run ends. Otherwise the next entry sees the current text, rewritten or not.

The run produces the final text (with a flag saying whether it differs from the copied text), the notices to show, the confirm proposals to open, and whether the placeholder module matched. Core decides what happens and the host only renders it, so a future macOS host reuses the whole flow.

**Resolving the order.** The optional `pipeline.order` list in `config.json` names entries by id, each with a `stop` flag:

- Without the list, the order is `BuiltInModules.All` followed by plugins sorted by id, with `stop` set on every entry. With no plugins installed this is today's first-match chain, so existing installs behave as they do now (D6).
- Entries the list doesn't name run after the listed ones, built-ins in `BuiltInModules.All` order first, then plugins by id, all with `stop` set.
- An entry that omits `stop` has it set, like the fallback. The model declares `public bool Stop { get; init; } = true;`, because C#'s default for a missing `bool` would silently turn a reordering into fall-through.
- An id in the list that matches no built-in or installed plugin is skipped. A repeated id keeps its first position. Each such (id, problem) pair is logged and shown once, and shown again only if it goes away and comes back.

The host resolves the order again whenever the mtime of `config.json` or the set of installed plugins changes. So removing a plugin that the list names is reported even though `config.json` didn't change. The order and the stop flags are edited in `config.json`, and hot reload picks them up.

### Plugin discovery and the manifest

The host reads `plugins/<id>/plugin.json` under the folder that holds the executable (D7). It rereads the plugin set when a clipboard event arrives, but only if the list of manifests or their mtimes changed. This is the same lazy approach `config.json` already uses, with no watcher to manage. An unreadable or invalid manifest leaves that plugin out, gets logged, and is reported once for each mtime of the file. An installer should write `plugin.json` last, through a temporary file and a rename, so the host never reads a half-installed plugin.

```json
{
  "protocol": 1,
  "id": "example-notes",
  "name": "Article notes",
  "match": {
    "patterns": ["^https://example\\.com/articles/\\d+$"]
  },
  "run": ["node", "dist/plugin.js"],
  "timeoutSeconds": 30
}
```

- `protocol`: the host accepts any version from 1 up to its own and refuses a higher one, naming both (D5). The host built by this plan speaks version 1.
- `id`: lowercase kebab-case, equal to the folder name. Every repo that installs a plugin shares the `plugins/` folder, so an id is named after its project (`acme-notes`, not `notes`). Lowercase kebab-case can never equal a camelCase built-in id.
- `name`: the title of the plugin's notifications, in sentence case.
- `match.patterns`: .NET regular expressions with `CultureInvariant` and a 100 ms match timeout, so a bad pattern can't freeze the UI thread. A pattern that times out counts as no match and is logged, and a pattern that doesn't compile fails the manifest. The patterns match text only. A later protocol version can add rules for other kinds of content, and a plugin whose rules name no kind keeps receiving only text (D3).
- `run`: an argv array. The working directory is the plugin folder.
- `timeoutSeconds`: the limit for each call, a whole number from 1 to 300. The default is 10. A value outside the range fails the manifest with a message naming the field.

Patterns are tested against the copied text with surrounding whitespace trimmed, and both calls carry that trimmed text. A line copied from an editor, a terminal or a chat often ends in CRLF, and .NET's `$` matches before a final `\n` but not before `\r\n`.

### Protocol v1

Each call starts one process. The host writes one JSON object to stdin and closes it, then reads one JSON object from stdout. It captures stderr for the log. Nothing in either direction mentions Win32 (D2).

The **copy call** is sent when the patterns match:

```json
{ "protocol": 1, "type": "copy", "kind": "text", "text": "https://example.com/articles/1234" }
```

The plugin answers with one of these:

| `type` | Other fields | Pipeline effect |
|---|---|---|
| `none` | — | Not matched: the text wasn't the plugin's after all |
| `rewrite` | `text`, non-empty | Matched. Later entries see `text`, and the host puts it on the clipboard |
| `notify` | `message`, non-empty | Matched: the plugin acted, and the host shows `message` |
| `confirm` | `title`, `message?`, `fields`, `actions`, `state?` | Matched. The host opens a confirm window |
| `error` | `message`, non-empty | Not matched. The host shows `message` as an error |

A confirm proposal needs a non-empty `title` and at least one action. Every field id and action id must be non-empty and unique within the proposal, and every field and action needs a non-empty `label`. An answer that breaks any rule in this section is a protocol error. The host checks this before touching the clipboard or the tray, because `Clipboard.SetText` throws on an empty string and `ShowBalloonTip` throws on empty text.

A confirm proposal looks like this:

```json
{
  "type": "confirm",
  "title": "Save article",
  "message": "Why clipboards are hard",
  "fields": [
    { "id": "folder", "label": "Folder", "value": "Reading list", "options": ["Reading list", "Archive"] },
    { "id": "tags", "label": "Tags", "value": "" }
  ],
  "actions": [{ "id": "save", "label": "Save" }],
  "state": { "articleId": 1234 }
}
```

A field is always editable text. When `options` is present, the host shows those values as a drop-down on an editable box, so a pick fills the box and the user can still type. The host adds its own Cancel button. The plugin's `actions` are the other buttons, and the first one is the default. `state` is an opaque value the host sends back unchanged, so a stateless plugin doesn't have to fetch again what it fetched for the proposal.

The **action call** is sent when the user presses one of the plugin's buttons:

```json
{
  "protocol": 1, "type": "action", "kind": "text", "text": "https://example.com/articles/1234",
  "action": "save",
  "fields": { "folder": "Reading list", "tags": "" },
  "state": { "articleId": 1234 }
}
```

It answers `notify` (the window closes) or `error` (the window stays open for a correction). Any other answer to either call is a protocol error and is reported as one.

A plugin must:

- read stdin to end of file, write one JSON object to stdout, and exit with code 0
- send diagnostics to stderr, which the host logs
- never start detached child processes, because Node gives a detached child its own console window, and pass `windowsHide: true` to any child it does start
- set `process.exitCode` rather than calling `process.exit()` straight after writing. On Windows, Node writes to pipes synchronously, but on macOS it doesn't, and an early exit can cut the answer short there

### Running a plugin

The research probe on this machine confirmed the following settings for a WinForms host driving Node:

- **Start**: `UseShellExecute = false`, `CreateNoWindow = true`, all three streams redirected, and `WorkingDirectory` set to the plugin folder. Every argument after the first goes through `ArgumentList`. A GUI host with `CreateNoWindow` gives Node a hidden console, and grandchildren inherit that console.
- **Encoding**: stdin uses `new UTF8Encoding(false)`. `Encoding.UTF8` writes a BOM, and Node's `JSON.parse` rejects it. Stdout and stderr use UTF-8. Cyrillic and emoji survive both directions.
- **No deadlock**: reads of stdout and stderr start before stdin is written. A plugin that wrote 2 MB to stderr before reading its input finished in 64–83 ms.
- **Deadline**: when `timeoutSeconds` runs out, the host ends the call's processes. Cancelling the wait doesn't stop the process, and a write to a plugin that never reads its input stays blocked until then. After the process exits, the host waits at most 2 seconds for stdout, stderr and the request write to finish, in case a process it started still holds a pipe, and then ends that process. Stdout alone decides the answer: once it has ended, the answer is complete even if a leftover still held stdin or stderr.
- **Output limit**: stdout is read up to 1 MB; a plugin writing more is ended and fails with that reason. Only the last 2,000 characters of stderr are kept, for the log.
- **Failure reason**: a failed call reports the first reason that applies, in this order: the start failed, the deadline passed, the exit code was not 0, stdout wasn't a valid answer. A plugin that exits without reading stdin makes the host's write fail with `IOException`, depending on timing. That failure goes to the log as detail and never becomes the reason, so a plugin that exits with code 3 is reported as exiting with code 3.
- **Resolving `run[0]`**: the host resolves the executable itself and passes an absolute path. A name with a path separator is relative to the plugin folder, and a bare name is searched on PATH. On Windows the host adds `.exe` to a name with no extension and refuses `.cmd` and `.bat`. `CreateProcess` would hand a batch file to `cmd.exe`, whose quoting `ArgumentList` can't make safe (BatBadBut). An executable that can't be found is reported as an error naming it. Node is on the persisted machine PATH here, so a host started from the Run key finds it.
- **Ending a call's processes**: on Windows each call also gets its own job object, nested in the host's, and ending the call terminates that job along with `Kill(entireProcessTree: true)`. The job catches a child whose parent has already exited, which a walk of the live process tree misses.
- **Host exit**: on Windows the host assigns every plugin process to one job object, created with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, whose handle it holds for its whole life. When the host ends for any reason, Windows kills every plugin process still running and the children they started inside the job. That covers "Exit", a crash, and the deploy script's `Stop-Process -Force`, which never runs the app's exit code. `OnExit` alone would miss the last two, leaving a hung call running forever and letting an action call finish its side effects unobserved. Whether Node's own job handling keeps its children inside this job is *(unverified)*, and Task 10 checks it.

Awaiting the process never blocks the UI thread: the message loop keeps pumping, and continuations return to the UI thread through the WinForms synchronization context. The clipboard and the windows are handled there.

### Handling a clipboard change

`ClipboardSession` in Core handles each clipboard change. The WinForms host implements a small interface for what only it can do:

- read and write the clipboard text
- report the clipboard's change count: `GetClipboardSequenceNumber` on Windows, and the pasteboard's change count on a future macOS host
- say whether the app is paused
- show notices, and open or bring forward a confirm window

Every rule below lives in Core, so the tests drive them with a fake host and a macOS host would reuse them. While a plugin call is awaited, the message loop runs, so a new `WM_CLIPBOARDUPDATE` can start a second run before the first has finished. The session handles that with these rules:

1. **Skip the app's own writes by change count.** Right after each write, the session records the change count, and it skips an event while the current count still equals the recorded one. That skips every event one write causes, however many increments it takes, and a later copy of the same text is processed. Today's `_lastCleanedResult` guard is never cleared. It drops every later copy of any text the app once wrote, so a re-copied cleaned URL is ignored and never moves to the front of the history. The rule assumes the count stops moving once `SetText` returns *(unverified)*.
2. **Remember at event time.** When the run first awaits a plugin, or ends if it never does, the history records the text as it stands, whether that is the copied text or a built-in's rewrite, under the exclusions above. A plugin rewrite applied later is remembered as well. So a template copied while a plugin is still working can already fill from the copy before it.
3. **Apply built-in rewrites before waiting on a plugin.** When a run is about to await its first plugin and the text has already changed, the session writes that text first. Otherwise a paste made while the plugin works gets the dirty text, and a later copy drops the built-in's rewrite with the plugin's. One copy can therefore cause two clipboard writes.
4. **Apply the final text only where it belongs.** When a run that awaited a plugin finishes with new text, the session rereads the clipboard. It writes the text, and remembers it, only if the clipboard still holds the text the run last applied, or the copied text in case the source app sent it again, and the app isn't paused. Otherwise it drops the write and logs it. The comparison is by text because a source app sending the same text again changes the count *(unverified: whether apps do this)*.
5. **Skip the plugins for duplicate copies.** An event whose copied text equals that of a run still in flight runs the built-ins only, and so does one whose text equals that of a run that finished less than 2 seconds ago with a plugin answering `notify` or `confirm`. So does the session's own last write put back by another app within 2 seconds, which would otherwise feed a plugin its own output. Some apps send several updates per copy, and without these checks a plugin that acts at once would act twice. The built-ins still run, so a repeated copy of a dirty URL is still cleaned and the history stays current. A deliberate re-copy after 2 seconds runs everything again.
6. **One confirm window per plugin and text.** Before calling a plugin, the session asks the host whether a window for that plugin and text is already open. If one is, the plugin isn't called: the host brings the window forward instead, with `Activate()` and `FlashWindowEx` as the fallback, logs it, and the entry counts as matched. A user who copies again usually does so because the window is hidden behind the browser.
7. **Check before opening a window.** At completion, a proposal opens only if the app isn't paused and the plugin is still installed and enabled. Otherwise it is dropped and logged. A `notify` result is shown either way, because the plugin has already acted.
8. **Nothing escapes a run.** The session wraps each run in a catch-all that logs the exception and shows a notice, and it removes the run from the in-flight set in `finally`. A failed clipboard read or write (`ExternalException` while another process holds the clipboard) drops that step and logs it. `Program.Main` registers `Application.ThreadException` and `AppDomain.CurrentDomain.UnhandledException` to write the log, so WinForms' unhandled-exception dialog never appears from a tray app.

A run that awaits nothing, which covers every copy no plugin pattern matches, completes synchronously and behaves as it does today.

### Notifications and the log

Notifications use `NotifyIcon.ShowBalloonTip` on the existing tray icon. It needs no package and no change to the target framework or the single-file publish. Windows 10 and 11 show these balloons as toasts unless the `EnableLegacyBalloonNotifications` policy is set, and it isn't set here. Keep titles under about 48 characters and text under about 200 (the hard limits are 64 and 256). A balloon doesn't stay in the Windows 11 notification center after it times out.

Windows 11 queues balloons, showing each only after the previous one has gone (checked 2026-09-24: a second balloon sent 0.7 s after the first appeared 3 to 5 seconds later), rather than replacing the one showing as the `ShowBalloonTip` documentation describes. So `Notices` in Core turns everything raised by one run, or by one reload of the config and the plugins, into one notice. A single notice shows as itself. Several show the first error, or the first notice when none is an error, followed by "and N more, see the log". Notices from separate runs still queue, and the log keeps each.

After the host plan was done, notices moved behind an `INotifier` interface in Core, with `BalloonNotifier` in the exe as the one implementation and `LoggedNotifier` logging every notice first. The user plans a second implementation that hands notices to the achievement-overlay app, once it accepts requests from other apps.

While Windows 11's Do Not Disturb is on, balloons don't show at all, and Windows 11 doesn't keep them in the notification center, so they are lost; the log still has everything. Checked 2026-09-24: `ToastNotificationManager.GetDefault().NotificationMode` read `PriorityOnly`, and a `notify` from a confirm window showed nothing. The user chose to leave balloons as they are rather than show results in the window or badge the tray icon.

- **Plugin messages**: a `notify` answer is shown with the plugin's name as the title. An `error` answer, a timeout, a non-zero exit, output that isn't a valid answer, or a failed start is shown with the title "`<name>` failed" and a one-line reason.
- **Configuration problems**: shown as soon as the change that found them arrives, not with that change's plugin results. A `config.json` that doesn't parse, or that holds a null list or a null inside one, is logged in full and shown once for each mtime. The last good config stays in use, and the file is read again at every event until it loads. A read that fails because the file is locked mid-write is retried at the next event without a notification. Invalid manifests and order problems are reported as described above.
- **Startup**: a `config.json` that doesn't parse no longer stops the app. Today `AppConfig.Load` throws before the message loop exists, so the Run key's instance crashes at logon with no message of its own, taking every plugin with it. The host starts from the embedded default in memory, leaves the user's file untouched, shows the problem once the tray icon exists, and keeps reading the file as above.
- **The log**: `Logger` in Core writes `url-cleaner.log` next to `config.json`. At startup it renames the previous log to `url-cleaner.old.log`, so the log that explains a crash survives one restart. Writes take a lock and never throw, because runner callbacks can finish off the UI thread. The log gets the full detail behind every notification, including the tail of the plugin's stderr, and every unhandled exception. "Open config location" in the tray already opens that folder.
- **One instance**: `Program.Main` takes a named mutex and exits when another instance holds it. With plugins that act, two instances would run each plugin twice on every matching copy: two confirm windows, two downloads, and two writers on one log. A second instance appears whenever the exe is started by hand while the Run key's instance is running.

### The confirm window

The `ConfirmForm` window renders a proposal:

- **Layout**: `FixedDialog` with `AutoScaleMode.Dpi`. An AutoSize `TableLayoutPanel` holds one label and one text box per field, or an editable `ComboBox` for a field with `options`. A right-to-left `FlowLayoutPanel` holds the buttons. The form is sized from the panel's realized `Height`, because `PreferredSize` is unreliable for wrapped labels.
- **Keyboard**: the first action is the `AcceptButton` and Cancel is the `CancelButton`. The first field has focus.
- **During the action call**: the buttons are disabled, and `FormClosing` refuses to close the window unless the app is exiting. A `notify` answer closes the window and shows the message. An `error` answer shows the message in the window and re-enables the buttons. Every error is also logged, and shown as a balloon when the window is already gone. Cancel, or closing the window, makes no call.
- **A plugin removed meanwhile**: the host reads the plugin's manifest again before the action call. If the plugin is gone, the window shows "Plugin no longer installed" and makes no call.
- **Failures**: the button handler has the same catch-all as a run.
- **Getting in front**: the window opens modeless with `TopMost`, `ShowInTaskbar = true` and `Activate()`. A tray app reacting to a copy isn't the foreground process, so Windows usually refuses to bring it to the front. If `GetForegroundWindow()` isn't the form, the host calls `FlashWindowEx`. How this looks in practice is *(unverified)*, and Task 8 checks it by hand.
- **DPI**: add `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` to the exe project. The app has had no windows until now, so it runs `SystemAware`, and a dialog on a scaled secondary monitor would be blurry.

### Plugin toggles

Installing a plugin turns it on, because a plugin missing from the `plugins` block is enabled (D8). The tray menu shows one checkbox per installed plugin after the converter toggles, labelled with the manifest's `name`:

- **Building the items**: they are rebuilt in `ContextMenuStrip.Opening`, which first runs the config and plugin reload, so the checkboxes match the files.
- **Writing the flag**: a click writes `plugins.<id>.enabled`. `UpdateConfigValue` gains a nested form that patches that one property inside the file's parsed `JsonObject`, creating `plugins` and the plugin's object only when they are missing. It never touches `pipeline`, so a toggle can neither erase a hand-edited order nor pin the fallback order into the file.
- **A failed write** reverts the checkbox, as the converter toggles already do.
- **Stale entries**: a `plugins` entry for a plugin that isn't installed is ignored and kept, so a reinstalled plugin comes back in the state it was left in.

### Configuration

Existing keys don't change (D8). Two new optional blocks join them: `plugins` holds each plugin's enabled flag, and `pipeline` holds the order (D6). Both are left out of `config/default.json`, so new installs and existing ones use the fallback order with every plugin enabled. A `config/local.json` with one plugin installed might look like this:

```jsonc
{
  "trimUrl": true,
  "convertPaths": true,
  "convertNumbers": true,
  "convertPlaceholders": true,
  "trackingParams": [ /* unchanged */ ],
  "siteRules": [ /* unchanged */ ],
  "plugins": {
    "example-notes": { "enabled": true }
  },
  "pipeline": {
    "order": [
      { "id": "urlCleaner", "stop": false },
      { "id": "example-notes", "stop": true },
      { "id": "convertPaths" },
      { "id": "convertNumbers" },
      { "id": "convertPlaceholders" }
    ]
  }
}
```

Here the URL cleaner runs first without stopping, so the plugin sees a URL with its tracking parameters already removed. In the fallback order the URL cleaner has `stop` set, so a URL with tracking parameters stops at the cleaner. Copying the cleaned URL again then reaches the plugin, because the echo guard skips only the app's own write.

### Project split for a future macOS host

Everything platform-neutral moves into `src/Core/UrlCleaner.Core.csproj`, which targets `net10.0` (D2): the config model and loader, the four converters, the modules, the pipeline, the history, the clipboard session, the notices, the manifest, the protocol, the runner and the logger. The WinForms project keeps the clipboard listener, the tray, the balloons, the confirm window and autostart.

- **The probe build**: a scratch `net10.0` library with the converters and `AppConfig` built cleanly. It warned only about CA1416 on `Registry`, which is why autostart moves to `src/Autostart.cs`.
- **The embedded default config** moves into the Core project with `LogicalName="UrlCleaner.default.json"`, and `typeof(AppConfig).Assembly` finds it at runtime.
- **The library sits in `src/Core/`**, so the deploy script's `ls src/*.csproj` and CI's `dotnet publish src/` keep working unchanged. The exe project needs `<DefaultItemExcludes>$(DefaultItemExcludes);Core/**</DefaultItemExcludes>`. Without it, the exe project compiles `Core/obj` as well and fails with CS0579. Publish output stays flat and gains `UrlCleaner.Core.dll`.
- **The namespace** stays `UrlCleaner`. The test project references Core and drops to `net10.0` without WinForms.
- **The job object** is Windows-only code in the runner, called behind `OperatingSystem.IsWindows()`. A macOS host needs its own way to end plugin processes with the host.
- **CI**: the framework-dependent release zip lists its files explicitly, so `UrlCleaner.Core.dll` has to be added to the list. Whether the single-file build bundles it is *(unverified)*, and Task 2 checks it.
- **A macOS host later** would need a configurable interpreter path. Apps started from Finder or at login get launchd's PATH, which doesn't include Homebrew. The Windows host needs nothing like it now.

### Tests

- **Unit tests (Core)** cover these areas:
  - the fallback order reproducing today's first-match chain
  - stop flags, an omitted `stop` meaning set, and later entries seeing rewritten text
  - disabled, unknown and duplicate ids
  - the history exclusions
  - the config loader falling back to the embedded default without touching an invalid file
  - the log's rename at startup
  - manifest validation: protocol range, id, patterns, `timeoutSeconds` range, `run` resolution and the default timeout
  - matching trimmed text, including a trailing CRLF
  - parsing every protocol answer, including each invalid one
  - summarising several notices into one balloon
- **Session tests**: `ClipboardSession` with a fake host, covering each rule under "Handling a clipboard change".
- **Fixture plugins**: small Node scripts under `tests/fixtures/plugins/`, run through the real runner, pipeline and session. They cover each answer type and each invalid answer, output that isn't JSON, a non-zero exit before reading stdin, a timeout (checking the child process is gone afterwards), Cyrillic in both directions, a 2 MB stderr flood, and a refused `.cmd`. These tests need Node. The `windows-latest` CI image (Windows Server 2025) has Node 22 on PATH, checked 2026-09-24 against the runner-images readme.
- **Manual checks**: the clipboard, the balloons, the confirm window and its foreground behaviour, and the absence of console windows. Tasks 8 and 10 cover them.

### Documentation

The configuration page gains the `pipeline` block, which orders the built-ins even with no plugin installed. The architecture page describes the pipeline, the clipboard rules, plugins running as separate processes, and the `plugins` block with its tray checkboxes. Plugins stay out of the configuration page and the features lists in `README.md` and `docs/index.md`, because the protocol is unpublished and a public reader has no plugin to install (D5). The protocol itself stays in this plan and in code comments.

### Open items

#### Unverified points

- **Foreground**: checked by the user 2026-09-24 with a copy from the browser. The first window came to the front and took the keyboard. Copying the same text again with the window behind the browser flashed its taskbar button and left the keyboard in the browser. Clicking a balloon doesn't give the app foreground rights (checked 2026-09-24 with Do Not Disturb off): a re-copy afterwards still only flashed the window. Being `TopMost`, the window stays above the browser even when the browser is clicked, so it never goes behind it.
- **Duplicate updates**: how far apart an app's several clipboard updates for one copy can arrive in general. Observed 2026-09-24: a WinForms `Clipboard.SetText` from PowerShell raised two updates with the same text about 50 ms apart, and the in-flight check skipped the second. The 2-second window covers later ones.
- **Change count**: whether the clipboard's change count has stopped moving by the time `SetText` returns, which the echo guard relies on. Observed holding 2026-09-24: a fixture's rewrite that still matched its own pattern was not processed again, though the app's own write raised two updates.
- **Balloons as toasts**: checked 2026-09-24 with Do Not Disturb off: Windows 11 queues a second balloon until the first has gone, instead of replacing it.
- **Job object**: checked 2026-09-24: a deploy's forced stop ended a running hang fixture and its child. For a Node plugin's child that doesn't prove the host's job ended it, since libuv ends a Node process's children when it dies.

## Implementation Steps

### Task 1: Get the host deploy fix into the dotfiles repo

This has to be done before any plugin is installed next to a deployed host.

- [x] Send the dotfiles repo's live session the defect and a fix: in the global deploy script, replace `rm -f "$INSTALL_DIR"/*` with a delete of files only, for example `find "$INSTALL_DIR" -maxdepth 1 -type f -delete`. Under `set -e`, `rm` exits 1 on the `plugins/` directory and aborts the deploy after the app's files are gone. Don't edit or commit that repo from here
- [x] Once it is fixed, run a host deploy with an empty `plugins/` folder in the install folder, and confirm the deploy completes and the folder survives

### Task 2: Split the platform-neutral code into UrlCleaner.Core

**Files:**
- Create: `src/Core/UrlCleaner.Core.csproj`, `src/Autostart.cs`
- Move: `src/AppConfig.cs`, `src/UrlSanitizer.cs`, `src/PathConverter.cs`, `src/NumberConverter.cs`, `src/PlaceholderConverter.cs` to `src/Core/`
- Modify: `src/UrlCleaner.csproj`, `src/TrayApplicationContext.cs`, `url-cleaner.slnx`, `tests/UrlCleaner.Tests.csproj`, `.github/workflows/build.yml`

- [x] Create the Core project (`net10.0`, `Nullable` and `ImplicitUsings` enabled) with the `EmbeddedResource` for `../../config/default.json`, keeping `LogicalName="UrlCleaner.default.json"`
- [x] Move the five files with `git mv`, keeping the `UrlCleaner` namespace
- [x] Move `GetAutoStart` and `SetAutoStart` from `AppConfig` into a static `Autostart` class in the exe project, and update the tray's callers
- [x] In the exe project, remove the `EmbeddedResource`, add a `ProjectReference` to Core, and add `<DefaultItemExcludes>$(DefaultItemExcludes);Core/**</DefaultItemExcludes>`
- [x] Add the Core project to `url-cleaner.slnx`
- [x] Point the test project at Core only, switch it to `net10.0`, and remove `UseWindowsForms`
- [x] Add `publish/framework-dependent/UrlCleaner.Core.dll` to the framework-dependent zip in `build.yml`
- [x] Run both `dotnet publish` commands from `build.yml` locally. Confirm the framework-dependent output contains `UrlCleaner.Core.dll`, and that the single-file exe starts and writes a default `config.json` from a folder of its own. Delete the publish output afterwards
- [x] Run the gate

### Task 3: Built-in modules, the pipeline and the clipboard session

**Files:**
- Create: `src/Core/BuiltInModules.cs`, `src/Core/Pipeline.cs`, `src/Core/ClipboardHistory.cs`, `src/Core/ClipboardSession.cs`, `tests/PipelineTests.cs`, `tests/ClipboardSessionTests.cs`
- Modify: `src/Core/AppConfig.cs`, `src/ClipboardMonitor.cs`

- [x] Add `ModuleContext`, `BuiltInModule` and `BuiltInModules.All` as in Design Notes
- [x] Add an optional `Pipeline` property to `AppConfig`, holding a nullable `Order` list of `{ Id, Stop }` with `Stop` defaulting to `true`
- [x] Implement order resolution: the fallback, unlisted entries, and unknown and duplicate ids reported as (id, problem) pairs
- [x] Implement the run for built-ins: enabled check, stop flags, rewritten text passed on, and a result carrying the final text, whether it changed, and whether `convertPlaceholders` matched
- [x] Move the history buffer and its remember rule from `ClipboardMonitor` into `ClipboardHistory`, and pass the converter a snapshot taken at run start
- [x] Add `ClipboardSession` and its host interface with the synchronous rules: the change-count echo guard in place of `_lastCleanedResult`, and remembering the result under the exclusions
- [x] Make `ClipboardMonitor` implement the host interface with `Clipboard` and `GetClipboardSequenceNumber`, and hand each `WM_CLIPBOARDUPDATE` to the session in place of the hard-coded chain
- [x] Write tests:
  - with no `pipeline` block, the pipeline gives the original chain's result (kept in the test as an oracle) for URL, path, number, placeholder and plain inputs, with the converters off and on
  - with `stop: false`, the next entry sees the rewritten text, and an entry that omits `stop` stops the run
  - a disabled built-in is skipped
  - an unknown or duplicate id is reported
  - a placeholder-filled result and a placeholder template are not remembered
  - the session skips the event caused by its own write, and processes a later copy of the same text, which moves it to the front of the history
- [x] Run the gate

### Task 4: The log, loud errors and a single instance

**Files:**
- Create: `src/Core/Logger.cs`, `src/Core/Notices.cs`, `src/Core/ConfigFile.cs` (loading, reload and reporting of `config.json`, shared by startup and reload), `src/Notifier.cs`, `tests/LoggerTests.cs`, `tests/ConfigFileTests.cs`, `tests/NoticesTests.cs`
- Modify: `src/Program.cs`, `src/Core/AppConfig.cs`, `src/Core/ClipboardSession.cs`, `src/ClipboardMonitor.cs`, `src/TrayApplicationContext.cs`

- [x] Add a static `Logger` with `Info`, `Warn` and `Error` that writes `url-cleaner.log` next to `config.json`, renames the previous log to `url-cleaner.old.log` at startup, locks around writes, and never throws
- [x] Add `Notices`, which turns one run's or one reload's notices into a single balloon's title and text, and a `Notifier` that shows it with `ShowBalloonTip`, shortening titles to 48 characters and text to 200
- [x] Make `AppConfig` report a config that doesn't parse instead of throwing. At startup, fall back to the embedded default in memory without writing the file, and show the problem once the tray icon exists
- [x] Change the reload so that a config that fails to parse is logged in full and shown once for that mtime. Keep the last good config, read the file again at every event until it loads, and retry a locked file silently
- [x] In `Program.Main`, take a named mutex and exit when another instance holds it, and register `Application.ThreadException` and `AppDomain.CurrentDomain.UnhandledException` to write the log
- [x] Write tests: a second logger start moves the first log's lines to `url-cleaner.old.log` and starts an empty log; an invalid `config.json` loads the embedded default, reports the error and leaves the file byte for byte as it was; several notices become one balloon that leads with the first error
- [x] Run the gate

### Task 5: Plugin discovery and manifests

**Files:**
- Create: `src/Core/PluginManifest.cs`, `src/Core/PluginFolder.cs` (discovery), `tests/PluginManifestTests.cs`, `tests/PluginFolderTests.cs`
- Modify: `src/Core/ClipboardSession.cs`

- [x] Add the manifest model and validation: `protocol` from 1 to the host's version, refusing a higher one with both versions named; a kebab-case `id` equal to the folder name; a non-empty `name`; patterns that compile; a non-empty `run`; and `timeoutSeconds` a whole number from 1 to 300, defaulting to 10
- [x] Add pattern matching against the trimmed text, with `CultureInvariant` and a 100 ms match timeout. A timeout counts as no match and is logged
- [x] Add `run[0]` resolution: relative to the plugin folder when it contains a path separator, otherwise on PATH. On Windows, add `.exe` to a name with no extension, and refuse `.cmd` and `.bat`
- [x] Discover `plugins/*/plugin.json` under the exe folder, and refresh on each clipboard event when the manifest list or an mtime changed. An invalid manifest is logged and shown once for each mtime
- [x] Resolve the order again whenever the config's mtime or the plugin set changes, and report each (id, problem) pair once until it disappears
- [x] Write tests for each validation rule (including protocol 2, and `timeoutSeconds` of 0, 301 and -1), the pattern timeout, matching text with a trailing CRLF and with surrounding spaces, `run` resolution including a refused `.cmd`, and a removed plugin named in the order being reported once
- [x] Run the gate

### Task 6: The plugin runner and protocol v1

**Files:**
- Create: `src/Core/PluginProtocol.cs`, `src/Core/PluginRunner.cs`, `tests/PluginRunnerTests.cs`, `tests/fixtures/plugins/`

- [x] Check first whether the `windows-latest` runner has `node` on PATH. If it doesn't, add `actions/setup-node` with Node 24 to `build.yml` before any test needs it. It does: Node 22 on Windows Server 2025
- [x] Add records for the copy and action requests and for the five answer types, serialized in camelCase. Anything that isn't exactly one JSON object of a known type, or that breaks a rule in "Protocol v1" (an empty rewrite text or message, a proposal with no actions or an empty title, empty or duplicate field and action ids), becomes a protocol error
- [x] Implement the runner as in "Running a plugin": start settings, a BOM-free stdin, reads started before the write, stdin closed after writing, the deadline enforced with `Kill(entireProcessTree: true)`, a 2-second limit on reads after exit, and stderr sent to the log
- [x] On Windows, assign each plugin process to the host's job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`
- [x] Turn every failure into an error result with a one-line reason, chosen in the order start, timeout, exit code, output. Log a failed stdin write as detail only
- [x] Write fixture plugins: one Node script, `tests/fixtures/plugins/fixture.js`, whose first argument picks the behaviour, run through manifests the tests generate, plus installable `fixture-rewrite`, `fixture-notify`, `fixture-confirm`, `fixture-error` and `fixture-hang` folders for the manual checks. The modes cover each answer type, each invalid answer listed above, output that isn't JSON, exit code 3 without reading stdin, a hang, a Cyrillic and emoji echo, and 2 MB to stderr before reading stdin
- [x] Write tests running each fixture through the runner. The exit-3 test asserts the reason names the exit code, and the hang test checks that the process and its child are gone afterwards. For a Node plugin the child's end doesn't prove the tree kill, since libuv ends a Node process's children when it dies
- [x] Run the gate

### Task 7: Plugins in the pipeline

**Files:**
- Modify: `src/Core/AppConfig.cs`, `src/Core/Pipeline.cs`, `src/Core/ClipboardSession.cs`, `src/ClipboardMonitor.cs`, `src/TrayApplicationContext.cs`, `tests/PipelineTests.cs`, `tests/ClipboardSessionTests.cs`, `tests/AppConfigTests.cs`

- [x] Add the optional `plugins` block to `AppConfig`, a map from plugin id to `{ Enabled }` with `Enabled` defaulting to `true`, and skip disabled plugins in the run
- [x] Add the nested `UpdateConfigValue` and the tray checkboxes as in "Plugin toggles"
- [x] Make the run asynchronous. Built-ins stay synchronous, and a run that reaches no matching plugin completes without awaiting
- [x] Add plugin entries: pattern check, the open-window check before the call, the copy call, and the answer mapped to "matched" as in Design Notes, with notices and proposals collected in the result
- [x] Add the asynchronous rules to the session: the built-in text applied and remembered before the first await, the final text applied only where it belongs, duplicate copies skipped while in flight and for 2 seconds after a `notify` or `confirm`, proposals checked for pause and installation before opening, and a catch-all with the in-flight entry removed in `finally`
- [x] Show each run's notices through `Notifier`
- [x] Write tests with fixture plugins and a fake host:
  - a plugin rewrite followed by a built-in
  - `stop` after a `notify`
  - `none` and `error` letting later entries run
  - a plugin whose patterns don't match is never started. Use a fixture that writes a marker file when it runs
  - with the URL cleaner at `stop: false` before a slow fixture, the cleaned URL is on the clipboard before the fixture answers
  - a template copied while a slow run is in flight fills from the text that run copied
  - a second event for the same text, 200 ms after a fast `notify` fixture finished, doesn't start it again
  - a plugin rewrite is dropped when the clipboard changed meanwhile
  - a run that throws leaves nothing in flight, so copying the same text again runs
  - a proposal is dropped when the app was paused during the call, and a `notify` is still shown
  - a plugin set to `enabled: false` is never started, and one missing from the `plugins` block is
  - the nested `UpdateConfigValue` writes `plugins.<id>.enabled` into a file with no `plugins` block, leaves every other value unchanged, and adds no `pipeline` block
- [x] Run the gate

### Task 8: The confirm window

**Files:**
- Create: `src/ConfirmForm.cs`
- Modify: `src/UrlCleaner.csproj`, `src/TrayApplicationContext.cs`

- [x] Add `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>` to the exe project
- [x] Build `ConfirmForm` from a proposal as in "The confirm window": fields, editable drop-downs for fields with options, the plugin's actions, and the host's Cancel button
- [x] Open proposals modeless with `TopMost`, `ShowInTaskbar` and `Activate()`, and call `FlashWindowEx` when the form didn't reach the foreground
- [x] Keep one open window per plugin and text, and bring it forward when the session asks
- [x] Send the action call with the field values and `state`: read the manifest again first, disable the buttons and refuse to close while the call runs, close on `notify`, and re-enable on `error`. Log every error, and show it as a balloon when the window is gone
- [x] Wrap the button handler in the same catch-all as a run
- [x] Deploy with a confirm fixture plugin installed, copy matching text from a browser, and record how the window appears. Note the result in this task: in front, behind with a flashing taskbar button, or other. Result: in front, with the keyboard
- [x] With the window behind the browser, copy the same text again. The window comes forward or flashes, and the log shows the plugin wasn't called again. Result: it flashed, and the log shows the window brought forward instead of a call
- [x] Click a plugin's balloon, then copy matching text within a few seconds, and record whether the confirm window now comes to the front. Result, with Do Not Disturb off: it only flashed, and the keyboard stayed in the browser
- [x] With a fixture whose action call is slow, try to close the window during the call. It stays open until the answer arrives. Result: X did nothing during the call, and the window closed when the answer came
- [x] Run the gate

### Task 9: Documentation

**Files:**
- Modify: `docs/pages/configuration.md`, `docs/pages/development.md`, `docs/pages/development/architecture.md`, `.claude/memory/MEMORY.md`

- [x] Invoke the `docs-style` skill before writing
- [x] Add a section to `configuration.md`, headed "Feature order" since that is what a user looks for: the `pipeline` block, the fallback order, and the stop flags, including that an omitted `stop` means set, with a JSON example that orders built-ins only. Leave the features lists in `README.md` and `docs/index.md` unchanged (D5)
- [x] Update `architecture.md`: the components table, the flow (pipeline, modules, plugins as separate processes), the clipboard rules, and the `plugins` block with its tray checkboxes. Leave the protocol out (D5)
- [x] Update the project structure in `development.md` for `src/Core/` and `url-cleaner.slnx`, and add Node to the prerequisites for running the tests
- [x] Update the repo layout and architecture notes in the project memory
- [x] Run the gate

### Task 10: Verify acceptance criteria

- [x] Deploy with no `pipeline` block. A URL with `utm_source`, a Windows path, `10,871.69` and a placeholder template all behave as before
- [x] Copy a URL with `utm_source`, then copy the cleaned URL from another app. The second copy is processed: a placeholder template copied next fills with it
- [x] Copy the rewrite, notify, confirm, error and hang fixture plugins into `<install>/plugins/`. Without a restart, each behaves as specified at the next matching copy, and the hang ends in a timeout notification
- [x] Make one run raise two notices (a `notify` fixture followed by an `error` fixture with `stop: false`). One balloon appears and mentions the second. Checked through the log's balloon line, since Do Not Disturb hid the balloon
- [x] Trigger two separate runs' balloons within a second of each other, and record whether the second replaces the first. Result, with Do Not Disturb off: the second appeared only after the first had gone, 3 to 5 seconds later
- [x] Throughout, no console window appears on the desktop. The user saw none, and the hang fixture's processes had no window handle while running
- [x] Add a `pipeline` block to `config/local.json` and deploy. The order and stop flags take effect. Done by editing the installed `config.json` instead, which hot reload picks up the same way, so the personal `local.json` stayed untouched; the configuration page's example gave `https://example.com/?q=shoes`
- [x] Untick a fixture plugin in the tray. The next matching copy doesn't start it, and `config.json` gains only `plugins.<id>.enabled`. Tick it again and it runs
- [x] Remove a fixture plugin that the order names. One notification appears without `config.json` changing
- [x] Break `config.json` by hand. One notification appears, the app keeps working, and fixing the file recovers it. Break it again and restart the app: it starts, notifies, and recovers when the file is fixed
- [x] Start the exe a second time by hand. It exits, and only one tray icon remains
- [x] Deploy while the hang fixture is running. No `node.exe` from the fixture is left afterwards
- [x] Before moving this plan, check it holds nothing private: no absolute paths, no library contents, no credentials, and no internals of another project. `gh repo view --json isPrivate` shows the repo is public, and `git check-ignore -v docs/plans/completed/2026-09-24-clipboard-plugins.md` must print nothing, so the move publishes the file. It printed the `docs/plans/` rule: git can't re-include a file under an excluded directory, so that rule had made the `completed/` exception dead, and it is now `docs/plans/*`. The plan names What's Next and its download plugin, and nothing else of that project
- [x] Move this plan to `docs/plans/completed/`
