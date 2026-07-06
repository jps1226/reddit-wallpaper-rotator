"""Download, validate, downscale and thumbnail wallpaper images (Pillow)."""
from __future__ import annotations

import hashlib
import io
from dataclasses import dataclass
from pathlib import Path
from typing import Optional, Tuple

import requests
from PIL import Image

from . import logger, paths
from .config import Settings

THUMB_W, THUMB_H = 320, 180
USER_AGENT = f"linux:com.wallpaperreddit.app:v1.0.0 (image-fetch)"


@dataclass
class DownloadResult:
    success: bool
    reason: str = ""
    file_path: str = ""
    thumb_path: str = ""
    width: int = 0
    height: int = 0
    sha256: str = ""
    bytes: int = 0


def _safe_name(name: str) -> str:
    return "".join(c if c.isalnum() or c in "-_." else "_" for c in name)


def download(candidate_id: str, url: str, settings: Settings,
             screen_size: Tuple[int, int]) -> DownloadResult:
    try:
        resp = requests.get(url, headers={"User-Agent": USER_AGENT}, timeout=60, stream=True)
        resp.raise_for_status()
        clen = resp.headers.get("Content-Length")
        if clen and int(clen) > settings.max_file_bytes:
            return DownloadResult(False, f"too large ({int(clen)//1048576} MB)")
        data = resp.content
    except Exception as ex:
        return DownloadResult(False, f"download error: {ex}")

    if len(data) > settings.max_file_bytes:
        return DownloadResult(False, f"too large ({len(data)//1048576} MB)")

    sha = hashlib.sha256(data).hexdigest()

    try:
        with Image.open(io.BytesIO(data)) as probe:
            probe.verify()  # detect truncated/corrupt files
        img = Image.open(io.BytesIO(data))
        width, height = img.size
    except Exception:
        return DownloadResult(False, "not a valid image")

    if width < settings.min_width or height < settings.min_height:
        return DownloadResult(False, f"too small ({width}x{height})")
    aspect = width / height if height else 0
    if aspect < settings.min_aspect or aspect > settings.max_aspect:
        return DownloadResult(False, f"aspect {aspect:.2f} outside wallpaper range")

    file_path = paths.WALLPAPERS_DIR / (_safe_name(candidate_id) + ".jpg")
    thumb_path = paths.THUMBNAILS_DIR / (_safe_name(candidate_id) + ".jpg")
    try:
        saved = _save_wallpaper(img, file_path, screen_size)
        _make_thumbnail(io.BytesIO(data), thumb_path)
    except Exception as ex:
        return DownloadResult(False, f"save error: {ex}")

    return DownloadResult(True, "", str(file_path), str(thumb_path),
                          width, height, sha, saved)


def _save_wallpaper(img: Image.Image, dest: Path, screen_size: Tuple[int, int]) -> int:
    """Re-encode to a clean baseline JPEG, downscaled with a cover fit so it just
    covers the screen (never upscaled). Normalising the encoding avoids odd
    source formats (CMYK/progressive) failing to render."""
    img = img.convert("RGB")
    sw, sh = screen_size
    if sw < 640 or sh < 480:
        sw, sh = 1920, 1080
    cover = max(sw / img.width, sh / img.height)
    scale = min(1.0, cover)
    if scale < 1.0:
        new_size = (max(1, round(img.width * scale)), max(1, round(img.height * scale)))
        img = img.resize(new_size, Image.LANCZOS)
    dest.parent.mkdir(parents=True, exist_ok=True)
    img.save(dest, "JPEG", quality=92)
    return dest.stat().st_size


def _make_thumbnail(buf: io.BytesIO, dest: Path) -> None:
    with Image.open(buf) as src:
        src = src.convert("RGB")
        scale = max(THUMB_W / src.width, THUMB_H / src.height)
        scaled = src.resize((max(1, round(src.width * scale)),
                             max(1, round(src.height * scale))), Image.LANCZOS)
        left = (scaled.width - THUMB_W) // 2
        top = (scaled.height - THUMB_H) // 2
        cropped = scaled.crop((left, top, left + THUMB_W, top + THUMB_H))
        dest.parent.mkdir(parents=True, exist_ok=True)
        cropped.save(dest, "JPEG", quality=82)


def rebuild_thumbnail(source_file: str, thumb_path: str) -> None:
    with open(source_file, "rb") as fh:
        _make_thumbnail(io.BytesIO(fh.read()), Path(thumb_path))
