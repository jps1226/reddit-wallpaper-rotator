"""Reads Reddit listings.

Uses application-only OAuth (the installed_client grant) when a client id is
configured (recommended, ~60 req/min), otherwise falls back to the public .json
endpoints (best effort, heavily rate-limited / often IP-blocked). Always sends a
unique, descriptive User-Agent as Reddit requires.
"""
from __future__ import annotations

import html
import time
import uuid
from dataclasses import dataclass
from typing import List, Optional, Tuple

import requests

from . import logger, paths
from .config import Settings

APP_ID = "com.wallpaperreddit.app"
VERSION = "1.0.0"


@dataclass
class Candidate:
    id: str
    title: str
    subreddit: str
    author: str
    permalink: str
    image_url: str
    width: int
    height: int
    score: int
    nsfw: bool


class RedditClient:
    def __init__(self, get_settings):
        self._get_settings = get_settings
        self._device_id = self._load_device_id()
        self._token: Optional[str] = None
        self._token_expiry = 0.0
        self._session = requests.Session()

    # ---- public -------------------------------------------------------
    def fetch_candidates(self) -> List[Candidate]:
        settings = self._get_settings()
        results: List[Candidate] = []
        seen = set()
        for name in settings.normalized_subreddits():
            try:
                for c in self._fetch_subreddit(name, settings):
                    if c.id and c.id not in seen:
                        seen.add(c.id)
                        results.append(c)
            except Exception as ex:
                logger.warn(f"Fetch failed for r/{name}: {ex}")
        logger.info(f"Fetched {len(results)} candidate posts from "
                    f"{len(settings.normalized_subreddits())} subreddit(s).")
        return results

    # ---- internals ----------------------------------------------------
    def _use_oauth(self, settings: Settings) -> bool:
        return bool(settings.reddit_client_id.strip())

    def _user_agent(self, settings: Settings) -> str:
        user = (settings.reddit_username or "anonymous").strip()
        return f"linux:{APP_ID}:v{VERSION} (by /u/{user})"

    def _fetch_subreddit(self, sub: str, settings: Settings) -> List[Candidate]:
        sort = settings.sort.lower()
        limit = max(5, min(100, settings.fetch_limit))
        headers = {"User-Agent": self._user_agent(settings)}

        if self._use_oauth(settings):
            self._ensure_token(settings)
            url = f"https://oauth.reddit.com/r/{sub}/{sort}"
            headers["Authorization"] = f"Bearer {self._token}"
        else:
            url = f"https://www.reddit.com/r/{sub}/{sort}.json"

        params = {"limit": limit, "raw_json": 1}
        if sort == "top":
            params["t"] = settings.time_period.lower()

        resp = self._session.get(url, headers=headers, params=params, timeout=30)
        if resp.status_code == 429:
            logger.warn(f"Rate limited by Reddit on r/{sub}. Add an OAuth client id in Settings.")
            return []
        if resp.status_code == 403:
            logger.warn(f"Reddit blocked anonymous access to r/{sub} (403). "
                        f"Add an OAuth client id in Settings.")
            return []
        resp.raise_for_status()
        return self._parse_listing(resp.json())

    def _parse_listing(self, doc: dict) -> List[Candidate]:
        out: List[Candidate] = []
        children = (doc.get("data") or {}).get("children") or []
        for child in children:
            d = child.get("data") or {}
            if d.get("is_self") or d.get("stickied") or d.get("is_gallery"):
                continue
            url, w, h = self._resolve_image(d)
            if not url:
                continue
            out.append(Candidate(
                id=d.get("name") or ("t3_" + str(d.get("id", ""))),
                title=d.get("title") or "(untitled)",
                subreddit=d.get("subreddit") or "",
                author=d.get("author") or "",
                permalink="https://www.reddit.com" + (d.get("permalink") or ""),
                image_url=url,
                width=w,
                height=h,
                score=int(d.get("score") or 0),
                nsfw=bool(d.get("over_18")),
            ))
        return out

    def _resolve_image(self, d: dict) -> Tuple[Optional[str], int, int]:
        # 1) preview.images[0].source has reliable dimensions.
        preview = d.get("preview") or {}
        images = preview.get("images") or []
        if images:
            source = images[0].get("source") or {}
            url = source.get("url")
            if url:
                url = html.unescape(url)
                if self._looks_like_image(url):
                    return url, int(source.get("width") or 0), int(source.get("height") or 0)
        # 2) direct image url on the post.
        direct = d.get("url_overridden_by_dest") or d.get("url")
        if direct and self._is_direct_image(direct):
            return direct, 0, 0
        return None, 0, 0

    @staticmethod
    def _is_direct_image(url: str) -> bool:
        u = url.lower().split("?", 1)[0]
        return u.endswith((".jpg", ".jpeg", ".png"))

    def _looks_like_image(self, url: str) -> bool:
        u = url.lower()
        return ("preview.redd.it" in u or "i.redd.it" in u
                or "i.imgur.com" in u or self._is_direct_image(url))

    # ---- OAuth (installed_client) -------------------------------------
    def _ensure_token(self, settings: Settings) -> None:
        if self._token and time.time() < self._token_expiry:
            return
        client_id = settings.reddit_client_id.strip()
        resp = self._session.post(
            "https://www.reddit.com/api/v1/access_token",
            auth=(client_id, ""),  # installed apps have no secret
            data={
                "grant_type": "https://oauth.reddit.com/grants/installed_client",
                "device_id": self._device_id,
            },
            headers={"User-Agent": self._user_agent(settings)},
            timeout=25,
        )
        resp.raise_for_status()
        tok = resp.json()
        self._token = tok["access_token"]
        self._token_expiry = time.time() + int(tok.get("expires_in", 3600)) - 60
        logger.info("Obtained Reddit application-only OAuth token.")

    @staticmethod
    def _load_device_id() -> str:
        try:
            if paths.DEVICE_ID_FILE.exists():
                return paths.DEVICE_ID_FILE.read_text().strip()
            paths.DEVICE_ID_FILE.parent.mkdir(parents=True, exist_ok=True)
            dev = uuid.uuid4().hex
            paths.DEVICE_ID_FILE.write_text(dev)
            return dev
        except Exception:
            return uuid.uuid4().hex
