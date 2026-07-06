using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperReddit.Services
{
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string Reason { get; set; }
        public string FilePath { get; set; }
        public string ThumbnailPath { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long Bytes { get; set; }
        public string Sha256 { get; set; }
    }

    /// <summary>
    /// Downloads images, validates that they are real, wallpaper-sized landscape pictures,
    /// hashes them for dedupe/blacklist, and produces thumbnails. Windows-only (System.Drawing).
    /// </summary>
    public class ImageService
    {
        public const int ThumbWidth = 320;
        public const int ThumbHeight = 180;

        private readonly HttpClient _http;

        public ImageService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "windows:com.wallpaperreddit.app:v1.0.0 (image-fetch)");
        }

        /// <summary>
        /// Downloads and validates a candidate. Returns the local paths and true content hash,
        /// or a failure reason. <paramref name="id"/> is used for the on-disk file names.
        /// </summary>
        public async Task<DownloadResult> DownloadAsync(string id, string imageUrl, AppSettings s, CancellationToken ct)
        {
            byte[] data;
            try
            {
                using var resp = await _http.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                if (resp.Content.Headers.ContentLength is long len && len > s.MaxFileBytes)
                    return Fail($"too large ({len / 1024 / 1024} MB)");

                data = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Fail($"download error: {ex.Message}");
            }

            if (data.Length > s.MaxFileBytes)
                return Fail($"too large ({data.Length / 1024 / 1024} MB)");

            var sha = Sha256Hex(data);

            int width, height;
            ImageFormat format;
            try
            {
                using var probe = new MemoryStream(data);
                using var img = Image.FromStream(probe, useEmbeddedColorManagement: false, validateImageData: true);
                width = img.Width;
                height = img.Height;
                format = img.RawFormat;
            }
            catch
            {
                return Fail("not a valid image");
            }

            // Quality gates: real wallpaper size + landscape-ish aspect ratio.
            if (width < s.MinWidth || height < s.MinHeight)
                return Fail($"too small ({width}x{height})");

            var aspect = (double)width / height;
            if (aspect < s.MinAspectRatio || aspect > s.MaxAspectRatio)
                return Fail($"aspect {aspect:0.00} outside wallpaper range");

            var ext = ExtensionFor(format);
            var filePath = Path.Combine(AppPaths.WallpapersDir, SafeName(id) + ext);
            var thumbPath = Path.Combine(AppPaths.ThumbnailsDir, SafeName(id) + ".jpg");

            try
            {
                await File.WriteAllBytesAsync(filePath, data, ct).ConfigureAwait(false);
                MakeThumbnail(data, thumbPath);
            }
            catch (Exception ex)
            {
                return Fail($"save error: {ex.Message}");
            }

            return new DownloadResult
            {
                Success = true,
                FilePath = filePath,
                ThumbnailPath = thumbPath,
                Width = width,
                Height = height,
                Bytes = data.Length,
                Sha256 = sha
            };
        }

        /// <summary>(Re)build a thumbnail from an existing full-size wallpaper file.</summary>
        public void RebuildThumbnail(string sourceFile, string thumbPath)
        {
            var data = File.ReadAllBytes(sourceFile);
            MakeThumbnail(data, thumbPath);
        }

        private static void MakeThumbnail(byte[] data, string thumbPath)
        {
            using var src = Image.FromStream(new MemoryStream(data));
            // cover-fit into the thumbnail box, cropping overflow
            double scale = Math.Max((double)ThumbWidth / src.Width, (double)ThumbHeight / src.Height);
            int scaledW = (int)Math.Ceiling(src.Width * scale);
            int scaledH = (int)Math.Ceiling(src.Height * scale);
            int offX = (ThumbWidth - scaledW) / 2;
            int offY = (ThumbHeight - scaledH) / 2;

            using var bmp = new Bitmap(ThumbWidth, ThumbHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(src, new Rectangle(offX, offY, scaledW, scaledH));
            }

            var encoder = GetJpegEncoder();
            var pars = new EncoderParameters(1);
            pars.Param[0] = new EncoderParameter(Encoder.Quality, 82L);
            bmp.Save(thumbPath, encoder, pars);
        }

        private static ImageCodecInfo GetJpegEncoder()
        {
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
                if (codec.FormatID == ImageFormat.Jpeg.Guid)
                    return codec;
            return null;
        }

        private static string Sha256Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
        }

        private static string ExtensionFor(ImageFormat format)
        {
            if (format.Equals(ImageFormat.Png)) return ".png";
            return ".jpg";
        }

        private static string SafeName(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                id = id.Replace(c, '_');
            return id;
        }

        private static DownloadResult Fail(string reason) => new() { Success = false, Reason = reason };
    }
}
