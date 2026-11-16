#!/usr/bin/env bash
#
# Publishes Pauth40 desktop builds for Linux and macOS, and the WebAssembly
# build. Run it from a Linux or macOS machine with the .NET 10 SDK installed.
#
#   ./publish.sh              # current runtime only
#   ./publish.sh linux-x64    # a specific runtime identifier
#
# Artifacts are written to ../dist.
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/Pauth40/Pauth40.csproj"
DIST="$(cd "$ROOT/.." && pwd)/dist"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT" | head -n1)"
VERSION="${VERSION:-0.0.0}"

if [[ $# -gt 0 ]]; then
    RID="$1"
elif [[ "$(uname -s)" == "Darwin" ]]; then
    RID="$([[ "$(uname -m)" == "arm64" ]] && echo osx-arm64 || echo osx-x64)"
else
    RID="$([[ "$(uname -m)" == "aarch64" ]] && echo linux-arm64 || echo linux-x64)"
fi

OUT="$DIST/Pauth40-$VERSION-$RID"
rm -rf "$OUT"
mkdir -p "$DIST"

echo "Publishing Pauth40 $VERSION for $RID"
dotnet publish "$PROJECT" \
    -f net10.0-desktop \
    -p:TargetFrameworks=net10.0-desktop \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:UseMonoRuntime=false \
    -p:DebugType=none \
    -o "$OUT"

rm -f "$OUT"/*.pdb

cp "$ROOT/Pauth40.desktop" "$OUT/Pauth40.desktop"
cp "$ROOT/install-linux.sh" "$OUT/install-linux.sh"
cp "$ROOT/uninstall-linux.sh" "$OUT/uninstall-linux.sh"
chmod +x "$OUT/install-linux.sh" "$OUT/uninstall-linux.sh" "$OUT/Pauth40"

if [[ -f "$OUT/Assets/app_mark.png" ]]; then
    cp "$OUT/Assets/app_mark.png" "$OUT/pauth40.png"
fi

if [[ "$RID" == linux-* ]]; then
    ARCHIVE="$DIST/Pauth40-$VERSION-$RID.tar.gz"
    echo "Creating $ARCHIVE"
    tar -C "$OUT" -czf "$ARCHIVE" .
fi

# Optional AppImage. Set APPIMAGETOOL to an appimagetool binary and the
# AppImage is produced from the publish folder.
if [[ "$RID" == linux-* && -n "${APPIMAGETOOL:-}" ]]; then
    APPDIR="$DIST/Pauth40.AppDir"
    rm -rf "$APPDIR"
    mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/icons/hicolor/256x256/apps"

    cp -R "$OUT/." "$APPDIR/usr/bin/"
    rm -rf "$APPDIR/usr/bin/install-linux.sh" "$APPDIR/usr/bin/uninstall-linux.sh"
    cp "$OUT/pauth40.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/pauth40.png"
    cp "$OUT/pauth40.png" "$APPDIR/pauth40.png"
    cp "$ROOT/Pauth40.desktop" "$APPDIR/pauth40.desktop"

    cat > "$APPDIR/AppRun" <<'EOF'
#!/bin/sh
HERE="$(dirname "$(readlink -f "$0")")"
export LD_LIBRARY_PATH="$HERE/usr/bin${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
exec "$HERE/usr/bin/Pauth40" "$@"
EOF
    chmod +x "$APPDIR/AppRun"

    ARCH=x86_64 "$APPIMAGETOOL" "$APPDIR" "$DIST/Pauth40-$VERSION-x86_64.AppImage"
    echo "AppImage: $DIST/Pauth40-$VERSION-x86_64.AppImage"
fi

if [[ "$RID" == linux-x64 ]]; then
    WASM_OUT="$DIST/wasm"
    rm -rf "$WASM_OUT"
    echo "Publishing WebAssembly"
    dotnet publish "$PROJECT" -f net10.0-browserwasm -p:TargetFrameworks=net10.0-browserwasm -c Release -o "$WASM_OUT"
    if [[ -d "$WASM_OUT/wwwroot" ]]; then
        (cd "$WASM_OUT/wwwroot" && zip -qr "$DIST/Pauth40-$VERSION-wasm.zip" .)
        echo "WebAssembly: $DIST/Pauth40-$VERSION-wasm.zip"
    fi
fi

echo
echo "Artifacts in $DIST:"
ls -1 "$DIST"
