using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using SteamLoginLite.Models;
using SteamLoginLite.Services;

namespace SteamLoginLite
{
    internal sealed class SettingsDialog : Form
    {
        private readonly TextBox _path = new TextBox { Dock = DockStyle.Fill };
        private readonly CheckBox _news = new CheckBox { Text = "启动后关闭推荐 / 新闻窗口", AutoSize = true };
        private readonly CheckBox _friends = new CheckBox { Text = "启动后关闭好友列表", AutoSize = true };
        private readonly CheckBox _library = new CheckBox { Text = "登录后打开游戏库", AutoSize = true };
        private readonly NumericUpDown _delay = new NumericUpDown { Minimum = 2, Maximum = 120, Width = 90 };
        private readonly NumericUpDown _interval = new NumericUpDown { Minimum = 500, Maximum = 30000, Increment = 100, Width = 90 };
        private readonly Label _runtime = new Label { AutoSize = true, MaximumSize = new Size(470, 0) };
        private readonly CancellationTokenSource _closing = new CancellationTokenSource();
        public AppSettings Settings => new AppSettings { SteamPath = _path.Text.Trim(), CloseRecommendations = _news.Checked, CloseFriendsList = _friends.Checked, OpenLibrary = _library.Checked, StartupDelaySeconds = (int)_delay.Value, QueryIntervalMilliseconds = (int)_interval.Value };
        internal bool[] SmokePreferences => new[] { _news.Checked, _friends.Checked, _library.Checked };
        public SettingsDialog(AppSettings settings)
        {
            Text = "偏好设置"; ClientSize = new Size(560, 500); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; Font = new Font("Microsoft YaHei UI", 9F); AutoScaleMode = AutoScaleMode.Dpi;
            _path.Text = settings.SteamPath; _news.Checked = settings.CloseRecommendations; _friends.Checked = settings.CloseFriendsList; _library.Checked = settings.OpenLibrary;
            _delay.Value = Math.Max(2, Math.Min(120, settings.StartupDelaySeconds)); _interval.Value = Math.Max(500, Math.Min(30000, settings.QueryIntervalMilliseconds));
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 10, AutoScroll = true };
            root.Controls.Add(new Label { Text = "Steam 主程序路径", AutoSize = true });
            var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            var browse = new Button { Text = "浏览…", AutoSize = true }; pathRow.Controls.Add(_path, 0, 0); pathRow.Controls.Add(browse, 1, 0); root.Controls.Add(pathRow);
            browse.Click += (_, __) => { using (var dialog = new OpenFileDialog { Filter = "Steam 主程序|steam.exe", CheckFileExists = true }) if (dialog.ShowDialog(this) == DialogResult.OK) _path.Text = dialog.FileName; };
            root.Controls.Add(_news); root.Controls.Add(_friends); root.Controls.Add(_library);
            var timing = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            timing.Controls.Add(new Label { Text = "启动后等待（秒）", AutoSize = true, Margin = new Padding(0, 6, 0, 0) }); timing.Controls.Add(_delay);
            timing.Controls.Add(new Label { Text = "查询间隔（毫秒）", AutoSize = true, Margin = new Padding(10, 6, 0, 0) }); timing.Controls.Add(_interval); root.Controls.Add(timing);
            var version = WebView2Runtime.AvailableVersion(); _runtime.Text = string.IsNullOrEmpty(version) ? "查询组件：未检测到 WebView2，首次查询时会自动下载并安装。" : "查询组件：WebView2 " + version;
            root.Controls.Add(_runtime);
            var runtimeActions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            var install = new Button { Text = "检测 / 安装查询组件", AutoSize = true }; var cache = new Button { Text = "清理查询缓存", AutoSize = true };
            runtimeActions.Controls.Add(install); runtimeActions.Controls.Add(cache); root.Controls.Add(runtimeActions);
            install.Click += async (_, __) => {
                install.Enabled = cache.Enabled = false;
                try { await WebView2Runtime.EnsureAsync(text => { if (!IsDisposed) _runtime.Text = text; }, _closing.Token); if (!IsDisposed) _runtime.Text = "查询组件已就绪：" + WebView2Runtime.AvailableVersion(); }
                catch (OperationCanceledException) { }
                catch (Exception ex) { if (!IsDisposed) _runtime.Text = "安装未完成：" + ex.Message; }
                finally { if (!IsDisposed) install.Enabled = cache.Enabled = true; }
            };
            cache.Click += (_, __) => {
                try { if (Directory.Exists(PubgPlusQueryForm.CacheDirectory)) Directory.Delete(PubgPlusQueryForm.CacheDirectory, true); _runtime.Text = "查询缓存已清理，账号数据未改动。"; }
                catch { _runtime.Text = "查询缓存仍被浏览器占用，请关闭查询窗口，稍后再试。"; }
            };
            root.Controls.Add(new Label { Text = "账号保存在 Windows 用户目录。关闭工具即退出，不常驻后台。", AutoSize = true, ForeColor = SystemColors.GrayText });
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "保存", AutoSize = true }; var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
            save.Click += (_, __) => { if (_path.Text.Trim().Length > 0 && !File.Exists(_path.Text.Trim())) { MessageBox.Show(this, "所选 Steam 主程序不存在。", "提示"); return; } DialogResult = DialogResult.OK; };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel); root.Controls.Add(buttons);
            foreach (Control control in root.Controls) control.Margin = new Padding(0, 0, 0, 10);
            Controls.Add(root); AcceptButton = save; CancelButton = cancel;
            FormClosing += (_, __) => _closing.Cancel(); FormClosed += (_, __) => _closing.Dispose();
        }
    }
}
