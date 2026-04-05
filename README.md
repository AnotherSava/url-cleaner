# Another URL Cleaner

A Windows background app that automatically strips tracking parameters from URLs on the clipboard.

## How it works

Another URL Cleaner runs in the system tray and monitors your clipboard. When you copy a URL, it instantly removes tracking parameters and replaces the clipboard contents with the cleaned URL — no manual steps needed.

### Avoiding double-processing

When the app replaces the clipboard with a cleaned URL, Windows fires another clipboard-change notification. If the cleaned URL is itself cleanable (e.g. `stripPathIndex` shifts segment indices after removal), a naive listener would re-clean it and corrupt the result.

To prevent this, the app remembers the last cleaned result. When a clipboard-change notification arrives and the clipboard text matches the last output, processing is skipped entirely. This is simpler and more robust than timing-based flags, which are vulnerable to race conditions between `Clipboard.SetText` returning and the `WM_CLIPBOARDUPDATE` message being dispatched.

## Features

- **60+ tracking parameters** stripped by default (Google Analytics, Facebook, HubSpot, Mailchimp, and more)
- **Per-site rules** with path cleaning, slug removal, fragment stripping, and more
- **Configurable** via `config.json` (auto-generated on first run)
- **Pause cleaning** from the tray menu — temporarily disables URL cleaning without exiting
- **Open config location** from the tray menu — opens Explorer with the config file selected
- **Convert paths** — optionally converts Windows-style backslash paths (`C:\Users\foo`) to forward-slash paths (`C:/Users/foo`), toggled from the tray menu (disabled by default)
- **Start with Windows** option in the tray menu

## Building from source

**Prerequisites:** Windows 10+, [.NET 10 SDK](https://dotnet.microsoft.com/download)

```
dotnet build src/
dotnet test tests/
```

The built executable will be in `src/bin/Debug/net10.0-windows/`.

## Configuration

On first run, a `config.json` file is created next to the executable with sensible defaults. Changes to the config file are picked up automatically — no restart needed.

### Convert paths

`convertPaths` (default: `false`) — when enabled, clipboard text that looks like a single Windows path (drive-letter or relative, but not UNC paths) is automatically converted to use forward slashes. Toggle this from the tray menu or set it directly in `config.json`.

### Tracking parameters

`trackingParams` — groups of query parameter names to strip from all URLs:

```json
{
  "comment": "Google / GA",
  "params": ["utm_source", "utm_medium", "utm_campaign", "gclid", "..."]
}
```

### Site rules

`siteRules` — per-domain overrides matched by domain suffix. Rules are matched by the `suffix` field, which accepts a single string or an array of strings.

Each option is described below with a config snippet and a before/after example.

---

**`stripAllParams`** — remove all query parameters (keep only those in `excludedParams`)

```json
{ "suffix": "amazon.com", "stripAllParams": true }
```
Before: `https://amazon.com/dp/B123?tag=abc&ref=sr&camp=456`

After: &ensp;`https://amazon.com/dp/B123`

---

**`additionalParams`** — extra parameters to strip for this site, on top of the global list

```json
{ "suffix": "airbnb.ca", "additionalParams": ["location", "search_mode", "category_tag"] }
```
Before: `https://airbnb.ca/rooms/12345?location=Toronto&search_mode=flex&guests=2`

After: &ensp;`https://airbnb.ca/rooms/12345?guests=2`

---

**`excludedParams`** — parameters to keep even when they appear in the global tracking list

```json
{ "suffix": "youtube.com", "excludedParams": ["pp"] }
```
Before: `https://youtube.com/watch?v=abc&utm_source=share&pp=keep`

After: &ensp;`https://youtube.com/watch?v=abc&pp=keep`

---

**`keepPathFrom`** — keep the path starting from the first occurrence of any listed segment, discarding the SEO prefix before it

```json
{ "suffix": "amazon.com", "keepPathFrom": ["dp", "gp"] }
```
Before: `https://amazon.com/Enchanti-Removable-Magnetic/dp/B0DPKB2ZMF`

After: &ensp;`https://amazon.com/dp/B0DPKB2ZMF`

---

**`stripPathSegments`** — remove path segments that start with these prefixes

```json
{ "suffix": "amazon.com", "stripPathSegments": "ref=" }
```
Before: `https://amazon.com/dp/B123/ref=sr_1_8`

After: &ensp;`https://amazon.com/dp/B123`

---

**`stripSlugs`** — strip SEO slug text from path segments that start with digits followed by a hyphen (`2409726-some-slug` becomes `2409726`)

```json
{ "suffix": "makerworld.com", "stripSlugs": true }
```
Before: `https://makerworld.com/en/models/2409726-travel-power-adapter-storage-box`

After: &ensp;`https://makerworld.com/en/models/2409726`

---

**`stripPathIndex`** — remove path segments at specific zero-based indices (accepts a single int or an array)

```json
{ "suffix": "costco.ca", "stripPathIndex": 2 }
```
Before: `https://www.costco.ca/p/-/drano-max-gel-ultra-clog-remover/4000299661`

After: &ensp;`https://www.costco.ca/p/-/4000299661`

---

**`stripFragment`** — remove the URL fragment (`#...`)

```json
{ "suffix": "makerworld.com", "stripFragment": true }
```
Before: `https://makerworld.com/en/models/2409726#profileId-2642005`

After: &ensp;`https://makerworld.com/en/models/2409726`

---

Options compose together. Here's a full Amazon rule that combines multiple features:

```json
{
  "suffix": ["amazon.com", "amazon.ca", "amazon.co.uk"],
  "keepPathFrom": ["dp", "gp"],
  "stripAllParams": true,
  "stripPathSegments": "ref="
}
```
Before: `https://amazon.com/Enchanti-Removable-Magnetic/dp/B0DPKB2ZMF/ref=sr_1_8?tag=abc&camp=123`

After: &ensp;`https://amazon.com/dp/B0DPKB2ZMF`

## Code signing policy

This project has applied for free code signing through [SignPath Foundation](https://signpath.org), but does not yet meet the community adoption requirements. Until then, Windows will show a SmartScreen warning when you run the executable.

**You can help!** Star the repo, fork it, or contribute — growing the community brings us closer to getting a trusted code signing certificate.

**Privacy:** This program will not transfer any information to other networked systems.

## License

[GPL-3.0](LICENSE)
