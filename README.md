# Another URL Cleaner

A Windows background app that automatically strips tracking parameters from URLs on the clipboard.

## How it works

Another URL Cleaner runs in the system tray and monitors your clipboard. When you copy a URL, it instantly removes tracking parameters and replaces the clipboard contents with the cleaned URL — no manual steps needed.

## Features

- **60+ tracking parameters** stripped by default (Google Analytics, Facebook, HubSpot, Mailchimp, and more)
- **Per-site rules** — e.g. Amazon: strip all query params and `ref=` path segments
- **Configurable** via `config.json` (auto-generated on first run)
- **Start with Windows** option in the tray menu

## Building from source

**Prerequisites:** Windows 10+, [.NET 10 SDK](https://dotnet.microsoft.com/download)

```
dotnet build src/
dotnet test tests/
```

The built executable will be in `src/bin/Debug/net10.0-windows/`.

## Configuration

On first run, a `config.json` file is created next to the executable with sensible defaults.

**`trackingParams`** — groups of query parameter names to strip from all URLs:

```json
{
  "comment": "Google / GA",
  "params": ["utm_source", "utm_medium", "utm_campaign", "gclid", "..."]
}
```

**`siteRules`** — per-domain overrides matched by domain suffix:

```json
{
  "suffix": ["amazon.com", "amazon.ca", "amazon.co.uk"],
  "enabled": true,
  "stripAllParams": true,
  "stripPathSegments": "ref="
}
```

Site rules support:
- `stripAllParams` — remove all query params (keep only those in `excludedParams`)
- `additionalParams` — extra params to strip for this site
- `excludedParams` — params to keep even if they're in the global list
- `stripPathSegments` — remove path segments matching these prefixes

## Code signing policy

Another URL Cleaner is signed with free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

**Team roles:**
- Author, reviewer, and approver: [AnotherSava](https://github.com/AnotherSava)

**Privacy:** This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

## License

[GPL-3.0](LICENSE)
