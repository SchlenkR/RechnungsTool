#!/usr/bin/env bash
# Publishes RechnungsTool as a self-contained macOS app bundle.
#
# Usage:
#   build/publish-macos.sh            # Apple Silicon (osx-arm64)
#   build/publish-macos.sh osx-x64    # Intel Macs
#
# Output: dist/<rid>/RechnungsTool.app  (plus raw publish folder)

set -euo pipefail

RID="${1:-osx-arm64}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/src/RechnungsTool/RechnungsTool.csproj"
OUT="$ROOT/dist/$RID"
PUBLISH="$OUT/publish"
APP="$OUT/RechnungsTool.app"

echo "==> dotnet publish ($RID, self-contained)"
rm -rf "$OUT"
dotnet publish "$PROJECT" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:PublishReadyToRun=true \
    -p:DebugType=none \
    -o "$PUBLISH"

echo "==> Building app bundle $APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH/." "$APP/Contents/MacOS/"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>RechnungsTool</string>
    <key>CFBundleDisplayName</key>
    <string>RechnungsTool</string>
    <key>CFBundleIdentifier</key>
    <string>de.pure-state.rechnungstool</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0.0</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleExecutable</key>
    <string>RechnungsTool</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
</dict>
</plist>
PLIST

echo "==> Ad-hoc code signing"
codesign --force --deep --sign - "$APP"

echo ""
echo "Fertig: $APP"
echo "Starten mit:  open \"$APP\""
