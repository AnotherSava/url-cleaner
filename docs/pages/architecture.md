---
layout: default
title: Architecture
---

[Home](..) | [Configuration](configuration) | [Architecture](architecture) | [Development](development)

---

# Architecture

## Overview

Another URL Cleaner is a .NET WinForms app that runs as a system tray icon using the `ApplicationContext` pattern — no visible window, just a `NotifyIcon` with a context menu.

The app consists of three main components:

| Component | File | Responsibility |
|---|---|---|
| **Tray application** | `TrayApplicationContext.cs` | System tray icon, context menu, user settings |
| **Clipboard monitor** | `ClipboardMonitor.cs` | Win32 clipboard listener, change detection |
| **URL sanitizer** | `UrlSanitizer.cs` | URL cleaning logic, site rule evaluation |

## Clipboard monitoring

The app registers as a clipboard format listener using `AddClipboardFormatListener` (Win32 API). When any application changes the clipboard, Windows sends a `WM_CLIPBOARDUPDATE` message to our invisible `NativeWindow`.

### Flow

1. `WM_CLIPBOARDUPDATE` received
2. Check if clipboard contains text
3. Skip if text matches the last cleaned result (see [Avoiding double-processing](#avoiding-double-processing))
4. Pass text through `UrlSanitizer.TryClean`
5. If no URL change, try `PathConverter.TryConvert` (if enabled)
6. If a cleaned result is produced, store it and replace the clipboard

### Avoiding double-processing

When the app replaces the clipboard with a cleaned URL, Windows fires another `WM_CLIPBOARDUPDATE` notification. If the cleaned URL is itself cleanable (e.g. `stripPathIndex` shifts segment indices after removal), a naive listener would re-clean it and corrupt the result.

To prevent this, the app remembers the last cleaned result. When a clipboard-change notification arrives and the clipboard text matches the last output, processing is skipped entirely. This is simpler and more robust than timing-based flags, which are vulnerable to race conditions between `Clipboard.SetText` returning and the `WM_CLIPBOARDUPDATE` message being dispatched.

## URL cleaning pipeline

`UrlSanitizer.TryClean` processes a URL through these stages in order:

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

Config is loaded from `config.json` next to the executable. If the file doesn't exist, it's generated from `default.json` (embedded as a .NET resource in the DLL). The clipboard monitor watches the config file's last-write time and hot-reloads on change.

## Autostart

The "Start with Windows" option writes to `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` — no config file field, registry-only.
