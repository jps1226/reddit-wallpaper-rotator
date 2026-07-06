using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperReddit.Services
{
    /// <summary>Loads and saves <see cref="AppSettings"/> as human-readable JSON.</summary>
    public static class SettingsStore
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(AppPaths.SettingsFile))
                {
                    var json = File.ReadAllText(AppPaths.SettingsFile);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
                    if (settings != null) return settings;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to load settings, using defaults.", ex);
            }

            var fresh = new AppSettings();
            Save(fresh);
            return fresh;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, Options);
                AtomicWrite.WriteAllText(AppPaths.SettingsFile, json);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to save settings.", ex);
            }
        }
    }

    /// <summary>Write-to-temp-then-replace so a crash mid-write can't corrupt a file.</summary>
    internal static class AtomicWrite
    {
        public static void WriteAllText(string path, string contents)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, contents);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
    }
}
