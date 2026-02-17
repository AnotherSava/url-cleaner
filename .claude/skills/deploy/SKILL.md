---
name: deploy
description: Build and deploy URL Cleaner to local install directory
disable-model-invocation: false
allowed-tools: AskUserQuestion, Read(config/deploy.env), Write(config/deploy.env), Bash(bash .claude/skills/deploy/scripts/deploy.sh *)
---

Deploy URL Cleaner to a local install directory. Run these steps in order:

**Step 1 — Resolve install directory:**
- Read the file `config/deploy.env` (relative to the repo root)
- If it exists, use the value of the `INSTALL_DIR` line as the install path
- If it does NOT exist or the read fails, you MUST ask the user with `AskUserQuestion`: "Where should URL Cleaner be installed?" with the default option `C:/Programs/url-cleaner`
  - Save their answer to `config/deploy.env` as `INSTALL_DIR=<their answer>`
- NEVER guess or assume the install directory — always read the config file or ask the user
**Step 2 — Run the deploy script:**
```
bash .claude/skills/deploy/scripts/deploy.sh <install-dir>
```
IMPORTANT: Use the relative path exactly as shown above. Do NOT use an absolute path — the allowed-tools permission pattern requires the relative path to match.

The script handles: stop app → build → clean install dir → copy → launch → verify.

Report the script output to the user. If it fails, show the errors.
