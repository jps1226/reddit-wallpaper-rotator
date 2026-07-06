using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WallpaperReddit.Services
{
    /// <summary>Sets the Windows desktop wallpaper via SystemParametersInfo, with a "fill" fit.</summary>
    public static class WallpaperSetter
    {
        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDWININICHANGE = 0x02;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        /// <summary>Applies the given image file as the current desktop wallpaper.</summary>
        public static bool Set(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !System.IO.File.Exists(imagePath))
                return false;

            try
            {
                SetFillStyle();
                var result = SystemParametersInfo(
                    SPI_SETDESKWALLPAPER, 0, imagePath,
                    SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);

                if (result == 0)
                {
                    Logger.Error($"SystemParametersInfo failed (Win32 error {Marshal.GetLastWin32Error()}).");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to set wallpaper.", ex);
                return false;
            }
        }

        /// <summary>WallpaperStyle=10 (Fill) + TileWallpaper=0 so images scale to the screen.</summary>
        private static void SetFillStyle()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
                if (key == null) return;
                key.SetValue("WallpaperStyle", "10");
                key.SetValue("TileWallpaper", "0");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Could not set wallpaper fit style: {ex.Message}");
            }
        }
    }
}
