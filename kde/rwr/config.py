"""User settings, persisted as JSON."""
from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from typing import List

from . import logger, paths

SORTS = ["top", "hot", "new", "rising"]
PERIODS = ["hour", "day", "week", "month", "year", "all"]


@dataclass
class Settings:
    subreddits: List[str] = field(default_factory=lambda: ["wallpaper", "wallpapers", "EarthPorn"])
    interval_minutes: int = 60
    sort: str = "top"
    time_period: str = "week"
    fetch_limit: int = 50

    # Quality gates: real, wallpaper-sized, landscape-ish images only.
    min_width: int = 1920
    min_height: int = 1080
    min_aspect: float = 1.2
    max_aspect: float = 3.6
    max_file_bytes: int = 25 * 1024 * 1024
    allow_nsfw: bool = False

    # Housekeeping
    max_stored: int = 60
    keep_favorites: bool = True

    # Behaviour
    rotate_on_startup: bool = True
    start_with_session: bool = False
    show_notifications: bool = True

    # Reddit OAuth (recommended). Create an "installed app" at
    # https://www.reddit.com/prefs/apps to get a client id.
    reddit_client_id: str = ""
    reddit_username: str = ""

    def normalized_subreddits(self) -> List[str]:
        out = []
        for s in self.subreddits:
            name = s.strip().lstrip("/")
            if name.lower().startswith("r/"):
                name = name[2:]
            if name and name not in out:
                out.append(name)
        return out


def load() -> Settings:
    try:
        if paths.SETTINGS_FILE.exists():
            data = json.loads(paths.SETTINGS_FILE.read_text(encoding="utf-8"))
            # Only keep known fields so old/extra keys don't break construction.
            known = {f for f in Settings().__dict__}
            return Settings(**{k: v for k, v in data.items() if k in known})
    except Exception as ex:
        logger.error(f"Failed to load settings, using defaults: {ex}")
    s = Settings()
    save(s)
    return s


def save(settings: Settings) -> None:
    try:
        paths.SETTINGS_FILE.parent.mkdir(parents=True, exist_ok=True)
        tmp = paths.SETTINGS_FILE.with_suffix(".json.tmp")
        tmp.write_text(json.dumps(asdict(settings), indent=2), encoding="utf-8")
        tmp.replace(paths.SETTINGS_FILE)
    except Exception as ex:
        logger.error(f"Failed to save settings: {ex}")
