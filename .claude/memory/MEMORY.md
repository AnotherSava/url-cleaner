# URL Cleaner — C# Rewrite

## Decision
- User chose **C# WinForms** rewrite (over improving Python or using Java)
- Learning C# alongside building the tool
- IDE: **JetBrains Rider**

## Project Setup (completed)
- .NET 10.0 SDK installed (v10.0.103)
- License: **GPL v3**
- `.gitignore` added (dotnet template)
- Solution file: `url-cleaner.sln` at repo root

## Repo Layout
```
/
  url-cleaner.sln
  LICENSE
  config/
    default.json          ← default config (embedded resource, grouped tracking params)
  src/
    UrlCleaner.csproj     ← targets net10.0-windows
    Program.cs            ← entry point, runs TrayApplicationContext
    TrayApplicationContext.cs ← system tray app (NotifyIcon, context menu)
    AppConfig.cs          ← config model + JSON loader + autostart registry helpers
    ClipboardMonitor.cs   ← Win32 clipboard listener (NativeWindow + P/Invoke)
    UrlSanitizer.cs       ← URL cleaning logic (strip tracking params)
```

## Completed Features
- System tray app (ApplicationContext pattern, NotifyIcon, `SystemIcons.Shield` placeholder)
- Clipboard monitoring via `AddClipboardFormatListener` / `WM_CLIPBOARDUPDATE`
- URL cleaning: strips tracking params, supports per-domain site rules
- Config: `config.json` auto-generated on first run from embedded `default.json`
- `trackingParams` in JSON uses grouped format: `{ comment, params[] }` for readability
- `suffix` field in site rules accepts string or array (custom `StringOrListConverter`)
- "Start with Windows" checkbox in tray menu (registry-only, no config field)
- All config model properties use `init` accessors (immutable after deserialization)
- `convertPlaceholders`: fills `{{kebab-case}}` placeholders from an in-app clipboard history buffer (`ClipboardMonitor._history`, last 10 distinct values, most-recent first); FIFO mapping — first-copied value → first-appearing placeholder, so a single placeholder takes the most recent copy

## Architecture Notes
- `default.json` is an **embedded resource** (`LogicalName="UrlCleaner.default.json"`)
- Autostart is registry-only (`HKCU\...\Run`) — not stored in config.json
- URL query parsing is manual (not `HttpUtility`) to preserve original encoding
- Infinite-loop prevention: `_isUpdatingClipboard` flag in `ClipboardMonitor`

## Deployment
- `deploy` runs `scripts/deploy.sh` → global deploy skill; installs to `INSTALL_DIR` from `config/deploy.env` (gitignored, machine-specific)
- **Config layering**: `config/default.json` = prod default (embedded, shipped to all users on first run); `config/local.json` (gitignored) = personal override that deploy **copies wholesale over** the installed `config.json` — so it must be a COMPLETE config and will drift from `default.json` as tracking params/site rules change
- Convert features ship OFF in prod, ON locally: `convertPaths` + `convertNumbers` + `convertPlaceholders` are `false` in `default.json`, `true` in `local.json`

## CI Notes
- Each push runs **two** workflows: the tracked `.github/workflows/build.yml`, and GitHub's built-in `dynamic/pages/pages-build-deployment` (auto-triggered because Pages serves `docs/`)
- The Pages workflow is GitHub-managed — its internal `actions/checkout@v4` / `upload-artifact@v4` emit a Node.js 20 deprecation warning that **cannot** be fixed from the repo. A lingering Node 20 warning after bumping `build.yml`'s own action versions is expected, not actionable

## Environment Notes
- `dotnet` CLI path: `"/c/Program Files/dotnet/dotnet.exe"` (not on bash PATH, use full path)
- Build: `"/c/Program Files/dotnet/dotnet.exe" build src` (run from the repo root)

## Original Context
- Forked from [Confiqure/TracklessURL](https://github.com/Confiqure/TracklessURL) (Python, proof-of-concept quality)
- Goal: Windows 11 background clipboard URL cleaner (system tray, event-driven)

## User Preferences
- Learning C# — explain concepts as we go
- Prefers clean repo root (user-facing files only, source in `src/`)
- Prefers config as external files over hardcoded defaults
- Prefers simplicity — no over-engineering, flat structure until ~10+ files
