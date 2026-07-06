#!/usr/bin/env bash
# Installs Reddit Wallpaper Rotator (KDE) into an isolated virtualenv under
# ~/.local/share and adds a launcher + application menu entry. No root needed.
set -euo pipefail

APP_ID="reddit-wallpaper-rotator"
SRC_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VENV_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/$APP_ID/venv"
BIN_DIR="$HOME/.local/bin"
DESKTOP_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/applications"

echo "==> Creating virtualenv at $VENV_DIR"
python3 -m venv "$VENV_DIR"
"$VENV_DIR/bin/pip" install --upgrade pip >/dev/null
echo "==> Installing app + dependencies"
"$VENV_DIR/bin/pip" install "$SRC_DIR"

echo "==> Creating launcher at $BIN_DIR/$APP_ID"
mkdir -p "$BIN_DIR"
cat > "$BIN_DIR/$APP_ID" <<EOF
#!/usr/bin/env bash
exec "$VENV_DIR/bin/reddit-wallpaper-rotator" "\$@"
EOF
chmod +x "$BIN_DIR/$APP_ID"

echo "==> Installing application menu entry"
mkdir -p "$DESKTOP_DIR"
sed "s|^Exec=.*|Exec=$BIN_DIR/$APP_ID|" "$SRC_DIR/$APP_ID.desktop" > "$DESKTOP_DIR/$APP_ID.desktop"

echo
echo "Done. Launch it from your app menu (\"Reddit Wallpaper Rotator\") or run:"
echo "    $BIN_DIR/$APP_ID"
echo
if ! command -v plasma-apply-wallpaperimage >/dev/null 2>&1; then
  echo "NOTE: 'plasma-apply-wallpaperimage' was not found on PATH. It ships with"
  echo "      plasma-workspace on Plasma 5.18+/6. A qdbus fallback is used otherwise."
fi
if [[ ":$PATH:" != *":$BIN_DIR:"* ]]; then
  echo "NOTE: $BIN_DIR is not on your PATH; add it to run '$APP_ID' from a terminal."
fi
