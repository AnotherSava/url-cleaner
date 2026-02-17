---
name: deploy
description: Build and deploy URL Cleaner to local install directory
disable-model-invocation: false
allowed-tools: Bash, AskUserQuestion, Read, Write
---

Deploy URL Cleaner to a local install directory. Run these steps in order:

**Step 0 — Resolve install directory:**
- Read the file `.claude/skills/deploy/config` (relative to the repo root)
- If it exists, use the value of the `INSTALL_DIR` line as the install path
- If it does NOT exist, ask the user with `AskUserQuestion`: "Where should URL Cleaner be installed?" with the default option `C:\Programs\url-cleaner`
  - Save their answer to `.claude/skills/deploy/config` as `INSTALL_DIR=<their answer>`
- Use the resolved path as `$INSTALL_DIR` in all subsequent steps (convert to both Unix `/c/...` and Windows `C:\...` forms as needed)

1. **Stop the running app** (if any):
   ```
   powershell.exe -Command "Get-Process UrlCleaner -ErrorAction SilentlyContinue | Stop-Process -Force"
   ```

2. **Build a Release publish** (framework-dependent, smaller output):
   ```
   "/c/Program Files/dotnet/dotnet.exe" publish D:/projects/url-cleaner/src -c Release -o D:/projects/url-cleaner/src/bin/publish
   ```

3. **Copy publish output** to the install directory:
   ```
   mkdir -p $INSTALL_DIR && cp -rf D:/projects/url-cleaner/src/bin/publish/* $INSTALL_DIR/
   ```

4. **Remove config.json** so the app regenerates it from the embedded default:
   ```
   rm -f $INSTALL_DIR/config.json
   ```

5. **Launch the app** (start detached so it outlives the shell):
   ```
   powershell.exe -Command "Start-Process '$INSTALL_DIR\UrlCleaner.exe'"
   ```

6. **Verify the app started** (wait briefly, then check for the process):
   ```
   sleep 2 && powershell.exe -Command "Get-Process UrlCleaner -ErrorAction Stop"
   ```

Report success or failure after each step. If the build fails, stop and show the errors.
