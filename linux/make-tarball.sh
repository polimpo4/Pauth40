#!/usr/bin/env bash
#
# Packs a published linux-x64 folder into a portable tarball with executable
# bits set correctly.
#
# Usage:
#   ./make-tarball.sh <source-folder> <target.tar.gz>
#
# It exists because tar on Windows/NTFS does not record the Unix execute bit,
# so a tarball created there installs a Pauth40 binary that cannot start.
set -euo pipefail

SOURCE="${1:?usage: make-tarball.sh <source-folder> <target.tar.gz>}"
TARGET="${2:?usage: make-tarball.sh <source-folder> <target.tar.gz>}"

if [[ ! -x "$SOURCE/Pauth40" && ! -f "$SOURCE/Pauth40" ]]; then
    echo "Missing published binary: $SOURCE/Pauth40" >&2
    exit 1
fi

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

cp -a "$SOURCE/." "$TMP/"
find "$TMP" -type d -exec chmod 755 {} +
find "$TMP" -type f -exec chmod 644 {} +
chmod 755 "$TMP/Pauth40" "$TMP/install-linux.sh" "$TMP/uninstall-linux.sh"

tar -C "$TMP" -czf "$TARGET" .
echo "Tarball: $TARGET"
