#!/usr/bin/env bash
#
# Builds the Linux AppImage from a published linux-x64 folder.
#
# Usage:
#   ./build-appimage.sh [version]
#
# It expects ../dist/Pauth40-<version>-linux-x64 to exist (produced by
# publish.sh or publish.ps1). Set APPIMAGETOOL to use a local appimagetool
# binary; otherwise the script downloads it.
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
DIST="$(cd "$ROOT/.." && pwd)/dist"
VERSION="${1:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/Pauth40/Pauth40.csproj" | head -n1)}"
SOURCE="$DIST/Pauth40-$VERSION-linux-x64"
TARGET="$DIST/Pauth40-$VERSION-x86_64.AppImage"

if [[ ! -x "$SOURCE/Pauth40" ]]; then
    echo "Missing published folder: $SOURCE" >&2
    echo "Run ./publish.sh linux-x64 first." >&2
    exit 1
fi

APPIMAGETOOL="${APPIMAGETOOL:-}"
if [[ -z "$APPIMAGETOOL" ]]; then
    APPIMAGETOOL="$(command -v appimagetool || true)"
fi
if [[ -z "$APPIMAGETOOL" ]]; then
    WORK="$HOME/.cache/pauth40-appimagetool"
    mkdir -p "$WORK"
    APPIMAGETOOL="$WORK/appimagetool"
    if [[ ! -x "$APPIMAGETOOL" ]]; then
        echo "Downloading appimagetool"
        curl -fsSL -o "$APPIMAGETOOL" \
            "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
        chmod +x "$APPIMAGETOOL"
    fi
fi

WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

APPDIR="$WORKDIR/Pauth40.AppDir"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/icons/hicolor/256x256/apps"

cp -R "$SOURCE/." "$APPDIR/usr/bin/"
rm -f "$APPDIR/usr/bin/install-linux.sh" "$APPDIR/usr/bin/uninstall-linux.sh"
cp "$APPDIR/usr/bin/pauth40.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/pauth40.png"
cp "$APPDIR/usr/bin/pauth40.png" "$APPDIR/pauth40.png"
cp "$ROOT/Pauth40.desktop" "$APPDIR/pauth40.desktop"

cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export LD_LIBRARY_PATH="$HERE/usr/bin${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec "$HERE/usr/bin/Pauth40" "$@"
EOF

chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/Pauth40"

ARCH=x86_64 "$APPIMAGETOOL" --appimage-extract-and-run "$APPDIR" "$TARGET"
echo "AppImage: $TARGET"
