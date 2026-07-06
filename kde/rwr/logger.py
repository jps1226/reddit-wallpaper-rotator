"""Tiny logger: appends to the log file and fans out lines to UI listeners."""
from __future__ import annotations

import datetime
from typing import Callable, List

from . import paths

_listeners: List[Callable[[str], None]] = []


def add_listener(fn: Callable[[str], None]) -> None:
    _listeners.append(fn)


def remove_listener(fn: Callable[[str], None]) -> None:
    if fn in _listeners:
        _listeners.remove(fn)


def _write(level: str, message: str) -> None:
    line = f"{datetime.datetime.now():%Y-%m-%d %H:%M:%S} [{level}] {message}"
    try:
        paths.LOG_FILE.parent.mkdir(parents=True, exist_ok=True)
        # Roll once at ~1 MB.
        if paths.LOG_FILE.exists() and paths.LOG_FILE.stat().st_size > 1_000_000:
            paths.LOG_FILE.replace(paths.LOG_FILE.with_suffix(".log.1"))
        with paths.LOG_FILE.open("a", encoding="utf-8") as fh:
            fh.write(line + "\n")
    except Exception:
        pass  # logging must never crash the app
    for fn in list(_listeners):
        try:
            fn(line)
        except Exception:
            pass


def info(msg: str) -> None:
    _write("INFO", msg)


def warn(msg: str) -> None:
    _write("WARN", msg)


def error(msg: str) -> None:
    _write("ERROR", msg)
