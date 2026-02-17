# Another URL Cleaner

A Windows background app that automatically strips tracking parameters from URLs on the clipboard.

## How it works

Another URL Cleaner runs in the system tray and monitors your clipboard. When you copy a URL, it instantly removes tracking parameters and replaces the clipboard contents with the cleaned URL — no manual steps needed.

## Features

- **60+ tracking parameters** stripped by default (Google Analytics, Facebook, HubSpot, Mailchimp, and more)
- **Per-site rules** with path cleaning, slug removal, fragment stripping, and more
- **Configurable** via `config.json` (auto-generated on first run)
- **Pause cleaning** from the tray menu — temporarily disables URL cleaning without exiting
- **Open config location** from the tray menu — opens Explorer with the config file selected
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

Another URL Cleaner is signed with free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

**Team roles:**
- Author, reviewer, and approver: [AnotherSava](https://github.com/AnotherSava)

**Privacy:** This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

## License

[GPL-3.0](LICENSE)
