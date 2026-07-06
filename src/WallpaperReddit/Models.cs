using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WallpaperReddit
{
    public enum RedditSort
    {
        Top,
        Hot,
        New,
        Rising
    }

    public enum RedditTimePeriod
    {
        Hour,
        Day,
        Week,
        Month,
        Year,
        All
    }

    /// <summary>User-configurable settings, persisted as JSON.</summary>
    public class AppSettings
    {
        public List<string> Subreddits { get; set; } = new()
        {
            "wallpaper",
            "wallpapers",
            "EarthPorn"
        };

        public int IntervalMinutes { get; set; } = 60;
        public RedditSort Sort { get; set; } = RedditSort.Top;
        public RedditTimePeriod TimePeriod { get; set; } = RedditTimePeriod.Week;

        /// <summary>How many posts to request per subreddit when building the candidate pool.</summary>
        public int FetchLimit { get; set; } = 50;

        // Quality gates so we grab real, wallpaper-sized landscape images and skip oddities.
        public int MinWidth { get; set; } = 1920;
        public int MinHeight { get; set; } = 1080;
        public double MinAspectRatio { get; set; } = 1.2;   // wider than this (landscape-ish)
        public double MaxAspectRatio { get; set; } = 3.6;   // ultrawide cap
        public long MaxFileBytes { get; set; } = 25 * 1024 * 1024; // skip absurdly large files
        public bool AllowNsfw { get; set; } = false;

        // Housekeeping
        public int MaxStoredWallpapers { get; set; } = 60;  // non-favorites kept on disk
        public bool KeepFavoritesForever { get; set; } = true;

        // Behaviour
        public bool RotateOnStartup { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public bool ShowNotifications { get; set; } = true;

        // Reddit OAuth (optional but strongly recommended for reliability).
        // Register an "installed app" at https://www.reddit.com/prefs/apps to get a client id.
        public string RedditClientId { get; set; } = "";
        public string RedditUsername { get; set; } = ""; // used only in the User-Agent contact string

        public bool IsValid() => Subreddits is { Count: > 0 } && IntervalMinutes >= 1;
    }

    /// <summary>A downloaded wallpaper plus its Reddit provenance and user flags.</summary>
    public class WallpaperItem
    {
        public string Id { get; set; }            // reddit fullname/id (e.g. t3_abc123) or hash fallback
        public string Title { get; set; }
        public string Subreddit { get; set; }
        public string Author { get; set; }
        public string Permalink { get; set; }     // reddit thread url
        public string SourceUrl { get; set; }     // direct image url that was downloaded
        public string FilePath { get; set; }      // local full-size image
        public string ThumbnailPath { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long FileBytes { get; set; }
        public string Sha256 { get; set; }        // dedupe + blacklist matching
        public DateTime DownloadedAt { get; set; }
        public DateTime? LastShownAt { get; set; }
        public bool IsFavorite { get; set; }

        [JsonIgnore]
        public bool FileExists => !string.IsNullOrEmpty(FilePath) && System.IO.File.Exists(FilePath);
    }

    /// <summary>An entry we never want to download or show again.</summary>
    public class BlacklistEntry
    {
        public string Id { get; set; }        // reddit id if known
        public string Sha256 { get; set; }    // content hash if known
        public string SourceUrl { get; set; }
        public string Title { get; set; }
        public DateTime AddedAt { get; set; }
    }
}
