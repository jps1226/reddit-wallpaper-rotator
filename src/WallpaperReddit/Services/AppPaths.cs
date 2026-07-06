using System;
using System.IO;

namespace WallpaperReddit.Services
{
    /// <summary>
    /// Centralises all on-disk locations. App data (settings, library, thumbnails, log)
    /// lives under %LOCALAPPDATA%\WallpaperReddit. The full-size wallpapers that get handed
    /// to Windows live under the user's Pictures folder instead: the Windows desktop/shell
    /// wallpaper loader cannot read files under %LOCALAPPDATA% on some setups (e.g. shells
    /// modified by ExplorerPatcher), which makes the desktop go black. Pictures is a media
    /// known-folder the shell can always read.
    /// </summary>
    public static class AppPaths
    {
        public static string Root { get; }
        public static string WallpapersDir { get; }
        public static string ThumbnailsDir { get; }
        public static string SettingsFile { get; }
        public static string LibraryFile { get; }
        public static string BlacklistFile { get; }
        public static string LogFile { get; }

        static AppPaths()
        {
            Root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WallpaperReddit");

            // Full-size wallpapers must be somewhere the shell can read (Pictures/Downloads),
            // NOT under AppData. Fall back to the app root if Pictures can't be resolved.
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            WallpapersDir = string.IsNullOrEmpty(pictures)
                ? Path.Combine(Root, "Wallpapers")
                : Path.Combine(pictures, "Reddit Wallpaper Rotator");

            ThumbnailsDir = Path.Combine(Root, "Thumbnails");
            SettingsFile = Path.Combine(Root, "settings.json");
            LibraryFile = Path.Combine(Root, "library.json");
            BlacklistFile = Path.Combine(Root, "blacklist.json");
            LogFile = Path.Combine(Root, "app.log");

            Directory.CreateDirectory(WallpapersDir);
            Directory.CreateDirectory(ThumbnailsDir);
        }
    }
}
