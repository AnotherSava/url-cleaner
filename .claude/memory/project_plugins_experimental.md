---
name: project-plugins-experimental
description: Plugins are experimental; the user docs deliberately leave them out, so don't propose adding them
metadata:
  type: project
---

Plugins are an experimental feature, and the user docs (README, `docs/index.md`, `docs/pages/`) deliberately say nothing about them. Only the developer docs describe them. The user decided this on 2026-10-04, when a docs-relevance pass flagged the missing `plugins` block and plugin ids in `configuration.md`.

**Why:** the plugin host has no consumer yet (the What's Next download plugin was dropped), and its plan is back in `docs/plans/draft/`.

**How to apply:** treat the absence of plugins from the user docs as intended, not as a docs gap. Raise it again only when plugins stop being experimental.
