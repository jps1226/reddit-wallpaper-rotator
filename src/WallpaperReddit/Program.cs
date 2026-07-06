using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using WallpaperReddit.App;
using WallpaperReddit.Services;

namespace WallpaperReddit
{
    internal static class Program
    {
        private static Mutex _singleInstance;

        [STAThread]
        private static void Main(string[] args)
        {
            // Single instance: if already running, just exit (the tray icon is the app).
            _singleInstance = new Mutex(initiallyOwned: true, "WallpaperReddit_SingleInstance_9F3C", out bool isNew);
            if (!isNew) return;

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Logger.Error("Unhandled exception.", e.ExceptionObject as Exception);
            Application.ThreadException += (_, e) =>
                Logger.Error("UI thread exception.", e.Exception);

            var startMinimized = args.Any(a =>
                a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-m", StringComparison.OrdinalIgnoreCase));

            Logger.Info("WallpaperReddit starting.");

            try
            {
                Application.Run(new TrayContext(startMinimized));
            }
            catch (Exception ex)
            {
                Logger.Error("Fatal startup error.", ex);
                MessageBox.Show("The app failed to start. See the log at:\n" + AppPaths.LogFile,
                    "Reddit Wallpaper Rotator", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _singleInstance?.ReleaseMutex();
            }
        }
    }
}
