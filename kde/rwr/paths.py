"""On-disk locations, following the XDG Base Directory spec.

Config  -> $XDG_CONFIG_HOME/reddit-wallpaper-rotator      (settings)
Data    -> $XDG_DATA_HOME/reddit-wallpaper-rotator        (library, thumbnails, log)
Images  -> <Pictures>/Reddit Wallpaper Rotator            (full-size wallpapers)

Wallpapers live under the user's Pictures folder (like the Windows build) so they
are easy to find and always readable by the Plasma wallpaper engine.
"""
from __future__ import annotations

import os
import subprocess
from pathlib import Path

APP_DIRNAME = "reddit-wallpaper-rotator"


def _xdg(env_var: str, default: Path) -> Path:
    val = os.environ.get(env_var)
    return Path(val) if val else default


HOME = Path.home()
CONFIG_DIR = _xdg("XDG_CONFIG_HOME", HOME / ".config") / APP_DIRNAME
DATA_DIR = _xdg("XDG_DATA_HOME", HOME / ".local" / "share") / APP_DIRNAME


def _pictures_dir() -> Path:
    """Resolve the user's Pictures folder via xdg-user-dir, falling back to ~/Pictures."""
    try:
        out = subprocess.run(
            ["xdg-user-dir", "PICTURES"],
            capture_output=True, text=True, timeout=5,
        )
        p = out.stdout.strip()
        if p and Path(p).is_absolute():
            return Path(p)
    except Exception:
        pass
    return HOME / "Pictures"


WALLPAPERS_DIR = _pictures_dir() / "Reddit Wallpaper Rotator"
THUMBNAILS_DIR = DATA_DIR / "thumbnails"

SETTINGS_FILE = CONFIG_DIR / "settings.json"
LIBRARY_FILE = DATA_DIR / "library.json"
BLACKLIST_FILE = DATA_DIR / "blacklist.json"
DEVICE_ID_FILE = DATA_DIR / "device.id"
LOG_FILE = DATA_DIR / "app.log"

AUTOSTART_DIR = _xdg("XDG_CONFIG_HOME", HOME / ".config") / "autostart"
AUTOSTART_FILE = AUTOSTART_DIR / "reddit-wallpaper-rotator.desktop"


def ensure_dirs() -> None:
    for d in (CONFIG_DIR, DATA_DIR, WALLPAPERS_DIR, THUMBNAILS_DIR):
        d.mkdir(parents=True, exist_ok=True)
