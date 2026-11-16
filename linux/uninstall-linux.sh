#!/usr/bin/env bash
#
# Removes the per-user Pauth40 installation created by install-linux.sh.
# The account vault in ~/.local/share/Pauth40 is preserved.
set -euo pipefail

DATA_HOME="${XDG_DATA_HOME:-$HOME/.local/share}"
BIN_HOME="${XDG_BIN_HOME:-$HOME/.local/bin}"

rm -rf "$DATA_HOME/pauth40"
rm -f "$DATA_HOME/applications/pauth40.desktop"
rm -f "$DATA_HOME/icons/hicolor/256x256/apps/pauth40.png"
rm -f "$BIN_HOME/pauth40"

echo "Pauth40 removed. Your vault in $DATA_HOME/Pauth40 was left untouched."
