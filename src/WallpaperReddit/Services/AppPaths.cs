using System;
using System.IO;

namespace WallpaperReddit.Services
{
    /// <summary>
    /// Centralises all on-disk locations. Everything lives under
    /// %LOCALAPPDATA%\WallpaperReddit so per-user install and uninstall stay tidy.
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

            WallpapersDir = Path.Combine(Root, "Wallpapers");
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
