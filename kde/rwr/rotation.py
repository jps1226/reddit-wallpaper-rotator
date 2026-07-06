"""Orchestrates one rotation: fetch -> filter -> rank -> download -> set -> record -> clean up.

Selection is deterministic (highest score first, skipping already-seen items) so we
avoid random/odd picks while still cycling through fresh wallpapers.
"""
from __future__ import annotations

import threading
import time
from typing import Callable, List, Optional, Tuple

from . import images, logger, wallpaper
from .library import Library, WallpaperItem
from .reddit import Candidate, RedditClient


class RotationEngine:
    def __init__(self, reddit: RedditClient, library: Library,
                 get_settings, get_screen_size: Callable[[], Tuple[int, int]]):
        self._reddit = reddit
        self._library = library
        self._get_settings = get_settings
        self._get_screen_size = get_screen_size
        self._lock = threading.Lock()
        self.status_cb: Optional[Callable[[str], None]] = None
        self.wallpaper_changed_cb: Optional[Callable[[WallpaperItem], None]] = None
        self.current_item_id: str = ""

    def _report(self, msg: str):
        logger.info(msg)
        if self.status_cb:
            self.status_cb(msg)

    def rotate(self) -> Optional[WallpaperItem]:
        if not self._lock.acquire(blocking=False):
            self._report("A rotation is already in progress.")
            return None
        try:
            settings = self._get_settings()
            self._report("Fetching wallpapers from Reddit…")
            candidates = self._reddit.fetch_candidates()
            ranked = self._rank(candidates, settings)
            if not ranked:
                self._report("No suitable new wallpapers found. Try more subreddits or a wider time period.")
                return None

            screen = self._get_screen_size()
            for cand in ranked:
                existing = self._library.get(cand.id)
                if existing and existing.file_exists():
                    self._apply(existing)
                    return existing

                self._report(f"Downloading: {_trim(cand.title)}")
                result = images.download(cand.id, cand.image_url, settings, screen)
                if not result.success:
                    logger.info(f"Skipped '{_trim(cand.title)}' — {result.reason}.")
                    continue

                if self._library.has_hash(result.sha256) or \
                        self._library.is_blacklisted(sha=result.sha256):
                    logger.info(f"Skipped '{_trim(cand.title)}' — duplicate/blacklisted content.")
                    continue

                item = WallpaperItem(
                    id=cand.id, title=cand.title, subreddit=cand.subreddit,
                    author=cand.author, permalink=cand.permalink, source_url=cand.image_url,
                    file_path=result.file_path, thumb_path=result.thumb_path,
                    width=result.width, height=result.height, file_bytes=result.bytes,
                    sha256=result.sha256, downloaded_at=time.time())
                self._library.add_or_update(item)
                self._apply(item)
                self._library.cleanup(settings.max_stored, settings.keep_favorites, item.id)
                return item

            self._report("Could not download a usable wallpaper this time.")
            return None
        except Exception as ex:
            logger.error(f"Rotation failed: {ex}")
            self._report(f"Rotation failed: {ex}")
            return None
        finally:
            self._lock.release()

    def apply_existing(self, item: WallpaperItem) -> bool:
        if not item or not item.file_exists():
            return False
        self._apply(item)
        return True

    def _apply(self, item: WallpaperItem):
        if wallpaper.set_wallpaper(item.file_path):
            self.current_item_id = item.id
            self._library.mark_shown(item.id, time.time())
            self._report(f"Wallpaper set: {_trim(item.title)} (r/{item.subreddit})")
            if self.wallpaper_changed_cb:
                self.wallpaper_changed_cb(item)
        else:
            self._report("Failed to apply wallpaper (see log).")

    def _rank(self, candidates: List[Candidate], settings) -> List[Candidate]:
        used = self._library.used_ids()

        def eligible(c: Candidate) -> bool:
            if not c.image_url:
                return False
            if c.nsfw and not settings.allow_nsfw:
                return False
            if self._library.is_blacklisted(item_id=c.id, url=c.image_url):
                return False
            if c.width and c.height:
                if c.width < settings.min_width or c.height < settings.min_height:
                    return False
                aspect = c.width / c.height
                if aspect < settings.min_aspect or aspect > settings.max_aspect:
                    return False
            return True

        pool = [c for c in candidates if eligible(c)]
        fresh = sorted([c for c in pool if c.id not in used],
                       key=lambda c: c.score, reverse=True)
        if fresh:
            return fresh

        # Everything seen — recycle, least-recently-shown first.
        def last_shown(c: Candidate) -> float:
            it = self._library.get(c.id)
            return it.last_shown_at if it else 0.0

        return sorted(pool, key=lambda c: (last_shown(c), -c.score))


def _trim(s: str) -> str:
    if not s:
        return ""
    return s if len(s) <= 70 else s[:70] + "…"
