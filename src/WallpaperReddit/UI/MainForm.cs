using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using WallpaperReddit.App;
using WallpaperReddit.Services;

namespace WallpaperReddit.UI
{
    /// <summary>The single main window: history grid, blacklist, and activity log.</summary>
    public class MainForm : Form
    {
        private readonly TrayContext _ctx;

        private ToolStrip _toolbar;
        private ToolStripButton _btnNext;
        private ToolStripButton _btnRebuild;
        private ToolStripButton _btnSettings;
        private ToolStripButton _btnFavorites;

        private TabControl _tabs;
        private ListView _history;
        private ImageList _thumbs;
        private ListView _blacklistView;
        private TextBox _log;

        private StatusStrip _status;
        private ToolStripStatusLabel _statusText;
        private ToolStripStatusLabel _nextRun;

        private bool _favoritesOnly;

        public MainForm(TrayContext ctx)
        {
            _ctx = ctx;
            BuildUi();

            _ctx.Library.Changed += OnLibraryChanged;
            _ctx.StatusChanged += OnStatus;
            Logger.OnLine += OnLogLine;

            FormClosing += OnFormClosing;

            RefreshHistory();
            RefreshBlacklist();
            UpdateNextRun();
        }

        // ---- UI construction ----------------------------------------------

        private void BuildUi()
        {
            Text = "Reddit Wallpaper Rotator";
            Icon = TryLoadIcon();
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(820, 560);
            Size = new Size(980, 660);
            Font = new Font("Segoe UI", 9f);

            _toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 2, 6, 2) };
            _btnNext = new ToolStripButton("Next wallpaper now") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            _btnNext.Click += async (_, _) => await _ctx.RotateNowAsync();
            _btnRebuild = new ToolStripButton("Rebuild thumbnails") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            _btnRebuild.Click += (_, _) => RebuildThumbnails();
            _btnSettings = new ToolStripButton("Settings…") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            _btnSettings.Click += (_, _) => OpenSettings();
            _btnFavorites = new ToolStripButton("Favorites only") { CheckOnClick = true, DisplayStyle = ToolStripItemDisplayStyle.Text };
            _btnFavorites.CheckedChanged += (_, _) => { _favoritesOnly = _btnFavorites.Checked; RefreshHistory(); };

            _toolbar.Items.AddRange(new ToolStripItem[]
            {
                _btnNext, new ToolStripSeparator(), _btnRebuild,
                new ToolStripSeparator(), _btnFavorites,
                new ToolStripSeparator(), _btnSettings
            });

