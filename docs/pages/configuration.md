---
layout: default
title: Configuration
nav_order: 2
---

# Configuration

On first run, a `config.json` file is created next to the executable with sensible defaults. Changes to the config file are picked up automatically — no restart needed.

If the file has an error, a notification says what's wrong and the last good settings stay in use, or the defaults when it happens at startup. The details go to `url-cleaner.log` next to `config.json`, which also records every notification the app shows.

## Convert paths

`convertPaths` (default: `false`) — when enabled, clipboard text that looks like a single Windows path (drive-letter or relative, but not UNC paths) is automatically converted to use forward slashes. Toggle this from the tray menu or set it directly in `config.json`.

## Convert numbers

`convertNumbers` (default: `false`) — when enabled, clipboard text that is entirely a number written with comma thousands separators (e.g. `10,871.69`) has the commas stripped (`10871.69`). Only whole-value numbers with properly grouped thousands are converted, so European decimals like `10,5` are left untouched. Toggle this from the tray menu or set it directly in `config.json`.

## Convert placeholders

`convertPlaceholders` (default: `false`) — when enabled, clipboard text containing {% raw %}`{{kebab-case}}` placeholders (e.g. `{{tvdb-api-key}}`){% endraw %} has each placeholder replaced with a recently copied clipboard value. Copy the value first, then copy the text containing the placeholder — a single placeholder takes the most recently copied value. When several distinct placeholders are present, they draw from the last few copied values in reading order: the value copied first fills the placeholder that appears first. The app remembers the last 10 distinct clipboard values in memory (cleared when it exits). Toggle this from the tray menu or set it directly in `config.json`.

## Feature order

Each copy goes through the features in a fixed order: URL cleaning, path conversion, number conversion, then placeholder filling. The first feature that changes the text ends the run, so the others never see that copy. An optional `pipeline` block changes both halves of that: the order, and whether a feature that changed the text passes its result on to the next.

```json
"pipeline": {
  "order": [
    { "id": "convertPlaceholders", "stop": false },
    { "id": "urlCleaner" }
  ]
}
```

With this block and placeholders enabled, a template is filled first and the result goes on to URL cleaning. Copying `shoes` and then {% raw %}`https://example.com/?q={{term}}&utm_source=news`{% endraw %} leaves `https://example.com/?q=shoes` on the clipboard. In the default order, URL cleaning takes the template first, strips `utm_source` and ends the run, so the placeholder is never filled.

- `id` names a feature: `urlCleaner`, `convertPaths`, `convertNumbers` or `convertPlaceholders`.
- `stop` (default: `true`) says whether the run ends once this feature has changed the text. Only a feature marked `"stop": false` passes its result on.
- Features the list doesn't name run after the listed ones, in the usual order, each with `stop` set.
- A disabled feature is skipped wherever it is listed.
- An unknown or repeated id is skipped, and a notification names it.

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
