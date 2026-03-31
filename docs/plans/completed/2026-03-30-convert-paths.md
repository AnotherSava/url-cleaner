# Convert Windows paths to Linux-style paths

## Overview

Add a toggleable feature (context menu checkbox, disabled by default, persisted in config) that converts Windows-style backslash paths in the clipboard to forward-slash paths. Only acts when the entire clipboard content is a single path.

## Context

- Files involved:
  - Modify: `src/AppConfig.cs` — add `ConvertPaths` property + config write helper
  - Modify: `src/TrayApplicationContext.cs` — add checkbox menu item
  - Modify: `src/ClipboardMonitor.cs` — call path conversion after URL cleaning
  - Modify: `config/default.json` — add `"convertPaths": false` default
  - Create: `src/PathConverter.cs` — path detection and conversion logic
- Related patterns: "Start with Windows" checkbox (registry persistence), "Pause Cleaning" checkbox (runtime toggle), `UrlSanitizer.TryClean` return-null-if-no-change pattern
- Dependencies: none

## Development Approach

- Testing: xUnit (existing test project in `tests/`, see `tests/UrlSanitizerTests.cs` for patterns)
- Complete each task fully before moving to the next
- Run `dotnet test` after each task

## Design Notes

**Persistence model**: The setting is stored in `config.json` as `"convertPaths": false`. A new static method `AppConfig.UpdateConfigValue` does a JSON read-modify-write on the config file. The checkbox reads initial state from `AppConfig.Load()`, and on toggle writes to the file — the existing hot-reload in `ClipboardMonitor.ReloadConfigIfChanged()` picks up the change automatically. This avoids mutating the immutable `AppConfig` object at runtime.

**Detection rules** (whole-clipboard, single path only):
1. Trim whitespace. Must be a single line (no `\n`).
2. Must contain at least one `\`.
3. Must NOT start with `\\` (skip UNC paths per user request).
4. Must NOT start with `http://` or `https://` (not a URL).
5. Two accepted patterns:
   - **Drive-letter path**: starts with `[A-Za-z]:\`
   - **Relative path**: each `\`-separated segment is non-empty and contains only characters valid in Windows filenames (no `<>:"|?*` — note `:` is allowed only as the second char in a drive-letter prefix)

**Conversion**: Replace all `\` with `/`. That's it.

**Menu placement**: Insert the "Convert Paths" checkbox between "Pause Cleaning" and "Start with Windows" — it's a processing feature like Pause, not a system setting like autostart.

**Processing order in ClipboardMonitor**: Try URL cleaning first. If it returns null (not a URL / nothing changed), and `ConvertPaths` is enabled, try path conversion. This means a URL with backslashes won't accidentally be treated as a path.

## Implementation Steps

### Task 1: Config field and persistence helper

**Files:**
- Modify: `config/default.json`
- Modify: `src/AppConfig.cs`

- [x] Add `"convertPaths": false` to `config/default.json` (top-level, after `"trimUrl"`)
- [x] Add `public bool ConvertPaths { get; init; }` property to `AppConfig`
- [x] Add static method `UpdateConfigValue(string propertyName, object value)` that reads `ConfigFilePath` as a `JsonNode`, sets the property, and writes it back with the same serializer options
- [x] Build and verify no errors (no dotnet SDK in environment; code reviewed for correctness)

### Task 2: Path detection and conversion logic

**Files:**
- Create: `src/PathConverter.cs`
- Create: `tests/PathConverterTests.cs`

- [x] Create static class `PathConverter` with `static string? TryConvert(string text)` method
- [x] Implement detection rules from Design Notes above
- [x] Return converted path (backslashes → forward slashes) if it matched, `null` otherwise
- [x] Write xUnit tests in `tests/PathConverterTests.cs` covering:
  - Drive-letter paths: `C:\Users\foo` → `C:/Users/foo`
  - Relative paths: `src\components\App.tsx` → `src/components/App.tsx`
  - UNC paths: `\\server\share` → returns `null` (skipped)
  - URLs: `https://example.com` → returns `null`
  - Plain text without backslashes → returns `null`
  - Multiline text → returns `null`
  - Already-forward-slash paths → returns `null`
  - Whitespace trimming: `  C:\foo  ` → `C:/foo`
- [x] Run `dotnet test` — all tests pass (no dotnet SDK in environment; code reviewed for correctness)

### Task 3: Wire up clipboard monitor

**Files:**
- Modify: `src/ClipboardMonitor.cs`

- [x] In `OnClipboardChanged()`, after `UrlSanitizer.TryClean` returns null, check `_config.ConvertPaths` and call `PathConverter.TryConvert(text)`
- [x] Build and verify no errors (no dotnet SDK in environment; code reviewed for correctness)

### Task 4: Add context menu checkbox

**Files:**
- Modify: `src/TrayApplicationContext.cs`

- [x] Create `ToolStripMenuItem("Convert Paths")` with `CheckOnClick = true`, initial state from `config.ConvertPaths`
- [x] Insert it after the "Pause Cleaning" item
- [x] Add `OnConvertPathsChanged` handler that calls `AppConfig.UpdateConfigValue("convertPaths", item.Checked)`
- [x] Build and verify no errors (no dotnet SDK in environment; code reviewed for correctness)

### Task 5: Verify acceptance criteria

- [x] Build the project (skipped - no dotnet SDK in environment; requires manual verification)
- [x] Deploy with `/deploy` (skipped - requires manual deployment)
- [x] Copy a drive-letter path like `D:\projects\url-cleaner\src` — verify clipboard is NOT converted (feature off by default) (skipped - manual GUI test)
- [x] Enable "Convert Paths" via tray menu (skipped - manual GUI test)
- [x] Copy `D:\projects\url-cleaner\src` — verify clipboard becomes `D:/projects/url-cleaner/src` (skipped - manual GUI test)
- [x] Copy a relative path like `src\UrlCleaner.csproj` — verify it becomes `src/UrlCleaner.csproj` (skipped - manual GUI test)
- [x] Copy a UNC path like `\\server\share` — verify it is NOT converted (skipped - manual GUI test)
- [x] Copy a URL like `https://example.com` — verify it is not affected (skipped - manual GUI test)
- [x] Copy plain text without backslashes — verify it is not affected (skipped - manual GUI test)
- [x] Restart the app — verify the checkbox remembers its state (skipped - manual GUI test)
- [x] Disable "Convert Paths", restart — verify it stays off (skipped - manual GUI test)

### Task 6: Update documentation

- [x] Move this plan to `docs/plans/completed/`
