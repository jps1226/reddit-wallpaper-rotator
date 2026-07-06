using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperReddit.Services
{
    /// <summary>A wallpaper candidate parsed from a Reddit listing (not yet downloaded).</summary>
    public class RedditCandidate
    {
        public string Id { get; set; }          // fullname e.g. t3_abc123
        public string Title { get; set; }
        public string Subreddit { get; set; }
        public string Author { get; set; }
        public string Permalink { get; set; }
        public string ImageUrl { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Score { get; set; }
        public bool IsNsfw { get; set; }
    }

    /// <summary>
    /// Reads Reddit listings. Uses application-only OAuth when a client id is configured
    /// (recommended: ~60 req/min), otherwise falls back to the public .json endpoints
    /// (~10 req/min, best-effort). Always sends a unique, descriptive User-Agent as Reddit requires.
    /// </summary>
    public class RedditClient
    {
        private const string AppId = "com.wallpaperreddit.app";
        private const string Version = "1.0.0";

        private readonly HttpClient _http;
        private readonly Func<AppSettings> _settings;
        private readonly string _deviceId;

        private string _token;
        private DateTime _tokenExpiresUtc = DateTime.MinValue;
        private readonly SemaphoreSlim _tokenLock = new(1, 1);

        public RedditClient(Func<AppSettings> settings)
        {
            _settings = settings;
            _deviceId = GetOrCreateDeviceId();

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            // Reddit's required "<platform>:<app id>:<version> (by /u/...)" form isn't a valid typed
            // product token, so add it raw without header validation.
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BuildUserAgent(settings()));
        }

        private static string BuildUserAgent(AppSettings s)
        {
            var user = string.IsNullOrWhiteSpace(s?.RedditUsername) ? "anonymous" : s.RedditUsername.Trim();
            // <platform>:<app id>:<version> (by /u/<user>)  — required Reddit format.
            return $"windows:{AppId}:v{Version} (by /u/{user})";
        }

        private static string GetOrCreateDeviceId()
        {
            // A stable per-install identifier for the installed_client grant.
            var path = System.IO.Path.Combine(AppPaths.Root, "device.id");
            try
            {
                if (System.IO.File.Exists(path)) return System.IO.File.ReadAllText(path).Trim();
                var id = Guid.NewGuid().ToString("N");
                System.IO.File.WriteAllText(path, id);
                return id;
            }
            catch
            {
                return Guid.NewGuid().ToString("N");
            }
        }

        private bool UseOAuth => !string.IsNullOrWhiteSpace(_settings()?.RedditClientId);

        /// <summary>Fetches and merges candidates from all configured subreddits.</summary>
        public async Task<List<RedditCandidate>> FetchCandidatesAsync(CancellationToken ct = default)
        {
            var settings = _settings();
            var results = new List<RedditCandidate>();
            var seen = new HashSet<string>();

            foreach (var sub in settings.Subreddits)
            {
                var name = sub.Trim().TrimStart('/');
                if (name.StartsWith("r/", StringComparison.OrdinalIgnoreCase)) name = name[2..];
                if (string.IsNullOrWhiteSpace(name)) continue;

                try
                {
                    var listing = await FetchSubredditAsync(name, settings, ct).ConfigureAwait(false);
                    foreach (var c in listing)
                        if (c.Id != null && seen.Add(c.Id))
                            results.Add(c);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.Warn($"Fetch failed for r/{name}: {ex.Message}");
                }
            }

            Logger.Info($"Fetched {results.Count} candidate posts from {settings.Subreddits.Count} subreddit(s).");
            return results;
        }

        private async Task<List<RedditCandidate>> FetchSubredditAsync(string sub, AppSettings s, CancellationToken ct)
        {
            var sort = s.Sort.ToString().ToLowerInvariant();
            var limit = Math.Clamp(s.FetchLimit, 5, 100);

            string url;
            if (UseOAuth)
            {
                await EnsureTokenAsync(ct).ConfigureAwait(false);
                url = $"https://oauth.reddit.com/r/{Uri.EscapeDataString(sub)}/{sort}?limit={limit}&raw_json=1";
            }
            else
            {
                url = $"https://www.reddit.com/r/{Uri.EscapeDataString(sub)}/{sort}.json?limit={limit}&raw_json=1";
            }

            if (s.Sort == RedditSort.Top)
                url += $"&t={s.TimePeriod.ToString().ToLowerInvariant()}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (UseOAuth && _token != null)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                Logger.Warn($"Rate limited by Reddit on r/{sub}. Consider adding an OAuth client id in Settings.");
                return new List<RedditCandidate>();
            }
            resp.EnsureSuccessStatusCode();

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return ParseListing(doc);
        }

        private static List<RedditCandidate> ParseListing(JsonDocument doc)
        {
            var list = new List<RedditCandidate>();
            if (!doc.RootElement.TryGetProperty("data", out var data)) return list;
            if (!data.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array) return list;

            foreach (var child in children.EnumerateArray())
            {
                if (!child.TryGetProperty("data", out var d)) continue;

                var candidate = ToCandidate(d);
                if (candidate != null) list.Add(candidate);
            }
            return list;
        }

        private static RedditCandidate ToCandidate(JsonElement d)
        {
            // Skip self/stickied posts outright.
            if (GetBool(d, "is_self") || GetBool(d, "stickied")) return null;
            if (GetBool(d, "is_gallery")) return null; // avoid multi-image dumps / odd picks

            var (imageUrl, width, height) = ResolveImage(d);
            if (string.IsNullOrEmpty(imageUrl)) return null;

            return new RedditCandidate
            {
                Id = GetString(d, "name") ?? ("t3_" + GetString(d, "id")),
                Title = GetString(d, "title") ?? "(untitled)",
                Subreddit = GetString(d, "subreddit"),
                Author = GetString(d, "author"),
                Permalink = "https://www.reddit.com" + (GetString(d, "permalink") ?? ""),
                ImageUrl = imageUrl,
                Width = width,
                Height = height,
                Score = GetInt(d, "score"),
                IsNsfw = GetBool(d, "over_18")
            };
        }

        /// <summary>
        /// Prefer the full-resolution preview source (gives reliable dimensions); otherwise
        /// fall back to a direct-image post url. Returns (url, width, height); dimensions 0 = unknown.
        /// </summary>
        private static (string url, int w, int h) ResolveImage(JsonElement d)
        {
            // 1) preview.images[0].source — carries true dimensions.
            if (d.TryGetProperty("preview", out var preview) &&
                preview.TryGetProperty("images", out var images) &&
                images.ValueKind == JsonValueKind.Array &&
                images.GetArrayLength() > 0)
            {
                var first = images[0];
                if (first.TryGetProperty("source", out var source) &&
                    source.TryGetProperty("url", out var urlEl))
                {
                    var url = System.Net.WebUtility.HtmlDecode(urlEl.GetString());
                    var w = source.TryGetProperty("width", out var wv) ? wv.GetInt32() : 0;
                    var h = source.TryGetProperty("height", out var hv) ? hv.GetInt32() : 0;
                    if (LooksLikeImageHost(url))
                        return (url, w, h);
                }
            }

            // 2) direct image url on the post itself.
            var direct = GetString(d, "url_overridden_by_dest") ?? GetString(d, "url");
            if (!string.IsNullOrEmpty(direct) && IsDirectImageUrl(direct))
                return (direct, 0, 0);

            return (null, 0, 0);
        }

        private static bool IsDirectImageUrl(string url)
        {
            var u = url.ToLowerInvariant();
            var q = u.IndexOf('?');
            if (q >= 0) u = u[..q];
            return u.EndsWith(".jpg") || u.EndsWith(".jpeg") || u.EndsWith(".png");
        }

        private static bool LooksLikeImageHost(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            var u = url.ToLowerInvariant();
            return u.Contains("preview.redd.it") || u.Contains("i.redd.it") ||
                   u.Contains("i.imgur.com") || IsDirectImageUrl(url);
        }

        // ---- OAuth (application-only, installed_client grant) --------------

        private async Task EnsureTokenAsync(CancellationToken ct)
        {
            if (_token != null && DateTime.UtcNow < _tokenExpiresUtc) return;

            await _tokenLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_token != null && DateTime.UtcNow < _tokenExpiresUtc) return;

                var clientId = _settings().RedditClientId.Trim();
                using var req = new HttpRequestMessage(HttpMethod.Post, "https://www.reddit.com/api/v1/access_token");

                // Installed apps have no secret: basic auth is clientId with an empty password.
                var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes(clientId + ":"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
                req.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "https://oauth.reddit.com/grants/installed_client"),
                    new KeyValuePair<string, string>("device_id", _deviceId)
                });

                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    throw new HttpRequestException($"Reddit token request failed ({(int)resp.StatusCode}): {body}");

                using var doc = JsonDocument.Parse(body);
                _token = doc.RootElement.GetProperty("access_token").GetString();
                var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
                _tokenExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn - 60);
                Logger.Info("Obtained Reddit application-only OAuth token.");
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        // ---- JSON helpers --------------------------------------------------

        private static string GetString(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static bool GetBool(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static int GetInt(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;
    }
}
