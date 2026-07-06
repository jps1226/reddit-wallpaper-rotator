using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WallpaperReddit.Services;
using WallpaperReddit.UI;

namespace WallpaperReddit.App
{
    /// <summary>
    /// Root of the running app: holds shared services, owns the tray icon and the rotation
    /// timer, and shows the main window on demand. The app lives in the tray so rotation
    /// keeps working with no window open.
    /// </summary>
    public class TrayContext : ApplicationContext
    {
        public AppSettings Settings { get; private set; }
        public Library Library { get; }
        public RedditClient Reddit { get; }
        public ImageService Images { get; }
        public RotationEngine Engine { get; }

        private readonly NotifyIcon _tray;
        private readonly System.Windows.Forms.Timer _timer;
        private MainForm _mainForm;
        private DateTime _nextRunUtc = DateTime.MaxValue;
        private bool _busy;

        public event Action<string> StatusChanged;
        public DateTime NextRunLocal => _nextRunUtc == DateTime.MaxValue ? DateTime.MaxValue : _nextRunUtc.ToLocalTime();

        public TrayContext(bool startMinimized)
        {
            Settings = SettingsStore.Load();
            Library = new Library();
            Library.Load();

            Reddit = new RedditClient(() => Settings);
            Images = new ImageService();
            Engine = new RotationEngine(Reddit, Images, Library, () => Settings);
            Engine.Status += msg => StatusChanged?.Invoke(msg);

            _tray = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "Reddit Wallpaper Rotator",
                Visible = true,
                ContextMenuStrip = BuildMenu()
            };
            _tray.DoubleClick += (_, _) => ShowMainWindow();

            // Fires every 30s; we decide whether it's time to rotate. This keeps the interval
            // responsive to settings changes without recreating timers.
            _timer = new System.Windows.Forms.Timer { Interval = 30_000 };
            _timer.Tick += async (_, _) => await OnTick().ConfigureAwait(true);
            _timer.Start();

            StartupManager.Apply(Settings.StartWithWindows);
            ScheduleNext(fromNow: !Settings.RotateOnStartup);

            if (Settings.RotateOnStartup)
                _ = RotateNowAsync();

            if (!startMinimized)
                ShowMainWindow();
        }

        private static Icon LoadTrayIcon()
        {
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe))
                {
                    var ico = Icon.ExtractAssociatedIcon(exe);
                    if (ico != null) return ico;
                }
            }
            catch { /* fall through */ }
            return SystemIcons.Application;
        }

        private ContextMenuStrip BuildMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, (_, _) => ShowMainWindow());
            menu.Items.Add("Next wallpaper now", null, async (_, _) => await RotateNowAsync());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Settings…", null, (_, _) => ShowMainWindow(openSettings: true));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => ExitApp());
            return menu;
        }

        // ---- Scheduling ----------------------------------------------------

        public void ScheduleNext(bool fromNow = true)
        {
            var minutes = Math.Max(1, Settings.IntervalMinutes);
            _nextRunUtc = DateTime.UtcNow.AddMinutes(minutes);
            StatusChanged?.Invoke($"Next change at {NextRunLocal:t}.");
        }

        private async Task OnTick()
        {
            if (_busy) return;
            if (DateTime.UtcNow < _nextRunUtc) return;
            await RotateNowAsync().ConfigureAwait(true);
        }

        public async Task RotateNowAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var item = await Engine.RotateAsync().ConfigureAwait(true);
                if (item != null && Settings.ShowNotifications)
                    ShowBalloon("Wallpaper changed", $"{item.Title}\r\nr/{item.Subreddit}");
            }
            finally
            {
                _busy = false;
                ScheduleNext();
            }
        }

        public void ReloadSettings(AppSettings updated)
        {
            Settings = updated;
            SettingsStore.Save(Settings);
            StartupManager.Apply(Settings.StartWithWindows);
            ScheduleNext();
        }

        // ---- Window / tray -------------------------------------------------

        public void ShowMainWindow(bool openSettings = false)
        {
            if (_mainForm == null || _mainForm.IsDisposed)
            {
                _mainForm = new MainForm(this);
                _mainForm.FormClosed += (_, _) => _mainForm = null;
            }

            if (!_mainForm.Visible) _mainForm.Show();
            if (_mainForm.WindowState == FormWindowState.Minimized)
                _mainForm.WindowState = FormWindowState.Normal;
            _mainForm.Activate();

            if (openSettings) _mainForm.OpenSettings();
        }

        private void ShowBalloon(string title, string text)
        {
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.ShowBalloonTip(4000);
            }
            catch { /* balloons are best-effort */ }
        }

        public void ExitApp()
        {
            _timer.Stop();
            _tray.Visible = false;
            _tray.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer?.Dispose();
                _tray?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