            _thumbs = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size(240, 135)
            };

            _history = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.LargeIcon,
                LargeImageList = _thumbs,
                MultiSelect = false,
                HideSelection = false,
                BorderStyle = BorderStyle.None
            };
            _history.MouseDoubleClick += (_, _) => SetSelectedAsWallpaper();
            _history.ContextMenuStrip = BuildHistoryMenu();

            var historyTab = new TabPage("History") { Padding = new Padding(6) };
            historyTab.Controls.Add(_history);

            _blacklistView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                BorderStyle = BorderStyle.None
            };
            _blacklistView.Columns.Add("Title", 520);
            _blacklistView.Columns.Add("Added", 160);
            _blacklistView.ContextMenuStrip = BuildBlacklistMenu();

            var blacklistTab = new TabPage("Blacklist") { Padding = new Padding(6) };
            blacklistTab.Controls.Add(_blacklistView);

            _log = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                Font = new Font("Consolas", 9f)
            };
            var logTab = new TabPage("Activity") { Padding = new Padding(6) };
            logTab.Controls.Add(_log);

            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.AddRange(new[] { historyTab, blacklistTab, logTab });

            _status = new StatusStrip();
            _statusText = new ToolStripStatusLabel("Ready.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _nextRun = new ToolStripStatusLabel("");
            _status.Items.Add(_statusText);
            _status.Items.Add(_nextRun);

            Controls.Add(_tabs);
            Controls.Add(_toolbar);
            Controls.Add(_status);
        }

        private ContextMenuStrip BuildHistoryMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Set as wallpaper", null, (_, _) => SetSelectedAsWallpaper());
            menu.Items.Add("Toggle favorite", null, (_, _) => ToggleFavorite());
            menu.Items.Add("Open on Reddit", null, (_, _) => OpenOnReddit());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Blacklist (never show again)", null, (_, _) => BlacklistSelected());
            menu.Items.Add("Remove from history", null, (_, _) => RemoveSelected());
            return menu;
        }

        private ContextMenuStrip BuildBlacklistMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Remove from blacklist", null, (_, _) => UnblacklistSelected());
            return menu;
        }

        // ---- History ------------------------------------------------------

        private void RefreshHistory()
        {
            var items = _ctx.Library.GetHistory();
            if (_favoritesOnly) items = items.Where(i => i.IsFavorite).ToList();

            _history.BeginUpdate();
            try
            {
                _history.Items.Clear();
                _thumbs.Images.Clear();

                foreach (var it in items)
                {
                    var key = it.Id;
                    if (!_thumbs.Images.ContainsKey(key))
                        _thumbs.Images.Add(key, LoadThumb(it));

                    var label = (it.IsFavorite ? "★ " : "") + Ellipsize(it.Title, 40);
                    var lvi = new ListViewItem(label)
                    {
                        ImageKey = key,
                        Tag = it,
                        ToolTipText = $"{it.Title}\r\nr/{it.Subreddit} · {it.Width}x{it.Height}\r\n{it.DownloadedAt:g}"
                    };
                    _history.Items.Add(lvi);
                }
            }
            finally
            {
                _history.EndUpdate();
            }

            _history.ShowItemToolTips = true;
        }

        private Image LoadThumb(WallpaperItem it)
        {
            try
            {
                var path = it.ThumbnailPath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return ImageFromFileNoLock(path);

                // No thumbnail but we still have the full image → build one on the fly.
                if (it.FileExists)
                {
                    var thumb = Path.Combine(AppPaths.ThumbnailsDir, SafeKey(it.Id) + ".jpg");
                    _ctx.Images.RebuildThumbnail(it.FilePath, thumb);
                    it.ThumbnailPath = thumb;
                    return ImageFromFileNoLock(thumb);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Thumbnail load failed for {it.Id}: {ex.Message}");
            }
            return Placeholder();
        }

        private WallpaperItem SelectedItem()
            => _history.SelectedItems.Count > 0 ? _history.SelectedItems[0].Tag as WallpaperItem : null;

        private void SetSelectedAsWallpaper()
        {
            var it = SelectedItem();
            if (it == null) return;
            if (!_ctx.Engine.ApplyExisting(it))
                MessageBox.Show(this, "That image file is missing. It may have been cleaned up.",
                    "Set wallpaper", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ToggleFavorite()
        {
            var it = SelectedItem();
            if (it == null) return;
            _ctx.Library.SetFavorite(it.Id, !it.IsFavorite);
        }

        private void OpenOnReddit()
        {
            var it = SelectedItem();
            if (it == null || string.IsNullOrEmpty(it.Permalink)) return;
            try { Process.Start(new ProcessStartInfo(it.Permalink) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.Warn($"Open on Reddit failed: {ex.Message}"); }
        }

        private void BlacklistSelected()
        {
            var it = SelectedItem();
            if (it == null) return;
            if (MessageBox.Show(this, $"Blacklist this image so it never appears again?\r\n\r\n{it.Title}",
                "Blacklist", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _ctx.Library.Blacklist(it);
            }
        }

        private void RemoveSelected()
        {
            var it = SelectedItem();
            if (it == null) return;
            _ctx.Library.Remove(it.Id, deleteFiles: true);
        }

        // ---- Blacklist ----------------------------------------------------

        private void RefreshBlacklist()
        {
            _blacklistView.BeginUpdate();
            try
            {
                _blacklistView.Items.Clear();
                foreach (var b in _ctx.Library.GetBlacklist())
                {
                    var lvi = new ListViewItem(Ellipsize(b.Title ?? b.SourceUrl ?? b.Id, 90)) { Tag = b };
                    lvi.SubItems.Add(b.AddedAt.ToString("g"));
                    _blacklistView.Items.Add(lvi);
                }
            }
            finally { _blacklistView.EndUpdate(); }
        }

        private void UnblacklistSelected()
        {
            if (_blacklistView.SelectedItems.Count == 0) return;
            if (_blacklistView.SelectedItems[0].Tag is BlacklistEntry b)
                _ctx.Library.RemoveFromBlacklist(b.Id, b.Sha256, b.SourceUrl);
        }

        // ---- Thumbnails ---------------------------------------------------

        private void RebuildThumbnails()
        {
            var items = _ctx.Library.GetHistory().Where(i => i.FileExists).ToList();
            int done = 0, failed = 0;
            UseWaitCursor = true;
            try
            {
                foreach (var it in items)
                {
                    try
                    {
                        var thumb = Path.Combine(AppPaths.ThumbnailsDir, SafeKey(it.Id) + ".jpg");
                        _ctx.Images.RebuildThumbnail(it.FilePath, thumb);
                        if (it.ThumbnailPath != thumb)
                        {
                            it.ThumbnailPath = thumb;
                            _ctx.Library.AddOrUpdate(it);
                        }
                        done++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Logger.Warn($"Rebuild thumb failed for {it.Id}: {ex.Message}");
                    }
                }
            }
            finally { UseWaitCursor = false; }

            RefreshHistory();
            MessageBox.Show(this, $"Rebuilt {done} thumbnail(s)." + (failed > 0 ? $" {failed} failed." : ""),
                "Rebuild thumbnails", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ---- Settings -----------------------------------------------------

        public void OpenSettings()
        {
            using var dlg = new SettingsForm(_ctx.Settings);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _ctx.ReloadSettings(dlg.Result);
                UpdateNextRun();
                _statusText.Text = "Settings saved.";
            }
        }

        // ---- Event plumbing (marshalled to UI thread) ---------------------

        private void OnLibraryChanged()
        {
            if (IsDisposed) return;
            BeginInvoke(new Action(() => { RefreshHistory(); RefreshBlacklist(); }));
        }

        private void OnStatus(string msg)
        {
            if (IsDisposed) return;
            BeginInvoke(new Action(() => { _statusText.Text = msg; UpdateNextRun(); }));
        }

        private void OnLogLine(string line)
        {
            if (IsDisposed || _log == null) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _log.AppendText(line + Environment.NewLine);
                }));
            }
            catch { /* window may be closing */ }
        }

        private void UpdateNextRun()
        {
            var next = _ctx.NextRunLocal;
            _nextRun.Text = next == DateTime.MaxValue ? "" : $"Next change: {next:t}";
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            // X hides to tray rather than exiting, so rotation keeps running.
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            _ctx.Library.Changed -= OnLibraryChanged;
            _ctx.StatusChanged -= OnStatus;
            Logger.OnLine -= OnLogLine;
        }

        // ---- Helpers ------------------------------------------------------

        private static Image ImageFromFileNoLock(string path)
        {
            var bytes = File.ReadAllBytes(path);
            return Image.FromStream(new MemoryStream(bytes));
        }

        private static Image Placeholder()
        {
            var bmp = new Bitmap(240, 135);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(230, 230, 235));
            using var pen = new Pen(Color.FromArgb(180, 180, 190), 2);
            g.DrawRectangle(pen, 1, 1, 237, 132);
            return bmp;
        }

        private static Icon TryLoadIcon()
        {
            try
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                return exe != null ? Icon.ExtractAssociatedIcon(exe) : SystemIcons.Application;
            }
            catch { return SystemIcons.Application; }
        }

        private static string SafeKey(string id)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                id = id.Replace(c, '_');
            return id;
        }

        private static string Ellipsize(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s[..(max - 1)] + "…";
        }
    }
}
