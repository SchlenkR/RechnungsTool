#!/usr/bin/env bash
# Lokaler Release direkt vom Mac – schneller als die GitHub Action.
# Baut beide macOS-Architekturen, zippt sie OTA-kompatibel und veröffentlicht
# das GitHub-Release per gh. Die CI erkennt ein bereits bestehendes Release
# und überspringt sich selbst (siehe .github/workflows/release.yml).
#
# Usage:
#   build/release-macos.sh            # Patch-Version automatisch hochzählen
#   build/release-macos.sh v1.2.0     # explizite Version (Minor/Major)
#   build/release-macos.sh 1.2.0      # (führendes v optional)

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
REPO="SchlenkR/RechnungsTool"

command -v gh >/dev/null || { echo "Fehlt: gh (GitHub CLI) – 'brew install gh'"; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "Bitte zuerst 'gh auth login' ausführen."; exit 1; }

if [ -n "$(git status --porcelain)" ]; then
    echo "Es gibt uncommittete Änderungen. Bitte erst committen (der Release zeigt auf den aktuellen Commit)."
    exit 1
fi

BRANCH="$(git rev-parse --abbrev-ref HEAD)"
git fetch --tags --quiet origin || true

# --- Version bestimmen (gleiche Logik wie die CI) ---
if [ "${1:-}" != "" ]; then
    TAG="$1"; [[ "$TAG" == v* ]] || TAG="v$TAG"
else
    LETZTER="$(git tag --list 'v*' --sort=-v:refname | head -1)"; LETZTER="${LETZTER:-v0.0.0}"
    IFS=. read -r MAJ MIN PATCH <<< "${LETZTER#v}"
    PATCH=$((PATCH + 1))
    while git ls-remote --exit-code --tags origin "refs/tags/v${MAJ}.${MIN}.${PATCH}" >/dev/null 2>&1; do
        PATCH=$((PATCH + 1))
    done
    TAG="v${MAJ}.${MIN}.${PATCH}"
fi
VERSION="${TAG#v}"

if gh release view "$TAG" -R "$REPO" >/dev/null 2>&1; then
    echo "Release $TAG existiert bereits – abgebrochen."
    exit 1
fi
echo "==> Release $TAG (Branch $BRANCH)"

# --- Bauen (beide Architekturen) ---
build/publish-macos.sh osx-arm64 "$VERSION"
build/publish-macos.sh osx-x64   "$VERSION"

# --- Zippen (Namen müssen auf macos-<arch>.zip enden → OTA-Update findet sie) ---
ARM="dist/RechnungsTool-${TAG}-macos-arm64.zip"
X64="dist/RechnungsTool-${TAG}-macos-x64.zip"
rm -f "$ARM" "$X64"
ditto -c -k --sequesterRsrc --keepParent dist/osx-arm64/RechnungsTool.app "$ARM"
ditto -c -k --sequesterRsrc --keepParent dist/osx-x64/RechnungsTool.app   "$X64"

# --- Aktuellen Commit nach origin bringen (das Release-Tag zeigt darauf) ---
git push origin HEAD

NOTES="$(mktemp)"
cat > "$NOTES" <<EOF
## Installation (macOS)

Empfohlen – per Terminal, dann gibt es keine Gatekeeper-Quarantäne:

\`\`\`
gh release download -R ${REPO} -p '*arm64.zip' -D /tmp/rt && ditto -x -k /tmp/rt/*.zip /Applications && rm -rf /tmp/rt
\`\`\`

(Intel-Mac: \`x64\` statt \`arm64\`.) Alternativ per Browser laden, nach **Programme**
ziehen und einmalig \`xattr -cr /Applications/RechnungsTool.app\` ausführen.
EOF

# --- Release veröffentlichen (gh legt das Tag am aktuellen Commit an) ---
gh release create "$TAG" "$ARM" "$X64" \
    -R "$REPO" \
    --target "$(git rev-parse HEAD)" \
    --title "RechnungsTool $TAG" \
    --notes-file "$NOTES"
rm -f "$NOTES"

echo ""
echo "Fertig: https://github.com/${REPO}/releases/tag/${TAG}"
