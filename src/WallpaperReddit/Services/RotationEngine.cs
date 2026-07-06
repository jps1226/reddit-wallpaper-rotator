using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperReddit.Services
{
    /// <summary>
    /// Orchestrates a single rotation: fetch → filter → rank → download → set → record → clean up.
    /// Selection is deterministic (highest score first, skipping already-seen items) so we avoid
    /// "weird random picks" while still cycling through fresh wallpapers.
    /// </summary>
    public class RotationEngine
    {
        private readonly RedditClient _reddit;
        private readonly ImageService _images;
        private readonly Library _library;
        private readonly Func<AppSettings> _settings;

        private readonly SemaphoreSlim _runLock = new(1, 1);

        public event Action<string> Status;
        public event Action<WallpaperItem> WallpaperChanged;

        public string CurrentItemId { get; private set; }

        public RotationEngine(RedditClient reddit, ImageService images, Library library, Func<AppSettings> settings)
        {
            _reddit = reddit;
            _images = images;
            _library = library;
            _settings = settings;
        }

        /// <summary>Runs one rotation. Returns the item that was applied, or null if none.</summary>
        public async Task<WallpaperItem> RotateAsync(CancellationToken ct = default)
        {
            if (!await _runLock.WaitAsync(0, ct).ConfigureAwait(false))
            {
                Report("A rotation is already in progress.");
                return null;
            }

            try
            {
                var s = _settings();
                Report("Fetching wallpapers from Reddit…");
                var candidates = await _reddit.FetchCandidatesAsync(ct).ConfigureAwait(false);

                var ranked = RankCandidates(candidates, s);
                if (ranked.Count == 0)
                {
                    Report("No suitable new wallpapers found. Try more subreddits or a wider time period.");
                    return null;
                }

                foreach (var cand in ranked)
                {
                    ct.ThrowIfCancellationRequested();

                    // Reuse an already-downloaded item without touching the network.
                    var existing = _library.GetById(cand.Id);
                    if (existing != null && existing.FileExists)
                    {
                        Apply(existing, s);
                        return existing;
                    }

                    Report($"Downloading: {Trim(cand.Title)}");
                    var result = await _images.DownloadAsync(cand.Id, cand.ImageUrl, s, ct).ConfigureAwait(false);
                    if (!result.Success)
                    {
                        Logger.Info($"Skipped '{Trim(cand.Title)}' — {result.Reason}.");
                        continue;
                    }

                    // Content-level dedupe: same image under a different post id.
                    if (_library.HasHash(result.Sha256) || _library.IsBlacklisted(null, result.Sha256, null))
                    {
                        Logger.Info($"Skipped '{Trim(cand.Title)}' — duplicate/blacklisted content.");
                        continue;
                    }

                    var item = new WallpaperItem
                    {
                        Id = cand.Id,
                        Title = cand.Title,
                        Subreddit = cand.Subreddit,
                        Author = cand.Author,
                        Permalink = cand.Permalink,
                        SourceUrl = cand.ImageUrl,
                        FilePath = result.FilePath,
                        ThumbnailPath = result.ThumbnailPath,
                        Width = result.Width,
                        Height = result.Height,
                        FileBytes = result.Bytes,
                        Sha256 = result.Sha256,
                        DownloadedAt = DateTime.Now
                    };

                    _library.AddOrUpdate(item);
                    Apply(item, s);

                    _library.Cleanup(s.MaxStoredWallpapers, s.KeepFavoritesForever, item.Id);
                    return item;
                }

                Report("Could not download a usable wallpaper this time.");
                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("Rotation failed.", ex);
                Report($"Rotation failed: {ex.Message}");
                return null;
            }
            finally
            {
                _runLock.Release();
            }
        }

        /// <summary>Re-apply a specific, already-downloaded wallpaper (e.g. from history).</summary>
        public bool ApplyExisting(WallpaperItem item)
        {
            if (item == null || !item.FileExists) return false;
            Apply(item, _settings());
            return true;
        }

        private void Apply(WallpaperItem item, AppSettings s)
        {
            if (WallpaperSetter.Set(item.FilePath))
            {
                CurrentItemId = item.Id;
                _library.MarkShown(item.Id, DateTime.Now);
                Report($"Wallpaper set: {Trim(item.Title)} (r/{item.Subreddit})");
                WallpaperChanged?.Invoke(item);
            }
            else
            {
                Report("Failed to apply wallpaper (see log).");
            }
        }

        /// <summary>
        /// Filters out unusable/blocked candidates and orders them so the best, freshest
        /// wallpaper comes first. Already-seen items sink to the bottom (least-recently-shown first)
        /// so we still rotate once the fresh pool is exhausted.
        /// </summary>
        private List<RedditCandidate> RankCandidates(List<RedditCandidate> candidates, AppSettings s)
        {
            var used = _library.UsedIds();

            var eligible = candidates.Where(c =>
            {
                if (string.IsNullOrEmpty(c.ImageUrl)) return false;
                if (c.IsNsfw && !s.AllowNsfw) return false;
                if (_library.IsBlacklisted(c.Id, null, c.ImageUrl)) return false;

                // If dimensions are known up-front, enforce the wallpaper gate early.
                if (c.Width > 0 && c.Height > 0)
                {
                    if (c.Width < s.MinWidth || c.Height < s.MinHeight) return false;
                    var aspect = (double)c.Width / c.Height;
                    if (aspect < s.MinAspectRatio || aspect > s.MaxAspectRatio) return false;
                }
                return true;
            }).ToList();

            var fresh = eligible.Where(c => !used.Contains(c.Id))
                                .OrderByDescending(c => c.Score)
                                .ToList();

            if (fresh.Count > 0) return fresh;

            // Everything's been seen — recycle, showing the least-recently-used first.
            return eligible
                .OrderBy(c =>
                {
                    var item = _library.GetById(c.Id);
                    return item?.LastShownAt ?? DateTime.MinValue;
                })
                .ThenByDescending(c => c.Score)
                .ToList();
        }

        private void Report(string msg)
        {
            Logger.Info(msg);
            Status?.Invoke(msg);
        }

        private static string Trim(string s)
            => string.IsNullOrEmpty(s) ? "" : (s.Length > 70 ? s[..70] + "…" : s);
    }
}
