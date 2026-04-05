---
layout: default
title: Another URL Cleaner
---

[Home](.) | [Configuration](pages/configuration) | [Architecture](pages/architecture) | [Development](pages/development)

---

A Windows background app that automatically strips tracking parameters from URLs on the clipboard.

Another URL Cleaner runs in the system tray and monitors your clipboard. When you copy a URL, it instantly removes tracking parameters and replaces the clipboard contents with the cleaned URL — no manual steps needed.

## Features

- **60+ tracking parameters** stripped by default (Google Analytics, Facebook, HubSpot, Mailchimp, and more)
- **Per-site rules** with path cleaning, slug removal, fragment stripping, and more
- **Configurable** via `config.json` — changes are picked up automatically, no restart needed
- **Pause cleaning** from the tray menu — temporarily disables URL cleaning without exiting
- **Open config location** from the tray menu — opens Explorer with the config file selected
- **Convert paths** — optionally converts Windows backslash paths to forward slashes, toggled from the tray menu
- **Start with Windows** option in the tray menu

## Download

Download the latest release from [GitHub Releases](https://github.com/AnotherSava/url-cleaner/releases).

| Package | Requirements |
|---|---|
| **Self-contained** | None — single exe, just unzip and run |
| **Framework-dependent** | [.NET Desktop Runtime 10](https://dotnet.microsoft.com/download/dotnet/10.0) |

## Usage

1. Download and unzip a release package
2. Run `UrlCleaner.exe` — an icon appears in the system tray
3. Copy any URL — tracking parameters are automatically stripped
4. Right-click the tray icon for options: pause cleaning, open config, start with Windows

## Code signing policy

This project has applied for free code signing through [SignPath Foundation](https://signpath.org), but does not yet meet the community adoption requirements. Until then, Windows will show a SmartScreen warning when you run the executable.

**You can help!** Star the repo, fork it, or contribute — growing the community brings us closer to getting a trusted code signing certificate.

**Privacy:** This program will not transfer any information to other networked systems.
