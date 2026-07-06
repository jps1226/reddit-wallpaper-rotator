"""Qt widgets: the main window (history / blacklist / activity) and settings dialog."""
from __future__ import annotations

import datetime
import subprocess
from typing import Optional

from PySide6.QtCore import QSize, Qt
from PySide6.QtGui import QAction, QIcon, QPixmap
from PySide6.QtWidgets import (
    QCheckBox, QComboBox, QDialog, QDialogButtonBox, QFormLayout, QGroupBox,
    QHBoxLayout, QLabel, QListWidget, QListWidgetItem, QMainWindow, QMenu,
    QMessageBox, QPlainTextEdit, QPushButton, QSpinBox, QTabWidget, QVBoxLayout,
    QWidget,
)

from . import config, images, logger, paths
from .config import PERIODS, SORTS, Settings


class SettingsDialog(QDialog):
    def __init__(self, current: Settings, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Settings")
        self.setMinimumWidth(460)
        self._result: Optional[Settings] = None
        self._build(current)

    def _build(self, s: Settings):
        layout = QVBoxLayout(self)

        # Sources
        src = QGroupBox("Sources")
        src_form = QFormLayout(src)
        self.subreddits = QPlainTextEdit("\n".join(s.subreddits))
        self.subreddits.setPlaceholderText("One subreddit per line, e.g. wallpaper")
        self.subreddits.setFixedHeight(96)
        src_form.addRow("Subreddits", self.subreddits)
        self.sort = QComboBox(); self.sort.addItems(SORTS); self.sort.setCurrentText(s.sort)
        src_form.addRow("Sort by", self.sort)
        self.period = QComboBox(); self.period.addItems(PERIODS); self.period.setCurrentText(s.time_period)
        src_form.addRow("Time period (Top only)", self.period)
        self.sort.currentTextChanged.connect(
            lambda t: self.period.setEnabled(t == "top"))
        self.period.setEnabled(s.sort == "top")
        layout.addWidget(src)

        # Rotation
        rot = QGroupBox("Rotation")
        rot_form = QFormLayout(rot)
        self.interval = _spin(1, 10080, s.interval_minutes)
        rot_form.addRow("Change every (minutes)", self.interval)
        self.rotate_on_startup = QCheckBox("Change wallpaper when the app starts")
        self.rotate_on_startup.setChecked(s.rotate_on_startup)
        rot_form.addRow("", self.rotate_on_startup)
        layout.addWidget(rot)

        # Quality
        q = QGroupBox("Image quality")
        q_form = QFormLayout(q)
        self.min_width = _spin(640, 16000, s.min_width)
        self.min_height = _spin(480, 16000, s.min_height)
        q_form.addRow("Minimum width (px)", self.min_width)
        q_form.addRow("Minimum height (px)", self.min_height)
        self.allow_nsfw = QCheckBox("Allow NSFW-flagged posts")
        self.allow_nsfw.setChecked(s.allow_nsfw)
        q_form.addRow("", self.allow_nsfw)
        layout.addWidget(q)

        # Storage
        st = QGroupBox("Storage")
        st_form = QFormLayout(st)
        self.max_stored = _spin(5, 1000, s.max_stored)
        st_form.addRow("Keep at most (non-favorites)", self.max_stored)
        self.keep_favorites = QCheckBox("Never delete favorites")
        self.keep_favorites.setChecked(s.keep_favorites)
        st_form.addRow("", self.keep_favorites)
        layout.addWidget(st)

        # System
        sysg = QGroupBox("System")
        sys_form = QFormLayout(sysg)
        self.start_with_session = QCheckBox("Start automatically when I log in")
        self.start_with_session.setChecked(s.start_with_session)
        sys_form.addRow("", self.start_with_session)
        self.notifications = QCheckBox("Notify when the wallpaper changes")
        self.notifications.setChecked(s.show_notifications)
        sys_form.addRow("", self.notifications)
        layout.addWidget(sysg)

        # Reddit
        rd = QGroupBox("Reddit account (recommended)")
        rd_form = QFormLayout(rd)
        from PySide6.QtWidgets import QLineEdit
        self.client_id = QLineEdit(s.reddit_client_id)
        self.username = QLineEdit(s.reddit_username)
        rd_form.addRow("OAuth client id", self.client_id)
        rd_form.addRow("Reddit username", self.username)
        help_lbl = QLabel(
            'Reddit throttles anonymous access. Create a free "installed app" at '
            '<a href="https://www.reddit.com/prefs/apps">reddit.com/prefs/apps</a> '
            'to get a client id.')
        help_lbl.setOpenExternalLinks(True)
        help_lbl.setWordWrap(True)
        rd_form.addRow("", help_lbl)
        layout.addWidget(rd)

        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Save | QDialogButtonBox.StandardButton.Cancel)
        buttons.accepted.connect(self._on_save)
        buttons.rejected.connect(self.reject)
        layout.addWidget(buttons)

    def _on_save(self):
        subs = []
        for line in self.subreddits.toPlainText().replace(",", "\n").splitlines():
            name = line.strip().lstrip("/")
            if name.lower().startswith("r/"):
                name = name[2:]
            if name and name not in subs:
                subs.append(name)
        if not subs:
            QMessageBox.warning(self, "Settings", "Please enter at least one subreddit.")
            return
        s = Settings(
            subreddits=subs,
            interval_minutes=self.interval.value(),
            sort=self.sort.currentText(),
            time_period=self.period.currentText(),
            min_width=self.min_width.value(),
            min_height=self.min_height.value(),
            max_stored=self.max_stored.value(),
            keep_favorites=self.keep_favorites.isChecked(),
            rotate_on_startup=self.rotate_on_startup.isChecked(),
            start_with_session=self.start_with_session.isChecked(),
            show_notifications=self.notifications.isChecked(),
            allow_nsfw=self.allow_nsfw.isChecked(),
            reddit_client_id=self.client_id.text().strip(),
            reddit_username=self.username.text().strip(),
        )
        self._result = s
        self.accept()

    def result_settings(self) -> Optional[Settings]:
        return self._result


