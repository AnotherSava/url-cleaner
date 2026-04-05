---
layout: default
title: Configuration
---

<style>
.url-example { font-family: monospace; font-size: 0.9em; word-break: break-all; }
.url-example del { color: #e74c3c; text-decoration: none; }
.url-example ins { color: #2ecc71; text-decoration: none; }
</style>

[Home](..) | [Configuration](configuration) | [Architecture](architecture) | [Development](development)

---

# Configuration

On first run, a `config.json` file is created next to the executable with sensible defaults. Changes to the config file are picked up automatically — no restart needed.

## Convert paths

`convertPaths` (default: `false`) — when enabled, clipboard text that looks like a single Windows path (drive-letter or relative, but not UNC paths) is automatically converted to use forward slashes. Toggle this from the tray menu or set it directly in `config.json`.

## Tracking parameters

`trackingParams` — groups of query parameter names to strip from all URLs:

```json
{
  "comment": "Google / GA",
  "params": ["utm_source", "utm_medium", "utm_campaign", "gclid", "..."]
}
```

## Site rules

`siteRules` — per-domain overrides matched by domain suffix. Rules are matched by the `suffix` field, which accepts a single string or an array of strings.

Each option is described below with a URL example where <del>red text</del> marks the parts that get removed, followed by the config snippet.

---

**`stripAllParams`** — remove all query parameters (keep only those in `excludedParams`)

<p class="url-example">https://amazon.com/dp/B123<del>?tag=abc&amp;ref=sr&amp;camp=456</del></p>

```json
{ "suffix": "amazon.com", "stripAllParams": true }
```

---

**`additionalParams`** — extra parameters to strip for this site, on top of the global list

<p class="url-example">https://airbnb.ca/rooms/12345?<del>location=Toronto&amp;</del><del>search_mode=flex&amp;</del>guests=2</p>

```json
{ "suffix": "airbnb.ca", "additionalParams": ["location", "search_mode", "category_tag"] }
```

---

**`excludedParams`** — parameters to keep even when they appear in the global tracking list

<p class="url-example">https://youtube.com/watch?v=abc<del>&amp;utm_source=share</del>&amp;<ins>pp=keep</ins></p>

```json
{ "suffix": "youtube.com", "excludedParams": ["pp"] }
```

---

**`keepPathFrom`** — keep the path starting from the first occurrence of any listed segment, discarding the SEO prefix before it

<p class="url-example">https://amazon.com/<del>Enchanti-Removable-Magnetic/</del>dp/B0DPKB2ZMF</p>

```json
{ "suffix": "amazon.com", "keepPathFrom": ["dp", "gp"] }
```

---

**`stripPathSegments`** — remove path segments that start with these prefixes

<p class="url-example">https://amazon.com/dp/B123/<del>ref=sr_1_8</del></p>

```json
{ "suffix": "amazon.com", "stripPathSegments": "ref=" }
```

---

**`stripSlugs`** — strip SEO slug text from path segments that start with digits followed by a hyphen

<p class="url-example">https://makerworld.com/en/models/2409726<del>-travel-power-adapter-storage-box</del></p>

```json
{ "suffix": "makerworld.com", "stripSlugs": true }
```

---

**`stripPathIndex`** — remove path segments at specific zero-based indices (accepts a single int or an array)

<p class="url-example">https://www.costco.ca/p/-/<del>drano-max-gel-ultra-clog-remover/</del>4000299661</p>

```json
{ "suffix": "costco.ca", "stripPathIndex": 2 }
```

---

**`stripFragment`** — remove the URL fragment (`#...`)

<p class="url-example">https://makerworld.com/en/models/2409726<del>#profileId-2642005</del></p>

```json
{ "suffix": "makerworld.com", "stripFragment": true }
```

---

## Composing options

Options compose together. Here's a full Amazon rule that combines multiple features:

<p class="url-example">https://amazon.com/<del>Enchanti-Removable-Magnetic/</del>dp/B0DPKB2ZMF/<del>ref=sr_1_8</del><del>?tag=abc&amp;camp=123</del></p>

```json
{
  "suffix": ["amazon.com", "amazon.ca", "amazon.co.uk"],
  "keepPathFrom": ["dp", "gp"],
  "stripAllParams": true,
  "stripPathSegments": "ref="
}
```
