---
layout: default
title: Development
---

[Home](..) | [Configuration](configuration) | [Architecture](architecture) | [Development](development)

---

## Prerequisites

- Windows 10+
- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Build

```
dotnet build src/
```

The built executable will be in `src/bin/Debug/net10.0-windows/`.

## Test

```
dotnet test tests/
```

## Project structure

```
/
  url-cleaner.sln
  config/
    default.json              default config (embedded resource)
  src/
    UrlCleaner.csproj         targets net10.0-windows
    Program.cs                entry point
    TrayApplicationContext.cs  system tray app (NotifyIcon, context menu)
    AppConfig.cs              config model, JSON loader, autostart helpers
    ClipboardMonitor.cs       Win32 clipboard listener (NativeWindow)
    UrlSanitizer.cs           URL cleaning logic
    PathConverter.cs          backslash-to-forward-slash path conversion
  tests/
    UrlSanitizerTests.cs      URL cleaning tests
    PathConverterTests.cs     path conversion tests
```

## Configuration defaults

`config/default.json` is embedded into the DLL as a .NET resource (`LogicalName="UrlCleaner.default.json"`). On first run, the app generates `config.json` next to the executable from this embedded default. To update the defaults for new builds, edit `config/default.json`.
