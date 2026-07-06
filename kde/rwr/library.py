"""Wallpaper history plus the blacklist, persisted as JSON. Thread-safe."""
from __future__ import annotations

import json
import threading
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import List, Optional

from . import logger, paths


@dataclass
class WallpaperItem:
    id: str
    title: str = ""
    subreddit: str = ""
    author: str = ""
    permalink: str = ""
    source_url: str = ""
    file_path: str = ""
    thumb_path: str = ""
    width: int = 0
    height: int = 0
    file_bytes: int = 0
    sha256: str = ""
    downloaded_at: float = 0.0
    last_shown_at: float = 0.0
    favorite: bool = False

    def file_exists(self) -> bool:
        return bool(self.file_path) and Path(self.file_path).is_file()


@dataclass
class BlacklistEntry:
    id: str = ""
    sha256: str = ""
    source_url: str = ""
    title: str = ""
    added_at: float = 0.0


def _read_list(path: Path, cls):
    try:
        if path.exists():
            raw = json.loads(path.read_text(encoding="utf-8"))
            known = {f for f in cls().__dict__} if _has_default_ctor(cls) else None
            out = []
            for row in raw:
                if known is not None:
                    row = {k: v for k, v in row.items() if k in known}
                out.append(cls(**row))
            return out
    except Exception as ex:
        logger.error(f"Failed to read {path.name}: {ex}")
    return []


def _has_default_ctor(cls) -> bool:
    try:
        cls()
        return True
    except TypeError:
        return False


def _write_list(path: Path, items) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps([asdict(i) for i in items], indent=2), encoding="utf-8")
    tmp.replace(path)


class Library:
    def __init__(self):
        self._lock = threading.RLock()
        self._items: List[WallpaperItem] = []
        self._blacklist: List[BlacklistEntry] = []
        self._changed_listeners = []

    def add_changed_listener(self, fn):
        self._changed_listeners.append(fn)

    def _notify(self):
        for fn in list(self._changed_listeners):
            try:
                fn()
            except Exception:
                pass

    def load(self):
        with self._lock:
            self._items = _read_list(paths.LIBRARY_FILE, WallpaperItem)
            self._blacklist = _read_list(paths.BLACKLIST_FILE, BlacklistEntry)

    # ---- queries ------------------------------------------------------
    def history(self) -> List[WallpaperItem]:
        with self._lock:
            return sorted(self._items, key=lambda i: i.downloaded_at, reverse=True)

    def get(self, item_id: str) -> Optional[WallpaperItem]:
        with self._lock:
            return next((i for i in self._items if i.id == item_id), None)

    def used_ids(self):
        with self._lock:
            return {i.id for i in self._items}

    def has_hash(self, sha: str) -> bool:
        with self._lock:
            return bool(sha) and any(i.sha256 == sha for i in self._items)

    def is_blacklisted(self, item_id="", sha="", url="") -> bool:
        with self._lock:
            return any(
                (item_id and b.id == item_id)
                or (sha and b.sha256 == sha)
                or (url and b.source_url == url)
                for b in self._blacklist
            )

    def blacklist_entries(self) -> List[BlacklistEntry]:
        with self._lock:
            return sorted(self._blacklist, key=lambda b: b.added_at, reverse=True)

    # ---- mutations ----------------------------------------------------
    def add_or_update(self, item: WallpaperItem):
        with self._lock:
            self._items = [i for i in self._items if i.id != item.id]
            self._items.append(item)
            _write_list(paths.LIBRARY_FILE, self._items)
        self._notify()

    def mark_shown(self, item_id: str, when: float):
        with self._lock:
            it = self.get(item_id)
            if not it:
                return
            it.last_shown_at = when
            _write_list(paths.LIBRARY_FILE, self._items)
        self._notify()

    def set_favorite(self, item_id: str, fav: bool):
        with self._lock:
            it = self.get(item_id)
            if not it:
                return
            it.favorite = fav
            _write_list(paths.LIBRARY_FILE, self._items)
        self._notify()

    def remove(self, item_id: str, delete_files: bool):
        with self._lock:
            it = self.get(item_id)
            if not it:
                return
            self._items.remove(it)
            if delete_files:
                _delete_files(it)
            _write_list(paths.LIBRARY_FILE, self._items)
        self._notify()

    def blacklist(self, item: WallpaperItem):
        import time
        with self._lock:
            if item.id and not any(b.id == item.id for b in self._blacklist):
                self._blacklist.append(BlacklistEntry(
                    id=item.id, sha256=item.sha256, source_url=item.source_url,
                    title=item.title, added_at=time.time()))
                _write_list(paths.BLACKLIST_FILE, self._blacklist)
            existing = self.get(item.id)
            if existing:
                self._items.remove(existing)
                _delete_files(existing)
                _write_list(paths.LIBRARY_FILE, self._items)
        self._notify()

    def remove_from_blacklist(self, item_id="", sha="", url=""):
        with self._lock:
            self._blacklist = [
                b for b in self._blacklist
                if not ((item_id and b.id == item_id)
                        or (sha and b.sha256 == sha)
                        or (url and b.source_url == url))
            ]
            _write_list(paths.BLACKLIST_FILE, self._blacklist)
        self._notify()

    def cleanup(self, max_non_favorites: int, keep_favorites: bool, protect_id: str) -> int:
        removed = 0
        with self._lock:
            non_fav = sorted(
                [i for i in self._items
                 if (not i.favorite or not keep_favorites) and i.id != protect_id],
                key=lambda i: i.downloaded_at, reverse=True)
            for it in non_fav[max(0, max_non_favorites):]:
                self._items.remove(it)
                _delete_files(it)
                removed += 1
            if removed:
                _write_list(paths.LIBRARY_FILE, self._items)
        if removed:
            self._notify()
        return removed


def _delete_files(it: WallpaperItem):
    for p in (it.file_path, it.thumb_path):
        try:
            if p and Path(p).is_file():
                Path(p).unlink()
        except Exception as ex:
            logger.warn(f"Could not delete {p}: {ex}")
