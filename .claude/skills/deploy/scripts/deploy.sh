#!/bin/bash
set -e

INSTALL_DIR="$1"
REPO_DIR="$(cd "$(dirname "$0")/../../../.." && pwd)"

if [ -z "$INSTALL_DIR" ]; then
    echo "Usage: deploy.sh <install-dir>"
    echo "Example: deploy.sh 'C:\Programs\url-cleaner'"
    exit 1
fi

echo "Deploying to: $INSTALL_DIR"

echo "=== Step 1: Stopping running app (if any)..."
powershell.exe -Command "Get-Process UrlCleaner -ErrorAction SilentlyContinue | Stop-Process -Force" || true
echo "Done."

echo "=== Step 2: Building Release publish..."
dotnet publish "$REPO_DIR/src" -c Release -o "$REPO_DIR/src/bin/publish"
echo "Done."

echo "=== Step 3: Deploying to install directory..."
mkdir -p "$INSTALL_DIR"
rm -f "$INSTALL_DIR"/*
cp -rf "$REPO_DIR/src/bin/publish"/* "$INSTALL_DIR/"
echo "Done."

echo "=== Step 4: Launching app..."
powershell.exe -Command "Start-Process '$INSTALL_DIR\UrlCleaner.exe'"
echo "Done."

echo "=== Step 5: Verifying app started..."
sleep 2
if powershell.exe -Command "Get-Process UrlCleaner -ErrorAction Stop" > /dev/null 2>&1; then
    echo "UrlCleaner is running. Deploy successful!"
else
    echo "ERROR: UrlCleaner process not found."
    exit 1
fi
