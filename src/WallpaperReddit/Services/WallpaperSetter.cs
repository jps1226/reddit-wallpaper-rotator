using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WallpaperReddit.Services
{
    /// <summary>
    /// Sets the Windows desktop wallpaper. Prefers the modern IDesktopWallpaper COM API
    /// (what the Settings app uses; reliable on Windows 8+ and on shells modified by
    /// ExplorerPatcher), and falls back to the legacy SystemParametersInfo call.
    /// A "Fill" fit is applied so images scale to the screen.
    /// </summary>
    public static class WallpaperSetter
    {
        // ---- Legacy SPI ----------------------------------------------------
        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDWININICHANGE = 0x02;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        // ---- Modern COM: IDesktopWallpaper ---------------------------------
        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId,
                              [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
            // Remaining vtable methods are unused and intentionally omitted.
        }

        [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
        private class DesktopWallpaperClass { }

        /// <summary>Applies the given image file as the current desktop wallpaper on all monitors.</summary>
        public static bool Set(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !System.IO.File.Exists(imagePath))
                return false;

            SetFillStyle();

            if (TrySetViaCom(imagePath))
                return true;

            Logger.Warn("IDesktopWallpaper failed; falling back to SystemParametersInfo.");
            return TrySetViaSpi(imagePath);
        }

        private static bool TrySetViaCom(string imagePath)
        {
            IDesktopWallpaper dw = null;
            try
            {
                dw = (IDesktopWallpaper)new DesktopWallpaperClass();
                uint count = dw.GetMonitorDevicePathCount();
                if (count == 0)
                {
                    dw.SetWallpaper(null, imagePath); // null = all monitors
                }
                else
                {
                    for (uint i = 0; i < count; i++)
                    {
                        var monitor = dw.GetMonitorDevicePathAt(i);
                        dw.SetWallpaper(monitor, imagePath);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"IDesktopWallpaper.SetWallpaper failed (0x{ex.HResult:X8}): {ex.Message}");
                return false;
            }
            finally
            {
                if (dw != null) Marshal.FinalReleaseComObject(dw);
            }
        }

        private static bool TrySetViaSpi(string imagePath)
        {
            try
            {
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
                Logger.Error("Failed to set wallpaper via SPI.", ex);
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
