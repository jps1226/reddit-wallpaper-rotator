using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WallpaperReddit.UI
{
    /// <summary>Editing surface for <see cref="AppSettings"/>. Returns a new settings object on OK.</summary>
    public class SettingsForm : Form
    {
        private readonly AppSettings _original;

        private TextBox _subreddits;
        private NumericUpDown _interval;
        private ComboBox _sort;
        private ComboBox _period;
        private NumericUpDown _minWidth;
        private NumericUpDown _minHeight;
        private NumericUpDown _maxStored;
        private CheckBox _keepFavorites;
        private CheckBox _rotateOnStartup;
        private CheckBox _startWithWindows;
        private CheckBox _showNotifications;
        private CheckBox _allowNsfw;
        private TextBox _clientId;
        private TextBox _username;

        public AppSettings Result { get; private set; }

        public SettingsForm(AppSettings current)
        {
            _original = current;
            BuildUi();
            LoadValues(current);
        }

        private void BuildUi()
        {
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 640);
            Font = new Font("Segoe UI", 9f);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(14),
                AutoScroll = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddSection(layout, "Sources");

            _subreddits = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 96, Dock = DockStyle.Fill };
            AddRow(layout, "Subreddits (one per line)", _subreddits);

            _sort = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Left, Width = 160 };
            _sort.Items.AddRange(Enum.GetNames(typeof(RedditSort)));
            _sort.SelectedIndexChanged += (_, _) => _period.Enabled = (string)_sort.SelectedItem == nameof(RedditSort.Top);
            AddRow(layout, "Sort by", _sort);

            _period = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Left, Width = 160 };
            _period.Items.AddRange(Enum.GetNames(typeof(RedditTimePeriod)));
            AddRow(layout, "Time period (Top only)", _period);

            AddSection(layout, "Rotation");

            _interval = MakeNumeric(1, 10080, 1);
            AddRow(layout, "Change every (minutes)", _interval);

            _rotateOnStartup = new CheckBox { Text = "Change wallpaper when the app starts", AutoSize = true };
            AddRow(layout, "", _rotateOnStartup);

            AddSection(layout, "Image quality");

            _minWidth = MakeNumeric(640, 16000, 10);
            AddRow(layout, "Minimum width (px)", _minWidth);
            _minHeight = MakeNumeric(480, 16000, 10);
            AddRow(layout, "Minimum height (px)", _minHeight);
            _allowNsfw = new CheckBox { Text = "Allow NSFW-flagged posts", AutoSize = true };
            AddRow(layout, "", _allowNsfw);

            AddSection(layout, "Storage");

            _maxStored = MakeNumeric(5, 1000, 5);
            AddRow(layout, "Keep at most (non-favorites)", _maxStored);
            _keepFavorites = new CheckBox { Text = "Never delete favorites", AutoSize = true };
            AddRow(layout, "", _keepFavorites);

            AddSection(layout, "System");

            _startWithWindows = new CheckBox { Text = "Start automatically with Windows", AutoSize = true };
            AddRow(layout, "", _startWithWindows);
            _showNotifications = new CheckBox { Text = "Show a notification when the wallpaper changes", AutoSize = true };
            AddRow(layout, "", _showNotifications);

            AddSection(layout, "Reddit account (recommended)");

            _clientId = new TextBox { Dock = DockStyle.Fill };
            AddRow(layout, "OAuth client id", _clientId);
            _username = new TextBox { Dock = DockStyle.Fill };
            AddRow(layout, "Reddit username", _username);

            var help = new LinkLabel
            {
                Text = "Why? Reddit throttles anonymous access. Create a free \"installed app\" to get a client id →",
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 6),
                MaximumSize = new Size(360, 0)
            };
            help.LinkClicked += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("https://www.reddit.com/prefs/apps") { UseShellExecute = true }); }
                catch { /* ignore */ }
            };
            AddRow(layout, "", help);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10),
                Height = 52
            };
            var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 90, Height = 30 };
            ok.Click += OnSave;
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 30 };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);

            Controls.Add(layout);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static NumericUpDown MakeNumeric(int min, int max, int step) => new()
        {
            Minimum = min,
            Maximum = max,
            Increment = step,
            Width = 120,
            Dock = DockStyle.Left
        };

        private void AddSection(TableLayoutPanel layout, string title)
        {
            var label = new Label
            {
                Text = title,
                Font = new Font(Font, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 4),
                ForeColor = Color.FromArgb(60, 90, 160)
            };
            int row = layout.RowCount;
            layout.RowCount = row + 1;
            layout.Controls.Add(label, 0, row);
            layout.SetColumnSpan(label, 2);
        }

        private void AddRow(TableLayoutPanel layout, string label, Control control)
        {
            int row = layout.RowCount;
            layout.RowCount = row + 1;

            if (!string.IsNullOrEmpty(label))
            {
                var lbl = new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 8, 6) };
                layout.Controls.Add(lbl, 0, row);
                layout.Controls.Add(control, 1, row);
            }
            else
            {
                control.Margin = new Padding(0, 4, 0, 4);
                layout.Controls.Add(control, 1, row);
            }
        }

        private void LoadValues(AppSettings s)
        {
            _subreddits.Text = string.Join(Environment.NewLine, s.Subreddits);
            _interval.Value = Clamp(s.IntervalMinutes, _interval);
            _sort.SelectedItem = s.Sort.ToString();
            _period.SelectedItem = s.TimePeriod.ToString();
            _period.Enabled = s.Sort == RedditSort.Top;
            _minWidth.Value = Clamp(s.MinWidth, _minWidth);
            _minHeight.Value = Clamp(s.MinHeight, _minHeight);
            _maxStored.Value = Clamp(s.MaxStoredWallpapers, _maxStored);
            _keepFavorites.Checked = s.KeepFavoritesForever;
            _rotateOnStartup.Checked = s.RotateOnStartup;
            _startWithWindows.Checked = s.StartWithWindows;
            _showNotifications.Checked = s.ShowNotifications;
            _allowNsfw.Checked = s.AllowNsfw;
            _clientId.Text = s.RedditClientId;
            _username.Text = s.RedditUsername;
        }

        private void OnSave(object sender, EventArgs e)
        {
            var subs = _subreddits.Text
                .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().TrimStart('/').Trim())
                .Where(x => x.Length > 0)
                .Select(x => x.StartsWith("r/", StringComparison.OrdinalIgnoreCase) ? x[2..] : x)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (subs.Count == 0)
            {
                MessageBox.Show(this, "Please enter at least one subreddit.", "Settings",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            // Start from a copy of the original so fields not shown here are preserved.
            var s = Clone(_original);
            s.Subreddits = subs;
            s.IntervalMinutes = (int)_interval.Value;
            s.Sort = Enum.Parse<RedditSort>((string)_sort.SelectedItem);
            s.TimePeriod = Enum.Parse<RedditTimePeriod>((string)_period.SelectedItem);
            s.MinWidth = (int)_minWidth.Value;
            s.MinHeight = (int)_minHeight.Value;
            s.MaxStoredWallpapers = (int)_maxStored.Value;
            s.KeepFavoritesForever = _keepFavorites.Checked;
            s.RotateOnStartup = _rotateOnStartup.Checked;
            s.StartWithWindows = _startWithWindows.Checked;
            s.ShowNotifications = _showNotifications.Checked;
            s.AllowNsfw = _allowNsfw.Checked;
            s.RedditClientId = _clientId.Text.Trim();
            s.RedditUsername = _username.Text.Trim();

            Result = s;
        }

        private static AppSettings Clone(AppSettings s) => new()
        {
            Subreddits = new List<string>(s.Subreddits),
            IntervalMinutes = s.IntervalMinutes,
            Sort = s.Sort,
            TimePeriod = s.TimePeriod,
            FetchLimit = s.FetchLimit,
            MinWidth = s.MinWidth,
            MinHeight = s.MinHeight,
            MinAspectRatio = s.MinAspectRatio,
            MaxAspectRatio = s.MaxAspectRatio,
            MaxFileBytes = s.MaxFileBytes,
            AllowNsfw = s.AllowNsfw,
            MaxStoredWallpapers = s.MaxStoredWallpapers,
            KeepFavoritesForever = s.KeepFavoritesForever,
            RotateOnStartup = s.RotateOnStartup,
            StartWithWindows = s.StartWithWindows,
            ShowNotifications = s.ShowNotifications,
            RedditClientId = s.RedditClientId,
            RedditUsername = s.RedditUsername
        };

        private static decimal Clamp(int value, NumericUpDown ctl)
            => Math.Max(ctl.Minimum, Math.Min(ctl.Maximum, value));
    }
}