def _spin(lo, hi, val) -> QSpinBox:
    sp = QSpinBox()
    sp.setRange(lo, hi)
    sp.setValue(max(lo, min(hi, val)))
    return sp


class MainWindow(QMainWindow):
    """History grid, blacklist and activity log. `controller` provides the services."""

    def __init__(self, controller):
        super().__init__()
        self.controller = controller
        self.setWindowTitle("Reddit Wallpaper Rotator")
        self.resize(980, 660)
        self._favorites_only = False
        self._build()

        controller.bridge.status.connect(self._on_status)
        controller.bridge.changed.connect(self._refresh_all)
        controller.bridge.log_line.connect(self._append_log)

        self._refresh_all()

    # ---- construction ------------------------------------------------
    def _build(self):
        tb = self.addToolBar("Main")
        tb.setMovable(False)
        act_next = QAction("Next wallpaper now", self)
        act_next.triggered.connect(self.controller.rotate_now)
        tb.addAction(act_next)
        tb.addSeparator()
        act_rebuild = QAction("Rebuild thumbnails", self)
        act_rebuild.triggered.connect(self._rebuild_thumbnails)
        tb.addAction(act_rebuild)
        tb.addSeparator()
        self.act_fav = QAction("Favorites only", self, checkable=True)
        self.act_fav.toggled.connect(self._toggle_favorites_only)
        tb.addAction(self.act_fav)
        tb.addSeparator()
        act_settings = QAction("Settings…", self)
        act_settings.triggered.connect(self.open_settings)
        tb.addAction(act_settings)

        self.tabs = QTabWidget()

        self.history = QListWidget()
        self.history.setViewMode(QListWidget.ViewMode.IconMode)
        self.history.setIconSize(QSize(240, 135))
        self.history.setResizeMode(QListWidget.ResizeMode.Adjust)
        self.history.setMovement(QListWidget.Movement.Static)
        self.history.setSpacing(8)
        self.history.setWordWrap(True)
        self.history.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self.history.customContextMenuRequested.connect(self._history_menu)
        self.history.itemDoubleClicked.connect(lambda _it: self._set_selected_wallpaper())
        self.tabs.addTab(self.history, "History")

        self.blacklist = QListWidget()
        self.blacklist.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self.blacklist.customContextMenuRequested.connect(self._blacklist_menu)
        self.tabs.addTab(self.blacklist, "Blacklist")

        self.log = QPlainTextEdit()
        self.log.setReadOnly(True)
        self.tabs.addTab(self.log, "Activity")

        self.setCentralWidget(self.tabs)
        self.status = self.statusBar()

    # ---- history -----------------------------------------------------
    def _refresh_all(self):
        self._refresh_history()
        self._refresh_blacklist()
        self._update_next_run()

    def _refresh_history(self):
        self.history.clear()
        items = self.controller.library.history()
        if self._favorites_only:
            items = [i for i in items if i.favorite]
        for it in items:
            label = ("★ " if it.favorite else "") + _ellipsize(it.title, 40)
            lw = QListWidgetItem(label)
            lw.setData(Qt.ItemDataRole.UserRole, it.id)
            lw.setToolTip(f"{it.title}\nr/{it.subreddit} · {it.width}x{it.height}")
            if it.thumb_path:
                pix = QPixmap(it.thumb_path)
                if not pix.isNull():
                    lw.setIcon(QIcon(pix))
            self.history.addItem(lw)

    def _selected_item(self):
        cur = self.history.currentItem()
        if not cur:
            return None
        return self.controller.library.get(cur.data(Qt.ItemDataRole.UserRole))

    def _history_menu(self, pos):
        it = self._selected_item()
        if not it:
            return
        menu = QMenu(self)
        menu.addAction("Set as wallpaper", self._set_selected_wallpaper)
        menu.addAction("Unfavorite" if it.favorite else "Favorite",
                       lambda: self.controller.library.set_favorite(it.id, not it.favorite))
        menu.addAction("Open on Reddit", lambda: _open_url(it.permalink))
        menu.addSeparator()
        menu.addAction("Blacklist (never show again)", lambda: self._blacklist_item(it))
        menu.addAction("Remove from history", lambda: self.controller.library.remove(it.id, True))
        menu.exec(self.history.mapToGlobal(pos))

    def _set_selected_wallpaper(self):
        it = self._selected_item()
        if not it:
            return
        if not self.controller.engine.apply_existing(it):
            QMessageBox.information(self, "Set wallpaper",
                                    "That image file is missing. It may have been cleaned up.")

    def _blacklist_item(self, it):
        if QMessageBox.question(self, "Blacklist",
                                f"Blacklist this image so it never appears again?\n\n{it.title}") \
                == QMessageBox.StandardButton.Yes:
            self.controller.library.blacklist(it)

    # ---- blacklist ---------------------------------------------------
    def _refresh_blacklist(self):
        self.blacklist.clear()
        for b in self.controller.library.blacklist_entries():
            when = datetime.datetime.fromtimestamp(b.added_at).strftime("%Y-%m-%d %H:%M") if b.added_at else ""
            lw = QListWidgetItem(f"{_ellipsize(b.title or b.source_url or b.id, 90)}    {when}")
            lw.setData(Qt.ItemDataRole.UserRole, b)
            self.blacklist.addItem(lw)

    def _blacklist_menu(self, pos):
        cur = self.blacklist.currentItem()
        if not cur:
            return
        b = cur.data(Qt.ItemDataRole.UserRole)
        menu = QMenu(self)
        menu.addAction("Remove from blacklist",
                       lambda: self.controller.library.remove_from_blacklist(
                           item_id=b.id, sha=b.sha256, url=b.source_url))
        menu.exec(self.blacklist.mapToGlobal(pos))

    # ---- thumbnails --------------------------------------------------
    def _rebuild_thumbnails(self):
        done = failed = 0
        for it in self.controller.library.history():
            if not it.file_exists():
                continue
            try:
                thumb = str(paths.THUMBNAILS_DIR / (images._safe_name(it.id) + ".jpg"))
                images.rebuild_thumbnail(it.file_path, thumb)
                if it.thumb_path != thumb:
                    it.thumb_path = thumb
                    self.controller.library.add_or_update(it)
                done += 1
            except Exception as ex:
                failed += 1
                logger.warn(f"Rebuild thumb failed for {it.id}: {ex}")
        self._refresh_history()
        QMessageBox.information(self, "Rebuild thumbnails",
                                f"Rebuilt {done} thumbnail(s)." + (f" {failed} failed." if failed else ""))

    # ---- settings / events -------------------------------------------
    def open_settings(self):
        dlg = SettingsDialog(self.controller.settings, self)
        if dlg.exec() == QDialog.DialogCode.Accepted and dlg.result_settings():
            self.controller.apply_settings(dlg.result_settings())
            self.status.showMessage("Settings saved.", 4000)
            self._update_next_run()

    def _toggle_favorites_only(self, checked):
        self._favorites_only = checked
        self._refresh_history()

    def _on_status(self, msg):
        self.status.showMessage(msg)
        self._update_next_run()

    def _append_log(self, line):
        self.log.appendPlainText(line)

    def _update_next_run(self):
        nxt = self.controller.next_run_local()
        self.setWindowTitle("Reddit Wallpaper Rotator" +
                            (f"  —  next change {nxt:%H:%M}" if nxt else ""))

    def closeEvent(self, event):
        # Hide to tray instead of quitting so rotation keeps running.
        event.ignore()
        self.hide()


def _ellipsize(s: str, n: int) -> str:
    if not s:
        return ""
    return s if len(s) <= n else s[:n - 1] + "…"


def _open_url(url: str):
    if not url:
        return
    try:
        subprocess.Popen(["xdg-open", url])
    except Exception as ex:
        logger.warn(f"xdg-open failed: {ex}")
