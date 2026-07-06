using System;
using System.IO;
using System.Text;

namespace WallpaperReddit.Services
{
    /// <summary>Tiny append-only logger with size-based rollover. No dependencies.</summary>
    public static class Logger
    {
        private static readonly object Gate = new();
        private const long MaxBytes = 1_000_000; // ~1 MB then roll once

        public static event Action<string> OnLine;

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message, Exception ex = null)
        {
            var text = ex == null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}";
            Write("ERROR", text);
        }

        private static void Write(string level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
            try
            {
                lock (Gate)
                {
                    RollIfNeeded();
                    File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never crash the app.
            }

            try { OnLine?.Invoke(line); } catch { /* UI listener issue is non-fatal */ }
        }

        private static void RollIfNeeded()
        {
            try
            {
                var fi = new FileInfo(AppPaths.LogFile);
                if (fi.Exists && fi.Length > MaxBytes)
                {
                    var backup = AppPaths.LogFile + ".1";
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(AppPaths.LogFile, backup);
                }
            }
            catch { /* ignore */ }
        }
    }
}
