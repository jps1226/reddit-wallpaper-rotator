using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperReddit.Services
{
    /// <summary>
    /// The wallpaper history plus the blacklist, persisted as JSON. Small data set
    /// (hundreds of rows at most), so a flat file keeps the app dependency-free.
    /// All public methods are thread-safe.
    /// </summary>
    public class Library
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly object _gate = new();
        private List<WallpaperItem> _items = new();
        private List<BlacklistEntry> _blacklist = new();

        public event Action Changed;

        public void Load()
        {
            lock (_gate)
            {
                _items = ReadList<WallpaperItem>(AppPaths.LibraryFile);
                _blacklist = ReadList<BlacklistEntry>(AppPaths.BlacklistFile);
            }
        }

        private static List<T> ReadList<T>(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read {Path.GetFileName(path)}.", ex);
            }
            return new List<T>();
        }

        private void SaveItems() => AtomicWrite.WriteAllText(AppPaths.LibraryFile, JsonSerializer.Serialize(_items, Options));
        private void SaveBlacklist() => AtomicWrite.WriteAllText(AppPaths.BlacklistFile, JsonSerializer.Serialize(_blacklist, Options));

        // ---- Queries -------------------------------------------------------

        /// <summary>History newest first.</summary>
        public List<WallpaperItem> GetHistory()
        {
            lock (_gate) return _items.OrderByDescending(i => i.DownloadedAt).ToList();
        }

        public WallpaperItem GetById(string id)
        {
            lock (_gate) return _items.FirstOrDefault(i => i.Id == id);
        }

        public bool IsBlacklisted(string id, string sha256, string url)
        {
            lock (_gate)
            {
                return _blacklist.Any(b =>
                    (!string.IsNullOrEmpty(id) && b.Id == id) ||
                    (!string.IsNullOrEmpty(sha256) && b.Sha256 == sha256) ||
                    (!string.IsNullOrEmpty(url) && b.SourceUrl == url));
            }
        }

        public bool HasItem(string id)
        {
            lock (_gate) return _items.Any(i => i.Id == id);
        }

        public bool HasHash(string sha256)
        {
            lock (_gate) return !string.IsNullOrEmpty(sha256) && _items.Any(i => i.Sha256 == sha256);
        }

        public HashSet<string> UsedIds()
        {
            lock (_gate) return _items.Select(i => i.Id).ToHashSet();
        }

        public List<BlacklistEntry> GetBlacklist()
        {
            lock (_gate) return _blacklist.OrderByDescending(b => b.AddedAt).ToList();
        }

        // ---- Mutations -----------------------------------------------------

        public void AddOrUpdate(WallpaperItem item)
        {
            lock (_gate)
            {
                var existing = _items.FirstOrDefault(i => i.Id == item.Id);
                if (existing != null) _items.Remove(existing);
                _items.Add(item);
                SaveItems();
            }
            Changed?.Invoke();
        }

        public void MarkShown(string id, DateTime when)
        {
            lock (_gate)
            {
                var it = _items.FirstOrDefault(i => i.Id == id);
                if (it == null) return;
                it.LastShownAt = when;
                SaveItems();
            }
            Changed?.Invoke();
        }

        public void SetFavorite(string id, bool favorite)
        {
            lock (_gate)
            {
                var it = _items.FirstOrDefault(i => i.Id == id);
                if (it == null) return;
                it.IsFavorite = favorite;
                SaveItems();
            }
            Changed?.Invoke();
        }

        /// <summary>Remove a history item and (optionally) its files.</summary>
        public void Remove(string id, bool deleteFiles)
        {
            lock (_gate)
            {
                var it = _items.FirstOrDefault(i => i.Id == id);
                if (it == null) return;
                _items.Remove(it);
                if (deleteFiles) TryDeleteFiles(it);
                SaveItems();
            }
            Changed?.Invoke();
        }

        /// <summary>Blacklist an item so it never returns, and delete its files.</summary>
        public void Blacklist(WallpaperItem item)
        {
            lock (_gate)
            {
                if (!_blacklist.Any(b => b.Id == item.Id && !string.IsNullOrEmpty(item.Id)))
                {
                    _blacklist.Add(new BlacklistEntry
                    {
                        Id = item.Id,
                        Sha256 = item.Sha256,
                        SourceUrl = item.SourceUrl,
                        Title = item.Title,
                        AddedAt = DateTime.Now
                    });
                    SaveBlacklist();
                }

                var it = _items.FirstOrDefault(i => i.Id == item.Id);
                if (it != null)
                {
                    _items.Remove(it);
                    TryDeleteFiles(it);
                    SaveItems();
                }
            }
            Changed?.Invoke();
        }

        public void RemoveFromBlacklist(string id, string sha256, string url)
        {
            lock (_gate)
            {
                _blacklist.RemoveAll(b =>
                    (b.Id == id && !string.IsNullOrEmpty(id)) ||
                    (b.Sha256 == sha256 && !string.IsNullOrEmpty(sha256)) ||
                    (b.SourceUrl == url && !string.IsNullOrEmpty(url)));
                SaveBlacklist();
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Keep the newest <paramref name="maxNonFavorites"/> non-favorite wallpapers on disk;
        /// delete older ones (files + row). Favorites and <paramref name="protectId"/> are never removed.
        /// </summary>
        public int Cleanup(int maxNonFavorites, bool keepFavorites, string protectId)
        {
            var removed = 0;
            lock (_gate)
            {
                var nonFav = _items
                    .Where(i => (!i.IsFavorite || !keepFavorites) && i.Id != protectId)
                    .OrderByDescending(i => i.DownloadedAt)
                    .ToList();

                foreach (var it in nonFav.Skip(Math.Max(0, maxNonFavorites)))
                {
                    _items.Remove(it);
                    TryDeleteFiles(it);
                    removed++;
                }

                if (removed > 0) SaveItems();
            }
            if (removed > 0) Changed?.Invoke();
            return removed;
        }

        private static void TryDeleteFiles(WallpaperItem it)
        {
            foreach (var p in new[] { it.FilePath, it.ThumbnailPath })
            {
                try { if (!string.IsNullOrEmpty(p) && File.Exists(p)) File.Delete(p); }
                catch (Exception ex) { Logger.Warn($"Could not delete {p}: {ex.Message}"); }
            }
        }
    }
}
