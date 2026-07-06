using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace WallpaperReddit.Services
{
    /// <summary>Manages the per-user "start with Windows" registry Run entry.</summary>
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "WallpaperReddit";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static void Apply(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (key == null) return;

                if (enable)
                {
                    var exe = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exe))
                        key.SetValue(ValueName, $"\"{exe}\" --minimized");
                }
                else if (key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not update startup entry: {ex.Message}");
            }
        }
    }
}
