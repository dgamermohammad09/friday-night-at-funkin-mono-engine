#!/bin/sh
# Linux / macOS: make the game's Content folder (~1.1 GB, not shipped in this folder) visible next to the executable.
#   ./link_content.sh /path/to/FNF_MonoGame_Full/Content          (symlink, no copy)
#   ./link_content.sh /path/to/Content --copy                       (real copy)
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$1"
if [ -z "$SRC" ] || [ ! -d "$SRC/data" ]; then
  echo "usage: $0 <path to Content folder (must contain data/, images/, songs/ ...)> [--copy]"; exit 1
fi
rm -rf "$HERE/Content"
if [ "$2" = "--copy" ]; then cp -R "$SRC" "$HERE/Content"; else ln -s "$(cd "$SRC" && pwd)" "$HERE/Content"; fi
chmod +x "$HERE/FNF_DesktopGL" 2>/dev/null || true
# macOS: binaries copied from another machine are quarantined by Gatekeeper
if [ "$(uname)" = "Darwin" ]; then
  xattr -dr com.apple.quarantine "$HERE" 2>/dev/null || true
  # Apple Silicon refuses unsigned arm64 executables: ad-hoc sign the app host (built on Windows, so unsigned)
  codesign --force -s - "$HERE/FNF_DesktopGL" 2>/dev/null || true
fi
echo "Content ready -> $HERE/Content.  Start the game with: $HERE/FNF_DesktopGL"
