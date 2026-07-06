"""Application controller: services, tray icon, scheduling and the Qt event loop."""
from __future__ import annotations

import sys
import threading
import time
from pathlib import Path
from typing import Optional

from PySide6.QtCore import QObject, QTimer, Signal
from PySide6.QtGui import QGuiApplication, QIcon
from PySide6.QtWidgets import QApplication, QMenu, QSystemTrayIcon

from . import config, logger, paths
from .config import Settings
from .library import Library, WallpaperItem
from .reddit import RedditClient
from .rotation import RotationEngine
from .ui import MainWindow


class Bridge(QObject):
    """Marshals worker-thread callbacks onto the Qt (GUI) thread via signals."""
    status = Signal(str)
    changed = Signal()
    log_line = Signal(str)
    rotation_done = Signal(object)


class Controller:
    def __init__(self, app: QApplication):
        self.app = app
        paths.ensure_dirs()
        self.settings: Settings = config.load()
        self.bridge = Bridge()

        self.library = Library()
        self.library.load()
        self.library.add_changed_listener(self.bridge.changed.emit)

        self.reddit = RedditClient(lambda: self.settings)
        self.engine = RotationEngine(self.reddit, self.library,
                                     lambda: self.settings, self._screen_size)
        self.engine.status_cb = self.bridge.status.emit
        self.engine.wallpaper_changed_cb = self.bridge.rotation_done.emit

        logger.add_listener(self.bridge.log_line.emit)
        self.bridge.rotation_done.connect(self._on_rotation_done)

        self._busy = False
        self._next_run_ts = time.time() + self.settings.interval_minutes * 60
        self.window: Optional[MainWindow] = None

        self._build_tray()
        self._apply_autostart(self.settings.start_with_session)

        self.timer = QTimer()
        self.timer.setInterval(30_000)
        self.timer.timeout.connect(self._on_tick)
        self.timer.start()

        if self.settings.rotate_on_startup:
            self.rotate_now()

    # ---- tray --------------------------------------------------------
    def _app_icon(self) -> QIcon:
        bundled = Path(__file__).with_name("icon.png")
        if bundled.is_file():
            return QIcon(str(bundled))
        return QIcon.fromTheme("preferences-desktop-wallpaper")

    def _build_tray(self):
        self.tray = QSystemTrayIcon(self._app_icon())
        self.tray.setToolTip("Reddit Wallpaper Rotator")
        menu = QMenu()
        menu.addAction("Open", self.show_window)
        menu.addAction("Next wallpaper now", self.rotate_now)
        menu.addSeparator()
        menu.addAction("Settings…", lambda: self.show_window(open_settings=True))
        menu.addSeparator()
        menu.addAction("Quit", self.quit)
        self.tray.setContextMenu(menu)
        self.tray.activated.connect(self._on_tray_activated)
        self.tray.show()

    def _on_tray_activated(self, reason):
        if reason == QSystemTrayIcon.ActivationReason.Trigger:
            self.show_window()

    def show_window(self, open_settings: bool = False):
        if self.window is None:
            self.window = MainWindow(self)
        self.window.show()
        self.window.raise_()
        self.window.activateWindow()
        if open_settings:
            self.window.open_settings()

    # ---- scheduling / rotation ---------------------------------------
    def _screen_size(self):
        screen = QGuiApplication.primaryScreen()
        if screen:
            g = screen.geometry()
            return g.width(), g.height()
        return 1920, 1080

    def _on_tick(self):
        if self._busy:
            return
        if time.time() >= self._next_run_ts:
            self.rotate_now()

    def rotate_now(self):
        if self._busy:
            return
        self._busy = True
        threading.Thread(target=self._rotate_worker, daemon=True).start()

    def _rotate_worker(self):
        try:
            self.engine.rotate()
        finally:
            self._busy = False
            self._next_run_ts = time.time() + max(1, self.settings.interval_minutes) * 60
            self.bridge.status.emit(f"Next change at {self.next_run_local():%H:%M}.")

    def _on_rotation_done(self, item: WallpaperItem):
        if self.settings.show_notifications and item:
            self.tray.showMessage("Wallpaper changed",
                                  f"{item.title}\nr/{item.subreddit}",
                                  self._app_icon(), 4000)

    def next_run_local(self):
        import datetime
        return datetime.datetime.fromtimestamp(self._next_run_ts)

    # ---- settings ----------------------------------------------------
    def apply_settings(self, updated: Settings):
        self.settings = updated
        config.save(self.settings)
        self._apply_autostart(self.settings.start_with_session)
        self._next_run_ts = time.time() + max(1, self.settings.interval_minutes) * 60

    def _apply_autostart(self, enable: bool):
        try:
            if enable:
                paths.AUTOSTART_DIR.mkdir(parents=True, exist_ok=True)
                exec_cmd = self._launch_command()
                paths.AUTOSTART_FILE.write_text(
                    "[Desktop Entry]\n"
                    "Type=Application\n"
                    "Name=Reddit Wallpaper Rotator\n"
                    f"Exec={exec_cmd}\n"
                    "Icon=preferences-desktop-wallpaper\n"
                    "X-KDE-autostart-after=panel\n"
                    "Terminal=false\n",
                    encoding="utf-8")
            elif paths.AUTOSTART_FILE.exists():
                paths.AUTOSTART_FILE.unlink()
        except Exception as ex:
            logger.warn(f"Could not update autostart entry: {ex}")

    @staticmethod
    def _launch_command() -> str:
        # Prefer an installed launcher on PATH; otherwise re-run this module.
        import shutil
        launcher = shutil.which("reddit-wallpaper-rotator")
        if launcher:
            return f"{launcher} --minimized"
        return f"{sys.executable} -m rwr --minimized"

    def quit(self):
        self.timer.stop()
        self.tray.hide()
        self.app.quit()


def main():
    logger.info("Reddit Wallpaper Rotator (KDE) starting.")
    app = QApplication(sys.argv)
    app.setApplicationName("Reddit Wallpaper Rotator")
    app.setQuitOnLastWindowClosed(False)  # live in the tray

    if not QSystemTrayIcon.isSystemTrayAvailable():
        logger.warn("No system tray available; showing the window instead.")

    controller = Controller(app)

    minimized = any(a in ("--minimized", "-m") for a in sys.argv[1:])
    if not minimized:
        controller.show_window()

    sys.exit(app.exec())


if __name__ == "__main__":
    main()
