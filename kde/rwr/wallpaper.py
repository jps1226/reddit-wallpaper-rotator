"""Set the KDE Plasma desktop wallpaper.

Primary path: `plasma-apply-wallpaperimage <file>` (Plasma 5.18+ and Plasma 6).
Fallback: evaluate a script through plasmashell over D-Bus (older Plasma), which
sets the org.kde.image wallpaper plugin on every desktop.
"""
from __future__ import annotations

import shutil
import subprocess
from pathlib import Path

from . import logger

_PLASMA_SCRIPT = """
var all = desktops();
for (var i = 0; i < all.length; i++) {{
    var d = all[i];
    d.wallpaperPlugin = "org.kde.image";
    d.currentConfigGroup = ["Wallpaper", "org.kde.image", "General"];
    d.writeConfig("Image", "file://{path}");
    d.writeConfig("FillMode", 2);
}}
"""


def set_wallpaper(image_path: str) -> bool:
    path = Path(image_path)
    if not path.is_file():
        logger.error(f"Wallpaper file does not exist: {image_path}")
        return False

    if _try_plasma_apply(str(path)):
        return True
    logger.warn("plasma-apply-wallpaperimage unavailable/failed; trying D-Bus fallback.")
    return _try_dbus(str(path))


def _try_plasma_apply(path: str) -> bool:
    exe = shutil.which("plasma-apply-wallpaperimage")
    if not exe:
        return False
    try:
        res = subprocess.run([exe, path], capture_output=True, text=True, timeout=30)
        if res.returncode == 0:
            return True
        logger.warn(f"plasma-apply-wallpaperimage failed: {res.stderr.strip() or res.stdout.strip()}")
        return False
    except Exception as ex:
        logger.warn(f"plasma-apply-wallpaperimage error: {ex}")
        return False


def _try_dbus(path: str) -> bool:
    script = _PLASMA_SCRIPT.format(path=path)
    # qdbus binary name varies across distros / Qt versions.
    for qdbus in ("qdbus6", "qdbus-qt6", "qdbus", "qdbus-qt5"):
        exe = shutil.which(qdbus)
        if not exe:
            continue
        try:
            res = subprocess.run(
                [exe, "org.kde.plasmashell", "/PlasmaShell",
                 "org.kde.PlasmaShell.evaluateScript", script],
                capture_output=True, text=True, timeout=30,
            )
            if res.returncode == 0:
                return True
            logger.warn(f"{qdbus} evaluateScript failed: {res.stderr.strip()}")
        except Exception as ex:
            logger.warn(f"{qdbus} error: {ex}")
    logger.error("Could not set wallpaper: no working plasma-apply-wallpaperimage or qdbus found.")
    return False
