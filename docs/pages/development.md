---
layout: default
title: Development
nav_order: 3
has_children: true
---

# Development

## Prerequisites

- Windows 10+
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) on PATH, for the tests: the plugin tests run small Node plugins, and fail rather than skip without it

## Build

```
dotnet build src/
```

The built executable will be in `src/bin/Debug/net10.0-windows/`, next to `UrlCleaner.Core.dll`.

## Test

```
dotnet test tests/
```

The commit gate, `bash .claude/commit-checks.sh`, runs the same restore, build and test as CI. Before them it runs the checks CI can't, since they read `~/.claude`: the repo's conventions checker, and `docs/screenshots/check-skill-scripts.ps1`, which checks that the screenshot capture scripts can still reach what they use from the docs-relevance skill. Its header lists what it checks and what it leaves unchecked.

## Architecture

See the [Architecture reference](development/architecture) for how clipboard monitoring, the pipeline, plugins and URL cleaning work.

## Project structure

```
/
  url-cleaner.slnx
  config/
    default.json                default config (embedded resource)
  src/
    UrlCleaner.csproj           the app, targets net10.0-windows
    Program.cs                  entry point, single-instance check, logging
    TrayApplicationContext.cs   system tray app (NotifyIcon, context menu)
    ClipboardMonitor.cs         Win32 clipboard listener, the session's Windows host
    ConfirmForm.cs              confirm window for a plugin's proposal
    ConfirmWindows.cs           the open confirm windows
    BalloonNotifier.cs          shows notices as tray balloons
    Autostart.cs                "Start with Windows" registry helpers
    Core/
      UrlCleaner.Core.csproj    everything that doesn't need Windows, targets net10.0
      AppConfig.cs              config model and JSON loader
      ConfigFile.cs             config reload and error reporting
      ClipboardSession.cs       handles each clipboard change
      ClipboardHistory.cs       recent values for placeholder filling
      Pipeline.cs               order resolution and the run
      BuiltInModules.cs         the built-in features as pipeline entries
      UrlSanitizer.cs           URL cleaning logic
      PathConverter.cs          backslash-to-forward-slash path conversion
      NumberConverter.cs        strips thousands separators from copied numbers
      PlaceholderConverter.cs   fills placeholders from recent clipboard values
      PluginFolder.cs           finds installed plugins
      PluginManifest.cs         plugin.json validation and pattern matching
      PluginProtocol.cs         requests and answers
      PluginRunner.cs           runs one plugin call as a process
      Notices.cs                notices, the notifier interface, logging every notice
      Logger.cs                 url-cleaner.log
  tests/
    *Tests.cs                   one file per component
    fixtures/plugins/           fixture.js, a test plugin driven by its first argument
```

## Configuration defaults

The default config, `config/default.json`, is embedded into `UrlCleaner.Core.dll` as a .NET resource (`LogicalName="UrlCleaner.default.json"`). On first run, the app generates `config.json` next to the executable from this embedded default. To update the defaults for new builds, edit `config/default.json`.
