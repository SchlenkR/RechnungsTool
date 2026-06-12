#!/usr/bin/env bash
# Installs the latest RechnungsTool release into /Applications.
#
# Downloading via gh/curl (instead of a browser) avoids the macOS quarantine
# attribute entirely - no xattr/Gatekeeper dance needed afterwards.
#
# Usage:  build/install.sh           (or run the one-liner from the README)

set -euo pipefail

REPO="SchlenkR/RechnungsTool"
case "$(uname -m)" in
    arm64) SUFFIX="macos-arm64" ;;
    *)     SUFFIX="macos-x64" ;;
esac

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

echo "==> Lade neuestes Release ($SUFFIX)…"
if command -v gh >/dev/null 2>&1; then
    # gh funktioniert auch für private Repos
    gh release download --repo "$REPO" --pattern "*${SUFFIX}.*" --dir "$TMP"
else
    URL="$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" \
        | grep -oE "https://github.com[^\"]*${SUFFIX}\.(zip|dmg)" | head -1)"
    [ -n "$URL" ] || { echo "Kein Release-Asset gefunden (privates Repo? Dann gh CLI installieren)."; exit 1; }
    curl -fsSL "$URL" -o "$TMP/asset_${SUFFIX}${URL##*${SUFFIX}}"
fi

echo "==> Installiere nach /Applications…"
ASSET="$(ls "$TMP"/*"${SUFFIX}".* | head -1)"
case "$ASSET" in
    *.dmg)
        MOUNT="$TMP/mnt"
        hdiutil attach -nobrowse -quiet -mountpoint "$MOUNT" "$ASSET"
        rm -rf /Applications/RechnungsTool.app
        ditto "$MOUNT/RechnungsTool.app" /Applications/RechnungsTool.app
        hdiutil detach -quiet "$MOUNT"
        ;;
    *.zip)
        ditto -x -k "$ASSET" "$TMP/extracted"
        rm -rf /Applications/RechnungsTool.app
        ditto "$TMP/extracted/RechnungsTool.app" /Applications/RechnungsTool.app
        ;;
esac

echo "Fertig: /Applications/RechnungsTool.app (keine Quarantäne, startet direkt)"
