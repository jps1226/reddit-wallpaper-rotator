# Reddit Wallpaper Rotator — KDE (Plasma) edition

A lightweight KDE Plasma tray app that downloads wallpapers from your chosen subreddits and rotates your desktop background on a schedule. It's the Linux/KDE counterpart of the Windows app in the parent folder, written in Python + PySide6 (Qt 6).

## Features

- Pick any subreddits, set the interval, and (for `Top`) limit to Hour / Day / Week / Month / Year / All.
- Quality-first selection: only real, wallpaper-sized landscape images pass the gate; picks are highest-score-first and skip images you've already seen — no random/odd picks.
- Automatic cleanup of old wallpapers (favorites protected).
- Tray app with a history grid, favorites (★), blacklist, and a *Rebuild thumbnails* button.
- Sets the wallpaper via `plasma-apply-wallpaperimage`, with a `qdbus`/plasmashell fallback.
- Optional Reddit OAuth for reliable, higher-rate access.

## Requirements

- **KDE Plasma 5.18+ or Plasma 6** (for `plasma-apply-wallpaperimage`, shipped with `plasma-workspace`).
- **Python 3.9+**.
- Dependencies (installed automatically): `PySide6`, `Pillow`, `requests`.

## Install

```bash
cd kde
./install.sh
```

This creates an isolated virtualenv under `~/.local/share/reddit-wallpaper-rotator/venv`, adds a launcher at `~/.local/bin/reddit-wallpaper-rotator`, and an application-menu entry. Then launch **Reddit Wallpaper Rotator** from your app menu, or run the launcher.

### Run without installing

```bash
cd kde
python3 -m venv .venv && . .venv/bin/activate
pip install -r requirements.txt
python -m rwr           # add --minimized to start hidden in the tray
```

## Reddit access (recommended)

Reddit throttles/blocks anonymous access. Create a free app for reliable fetching:

1. Go to <https://www.reddit.com/prefs/apps> → **create another app…**
2. Choose **installed app**, set redirect URI to `http://localhost`.
3. Copy the **client id** (the string under the app name).
4. In the app: **Settings → Reddit account**, paste the client id + your username, **Save**.

The app uses the OAuth application-only (`installed_client`) flow — read-only, no login.

## Where things live (XDG)

| Location | Purpose |
|---|---|
| `$XDG_CONFIG_HOME/reddit-wallpaper-rotator/settings.json` | Settings |
| `$XDG_DATA_HOME/reddit-wallpaper-rotator/library.json` | History + favorites |
| `$XDG_DATA_HOME/reddit-wallpaper-rotator/blacklist.json` | Blacklist |
| `$XDG_DATA_HOME/reddit-wallpaper-rotator/thumbnails/` | Thumbnails |
| `<Pictures>/Reddit Wallpaper Rotator/` | Downloaded wallpapers |
| `$XDG_DATA_HOME/reddit-wallpaper-rotator/app.log` | Activity log |

"Start automatically when I log in" writes `~/.config/autostart/reddit-wallpaper-rotator.desktop`.

## Layout

```
kde/
  pyproject.toml          packaging + console entry point
  requirements.txt
  install.sh              venv install + menu entry
  reddit-wallpaper-rotator.desktop
  rwr/
    __main__.py           `python -m rwr`
    app.py                controller, tray icon, scheduler, Qt event loop
    ui.py                 main window (history/blacklist/activity) + settings dialog
    reddit.py             listings via OAuth or public JSON
    images.py             download, validate, downscale, thumbnails (Pillow)
    wallpaper.py          plasma-apply-wallpaperimage / qdbus
    library.py            history / favorites / blacklist store (+ cleanup)
    config.py             settings
    logger.py, paths.py   logging + XDG paths
    icon.png
```

## Verification status

The cross-platform core is tested: live Reddit OAuth fetch, the listing parser, the
image download/validate/downscale/thumbnail pipeline, and the settings/library logic.
The Qt UI and the Plasma wallpaper call (`plasma-apply-wallpaperimage` / `qdbus`) were
written to KDE docs but need a real Plasma session to exercise — please report any issues.
