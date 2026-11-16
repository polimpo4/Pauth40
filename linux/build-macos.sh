#!/usr/bin/env bash
#
# Packages a published macOS desktop folder as a Pauth40.app bundle and zip.
#
# Usage:
#   ./build-macos.sh <rid> [version]
#
# Example: ./build-macos.sh osx-arm64
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
DIST="$(cd "$ROOT/.." && pwd)/dist"
RID="${1:?usage: build-macos.sh <rid> [version]}"
VERSION="${2:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Pauth40/Pauth40.csproj" | head -n1)}"
SOURCE="$DIST/Pauth40-$VERSION-$RID"
APP="$DIST/Pauth40.app"
ZIP="$DIST/Pauth40-$VERSION-$RID.zip"

if [[ ! -x "$SOURCE/Pauth40" ]]; then
    echo "Missing published folder: $SOURCE" >&2
    exit 1
fi

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$SOURCE/." "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/Pauth40"

cp "$DIST/pauth40.icns" "$APP/Contents/Resources/pauth40.icns" 2>/dev/null || true

cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Pauth40</string>
  <key>CFBundleDisplayName</key><string>Pauth40</string>
  <key>CFBundleIdentifier</key><string>com.pauth40.authenticator</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>Pauth40</string>
  <key>CFBundleIconFile</key><string>pauth40</string>
  <key>LSMinimumSystemVersion</key><string>10.15</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
EOF

rm -f "$ZIP"
python3 - "$APP" "$ZIP" <<'PY'
import os
import sys
import zipfile

app, target = sys.argv[1], sys.argv[2]
base = os.path.dirname(app)

with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
    for folder, _, files in os.walk(app):
        for name in files:
            path = os.path.join(folder, name)
            arcname = os.path.relpath(path, base)
            info = zipfile.ZipInfo(arcname)
            info.external_attr = (os.stat(path).st_mode & 0xFFFF) << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            with open(path, "rb") as source, archive.open(info, "w") as sink:
                while True:
                    chunk = source.read(1024 * 1024)
                    if not chunk:
                        break
                    sink.write(chunk)

print(target)
PY

echo "macOS: $ZIP"
