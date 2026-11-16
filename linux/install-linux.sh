#!/usr/bin/env bash
#
# Installs a published Pauth40 Linux build for the current user.
#
# Usage:
#   ./install-linux.sh [publish-directory]
#
# Without an argument the script installs the files that sit next to it, which
# is the layout of the release tarball. The application ends up in
# ~/.local/share/pauth40, the launcher in ~/.local/bin/pauth40 and the icon and
# desktop entry in the usual XDG folders.
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
SOURCE_DIR="${1:-$SCRIPT_DIR}"

if [[ ! -x "$SOURCE_DIR/Pauth40" ]]; then
    echo "Pauth40 executable not found in: $SOURCE_DIR" >&2
    echo "Publish the app first: ./publish.sh" >&2
    exit 1
fi

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
BIN_HOME="${XDG_BIN_HOME:-$HOME/.local/bin}"
APP_DIR="$DATA_HOME/pauth40"
ICON_DIR="$DATA_HOME/icons/hicolor/256x256/apps"
DESKTOP_DIR="$DATA_HOME/applications"

mkdir -p "$APP_DIR" "$ICON_DIR" "$DESKTOP_DIR" "$BIN_HOME"

echo "Installing application files in $APP_DIR"
cp -R "$SOURCE_DIR/." "$APP_DIR/"
chmod +x "$APP_DIR/Pauth40"

if [[ -f "$SOURCE_DIR/pauth40.png" ]]; then
    cp "$SOURCE_DIR/pauth40.png" "$ICON_DIR/pauth40.png"
elif [[ -f "$SOURCE_DIR/Assets/app_mark.png" ]]; then
    cp "$SOURCE_DIR/Assets/app_mark.png" "$ICON_DIR/pauth40.png"
fi

if [[ -f "$SCRIPT_DIR/Pauth40.desktop" ]]; then
    sed "s|^Exec=.*|Exec=$APP_DIR/Pauth40|" \
        "$SCRIPT_DIR/Pauth40.desktop" > "$DESKTOP_DIR/pauth40.desktop"
else
    cat > "$DESKTOP_DIR/pauth40.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Pauth40
Comment=Local TOTP two-factor authenticator
Exec=$APP_DIR/Pauth40
Icon=pauth40
Terminal=false
Categories=Utility;Security;
StartupWMClass=Pauth40
EOF
fi

cat > "$BIN_HOME/pauth40" <<EOF
#!/usr/bin/env sh
exec "$APP_DIR/Pauth40" "\$@"
EOF
chmod +x "$BIN_HOME/pauth40"

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database "$DESKTOP_DIR" >/dev/null 2>&1 || true
fi

if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -f -t "$DATA_HOME/icons/hicolor" >/dev/null 2>&1 || true
fi

echo
echo "Pauth40 installed."
echo "  Application: $APP_DIR/Pauth40"
echo "  Launcher:    $BIN_HOME/pauth40"
echo
echo "If '$BIN_HOME' is not in your PATH, add it to your shell profile:"
echo "  export PATH=\"\$HOME/.local/bin:\$PATH\""
