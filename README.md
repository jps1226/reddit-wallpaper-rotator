# Reddit Wallpaper Rotator

A lightweight Windows desktop app that downloads wallpapers from your chosen subreddits and rotates your desktop background on a schedule. It lives quietly in the system tray, keeps a browsable history, and lets you favorite, blacklist, and re-apply images.

> **Using KDE Plasma / Linux?** There's a native PySide6 (Qt) port in [`kde/`](kde/README.md) with the same features.

## Features

- **Pick your sources** — any list of subreddits (e.g. `wallpaper`, `wallpapers`, `EarthPorn`).
- **Schedule** — change the wallpaper every *N* minutes.
- **Reddit time period** — for `Top` sort, limit to Hour / Day / Week / Month / Year / All.
- **Quality first, not random** — only real, wallpaper-sized landscape images pass the gate (min resolution + aspect-ratio checks), and selection is highest-score-first while skipping images you've already seen, so you don't get weird picks.
- **Tidy storage** — keeps the newest *N* non-favorite wallpapers and cleans up the rest automatically; favorites are protected.
- **Simple UI** — history grid with thumbnails, mark favorites (★), blacklist images so they never return, and a one-click *Rebuild thumbnails*.
- **Stable & unobtrusive** — single instance, runs from the tray, closing the window hides it instead of quitting, and everything is logged.
- **Optional Reddit OAuth** — add a free "installed app" client id for reliable, higher-rate access (see below).

## Requirements

- Windows 10 or 11 (x64).
- Nothing else for end users — the installer ships a **self-contained** build (no .NET runtime to install).
- To *build from source*: the **.NET 8 SDK**. To build the *installer*: **Inno Setup 6**.

  ```powershell
  winget install Microsoft.DotNet.SDK.8
  winget install JRSoftware.InnoSetup
  ```

## Build

```powershell
# From the repo root:
./build.ps1              # produces publish\WallpaperReddit.exe (self-contained, single file)
./build.ps1 -Installer   # also builds installer\Output\RedditWallpaperRotator-Setup-1.0.0.exe
```

Or open `WallpaperReddit.sln` in Visual Studio 2022 and run.

## Install & run

1. Run the generated `RedditWallpaperRotator-Setup-1.0.0.exe`.
2. Launch **Reddit Wallpaper Rotator**. It appears in the system tray.
3. Double-click the tray icon to open the window; use **Settings…** to choose subreddits, interval, and quality gates.

## Reddit access (recommended)

Reddit heavily throttles anonymous requests and can block them outright. The app works anonymously for light use, but for reliability create a free app:

1. Go to <https://www.reddit.com/prefs/apps> → **create another app…**
2. Choose **installed app**. Set the redirect URI to `http://localhost` (unused, but required).
3. Copy the **client id** (the string under the app name).
4. In **Settings → Reddit account**, paste the client id and your Reddit username.

The app uses the OAuth *application-only* (`installed_client`) flow — read-only, no password, no login. A unique descriptive `User-Agent` is always sent, as Reddit requires.

## Where things live

App data lives per-user under `%LOCALAPPDATA%\WallpaperReddit`:

| File / folder      | Purpose                                   |
|--------------------|-------------------------------------------|
| `settings.json`    | Your settings                             |
| `library.json`     | Wallpaper history + favorite flags        |
| `blacklist.json`   | Images you never want again               |
| `Thumbnails\`      | Generated thumbnails                      |
| `app.log`          | Activity / diagnostics                    |

Downloaded wallpapers are stored separately, under **`%USERPROFILE%\Pictures\Reddit Wallpaper Rotator\`**. This is deliberate: the Windows desktop/shell wallpaper loader can't read images under `AppData` on some setups (e.g. shells modified by ExplorerPatcher), which makes the desktop go black — Pictures is a media folder the shell can always read. Images are also re-encoded to a screen-sized baseline JPEG before being applied, so oversized or oddly-encoded source images can't break rendering.

Uninstalling removes the `%LOCALAPPDATA%\WallpaperReddit` folder; your downloaded wallpapers in Pictures are left in place.

## Project layout

```
WallpaperReddit.sln
src/WallpaperReddit/
  Program.cs                 entry point (single instance, high-DPI)
  Models.cs                  settings + data models
  App/TrayContext.cs         tray icon, scheduler, service wiring
  Services/
    RedditClient.cs          listings via OAuth or public JSON
    ImageService.cs          download, validate, hash, thumbnails
    WallpaperSetter.cs       SystemParametersInfo (set + fill style)
    RotationEngine.cs        fetch → rank → download → set → clean up
    Library.cs               history / favorites / blacklist store (+ cleanup)
    SettingsStore.cs         JSON settings
    StartupManager.cs        run-at-login registry entry
    Logger.cs / AppPaths.cs  logging + paths
  UI/MainForm.cs             history, blacklist, activity
  UI/SettingsForm.cs         settings dialog
installer/setup.iss          Inno Setup installer
build.ps1                    build + package script
```
```
