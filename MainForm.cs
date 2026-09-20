using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamLoginLite.Models;
using SteamLoginLite.Services;

namespace SteamLoginLite
{
    public sealed class MainForm : Form
    {
        private readonly Color _navy = Color.FromArgb(19, 28, 46);
        private readonly Color _blue = Color.FromArgb(67, 97, 238);
        private readonly Color _background = Color.FromArgb(244, 247, 251);
        private readonly DataStore _store = new DataStore();
        private readonly SteamService _steam = new SteamService();
        private AppData _data;
        private readonly Panel _content = new Panel();
        private readonly Label _pageTitle = new Label();
        private DataGridView _accountsGrid;
        private TextBox _searchBox;
        private TextBox _importText;
        private DataGridView _previewGrid;
        private Label _previewCount;
        private List<ImportedAccount> _preview = new List<ImportedAccount>();
        private TextBox _steamPath;
        private NumericUpDown _startupDelay;
        private CheckBox _closeRecommendations;
        private CheckBox _closeFriends;
        private CheckBox _openLibrary;
        private CancellationTokenSource _operation = new CancellationTokenSource();
        private string _statusFilter = "";
        private readonly List<Button> _navButtons = new List<Button>();
        private Button _activeNavButton;

        public MainForm()
        {
            Text = "Steam切换器";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Width = 1380;
            Height = 760;
            MinimumSize = new Size(1120, 640);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = _background;
            Font = new Font("Microsoft YaHei UI", 9F);
            try { _data = _store.Load(); }
            catch (Exception ex) { _data = new AppData(); MessageBox.Show(ex.Message, "数据恢复", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            if (string.IsNullOrWhiteSpace(_data.Settings.SteamPath)) _data.Settings.SteamPath = SteamService.FindSteamPath();
            SyncCurrentAccountFromSteam();
            BuildShell();
            if (_navButtons.Count > 0) SetActiveNav(_navButtons[_navButtons.Count - 1]);
            ShowAccountsPage();
            FormClosing += (_, __) => { _operation.Cancel(); SaveData(false); };
        }

        private void BuildShell()
        {
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 210, BackColor = _navy, Padding = new Padding(18, 24, 18, 18) };
            var brand = new Label { Text = "Steam切换器", ForeColor = Color.White, Font = new Font(Font.FontFamily, 15, FontStyle.Bold), Height = 58, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            sidebar.Controls.Add(NavButton("设置", NavIcon.Settings, ShowSettingsPage));
            sidebar.Controls.Add(NavButton("批量导入", NavIcon.Import, ShowImportPage));
            sidebar.Controls.Add(NavButton("账号管理", NavIcon.Accounts, ShowAccountsPage));
            sidebar.Controls.Add(brand);

            var header = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Color.White, Padding = new Padding(28, 0, 28, 0) };
            _pageTitle.Dock = DockStyle.Left;
            _pageTitle.AutoSize = false;
            _pageTitle.Width = 500;
            _pageTitle.TextAlign = ContentAlignment.MiddleLeft;
            _pageTitle.Font = new Font(Font.FontFamily, 18, FontStyle.Bold);
            _pageTitle.ForeColor = _navy;
            var portable = new Label { Text = "● 绿色便携版 · 数据仅保存在本机", Dock = DockStyle.Right, Width = 260, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(72, 91, 116) };
            header.Controls.Add(portable);
            header.Controls.Add(_pageTitle);

            _content.Dock = DockStyle.Fill;
            _content.Padding = new Padding(26);
            _content.BackColor = _background;
            Controls.Add(_content);
            Controls.Add(header);
            Controls.Add(sidebar);
        }

        private enum NavIcon { Accounts, Import, Settings }

        private static readonly Color NavTextIdle = Color.FromArgb(186, 196, 212);
        private static readonly Color NavTextActive = Color.White;
        private static readonly Color NavHover = Color.FromArgb(32, 45, 69);
        private static readonly Color NavActiveBackground = Color.FromArgb(29, 41, 66);

        private Button NavButton(string text, NavIcon icon, Action action)
        {
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = 48,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = NavTextIdle,
                BackColor = _navy,
                Cursor = Cursors.Hand,
                Padding = new Padding(38, 0, 0, 0),
                Margin = new Padding(0, 0, 0, 2),
                Tag = icon
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = NavHover;
            button.FlatAppearance.MouseDownBackColor = NavActiveBackground;
            button.Paint += (sender, e) =>
            {
                var owner = (Button)sender;
                var active = ReferenceEquals(owner, _activeNavButton);
                if (active)
                {
                    using (var accent = new SolidBrush(_blue))
                        e.Graphics.FillRectangle(accent, 0, (owner.Height - 20) / 2, 3, 20);
                }
                DrawNavIcon(e.Graphics, icon, new Rectangle(13, (owner.Height - 16) / 2, 16, 16), owner.ForeColor);
            };
            button.Click += (sender, __) => { SetActiveNav((Button)sender); action(); };
            _navButtons.Add(button);
            return button;
        }

        private void SetActiveNav(Button button)
        {
            _activeNavButton = button;
            foreach (var item in _navButtons)
            {
                var active = ReferenceEquals(item, button);
                item.ForeColor = active ? NavTextActive : NavTextIdle;
                item.BackColor = active ? NavActiveBackground : _navy;
                item.Font = new Font(item.Font, active ? FontStyle.Bold : FontStyle.Regular);
                item.Invalidate();
            }
        }

        private static void DrawNavIcon(Graphics graphics, NavIcon icon, Rectangle bounds, Color color)
        {
            var previousMode = graphics.SmoothingMode;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var x = bounds.X;
            var y = bounds.Y;
            var size = (float)bounds.Width;
            using (var pen = new Pen(color, 1.5f)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round,
                LineJoin = System.Drawing.Drawing2D.LineJoin.Round
            })
            {
                if (icon == NavIcon.Accounts)
                {
                    // 人像：头 + 肩弧
                    graphics.DrawEllipse(pen, x + size * 0.32f, y + size * 0.10f, size * 0.36f, size * 0.36f);
                    graphics.DrawArc(pen, x + size * 0.17f, y + size * 0.56f, size * 0.66f, size * 0.48f, 180, 180);
                }
                else if (icon == NavIcon.Import)
                {
                    // 下载：竖线 + V 形箭头 + 托盘
                    graphics.DrawLine(pen, x + size * 0.50f, y + size * 0.10f, x + size * 0.50f, y + size * 0.60f);
                    graphics.DrawLines(pen, new[]
                    {
                        new PointF(x + size * 0.28f, y + size * 0.40f),
                        new PointF(x + size * 0.50f, y + size * 0.63f),
                        new PointF(x + size * 0.72f, y + size * 0.40f)
                    });
                    graphics.DrawLine(pen, x + size * 0.16f, y + size * 0.86f, x + size * 0.84f, y + size * 0.86f);
                }
                else
                {
                    // 设置：双滑杆 + 滑块
                    graphics.DrawLine(pen, x + size * 0.12f, y + size * 0.30f, x + size * 0.88f, y + size * 0.30f);
                    graphics.DrawLine(pen, x + size * 0.12f, y + size * 0.70f, x + size * 0.88f, y + size * 0.70f);
                    using (var brush = new SolidBrush(color))
                    {
                        graphics.FillEllipse(brush, x + size * 0.25f, y + size * 0.17f, size * 0.26f, size * 0.26f);
                        graphics.FillEllipse(brush, x + size * 0.51f, y + size * 0.57f, size * 0.26f, size * 0.26f);
                    }
                }
            }
            graphics.SmoothingMode = previousMode;
        }

        private void ShowAccountsPage()
        {
            BeginPage("账号管理");
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            _searchBox = new TextBox { Width = 230, Font = new Font(Font.FontFamily, 10), Margin = new Padding(0, 8, 12, 0) };
            _searchBox.TextChanged += (_, __) => RefreshAccounts();
            toolbar.Controls.Add(_searchBox);
            toolbar.Controls.Add(ActionButton("批量查询", QueryChecked, false));
            toolbar.Controls.Add(ActionButton("批量删除", DeleteSelected, false));

            _accountsGrid = CreateGrid(true);
            _accountsGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "选择", DataPropertyName = "UiSelected", Width = 48, ReadOnly = false });
            AddColumn(_accountsGrid, "账号", "Username", 125);
            AddColumn(_accountsGrid, "查询ID", "EffectiveGameId", 125);
            AddColumn(_accountsGrid, "等级", "LevelText", 55);
            AddColumn(_accountsGrid, "封禁状态", "Status", 92);
            AddColumn(_accountsGrid, "最后登录", "LastLoginText", 145);
            AddColumn(_accountsGrid, "最后查询", "LastQueryText", 145);
            AddColumn(_accountsGrid, "备注", "Note", 100);
            AddActionColumn(_accountsGrid, "LoginAction", "登录");
            AddActionColumn(_accountsGrid, "QueryAction", "查询");
            AddActionColumn(_accountsGrid, "EditAction", "编辑");
            AddActionColumn(_accountsGrid, "DeleteAction", "删除", true);
            _accountsGrid.CellFormatting += (_, e) =>
            {
                var property = _accountsGrid.Columns[e.ColumnIndex].DataPropertyName;
                if (property != "Status" || e.Value == null) return;
                var status = e.Value.ToString();
                e.CellStyle.ForeColor = status == "正常" ? Color.FromArgb(14, 159, 110) : status.Contains("封禁") ? Color.FromArgb(220, 53, 69) : Color.FromArgb(99, 115, 136);
                e.CellStyle.Font = new Font(Font, FontStyle.Bold);
            };
            _accountsGrid.CurrentCellDirtyStateChanged += (_, __) =>
            {
                if (_accountsGrid.IsCurrentCellDirty) _accountsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _accountsGrid.CellContentClick += async (_, e) =>
            {
                if (e.RowIndex < 0) return;
                var action = _accountsGrid.Columns[e.ColumnIndex].Name;
                if (action != "LoginAction" && action != "QueryAction" && action != "EditAction" && action != "DeleteAction") return;
                var account = _accountsGrid.Rows[e.RowIndex].DataBoundItem as AccountRecord;
                if (account == null) return;
                if (action == "LoginAction") { await LoginAccountAsync(account); return; }
                if (action == "EditAction") { EditAccount(account); return; }
                if (action == "DeleteAction") { DeleteAccount(account); return; }
                RunPubgPlusQuery(new List<AccountRecord> { account });
            };
            _content.Controls.Add(_accountsGrid);
            _content.Controls.Add(toolbar);
            _content.Controls.Add(CreateSummaryPanel());
            RefreshAccounts();
        }

        private Control CreateSummaryPanel()
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 102, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            panel.Controls.Add(StatCard("stat_total", "账号总数", _data.Accounts.Count.ToString(), _navy, ""));
            panel.Controls.Add(StatCard("stat_normal", "正常", _data.Accounts.Count(a => a.Status == "正常").ToString(), Color.FromArgb(14, 159, 110), "正常"));
            panel.Controls.Add(StatCard("stat_temp", "临时封禁", _data.Accounts.Count(a => a.Status == "临时封禁").ToString(), Color.FromArgb(220, 53, 69), "临时封禁"));
            panel.Controls.Add(StatCard("stat_permanent", "永久封禁", _data.Accounts.Count(a => a.Status == "永久封禁").ToString(), Color.FromArgb(220, 53, 69), "永久封禁"));
            return panel;
        }

        private Control StatCard(string name, string title, string value, Color valueColor, string filter)
        {
            var card = new Panel { Name = name + "_card", Width = 170, Height = 86, BackColor = Color.White, Margin = new Padding(0, 0, 14, 16), Padding = new Padding(16, 12, 16, 8), Cursor = Cursors.Hand, Tag = filter };
            var valueLabel = new Label { Name = name, Text = value, Dock = DockStyle.Bottom, Height = 34, Font = new Font(Font.FontFamily, 16, FontStyle.Bold), ForeColor = valueColor, Cursor = Cursors.Hand };
            var titleLabel = new Label { Text = title, Dock = DockStyle.Top, Height = 24, ForeColor = Color.FromArgb(99, 115, 136), Cursor = Cursors.Hand };
            Action applyFilter = () =>
            {
                _statusFilter = filter;
                UpdateStatCardSelection();
                RefreshAccounts();
            };
            card.Click += (_, __) => applyFilter();
            valueLabel.Click += (_, __) => applyFilter();
            titleLabel.Click += (_, __) => applyFilter();
            card.Controls.Add(valueLabel);
            card.Controls.Add(titleLabel);
            return card;
        }

        private void UpdateStatCardSelection()
        {
            foreach (var key in new[] { "stat_total", "stat_normal", "stat_temp", "stat_permanent" })
            {
                var matches = _content.Controls.Find(key + "_card", true);
                if (matches.Length == 0) continue;
                var card = matches[0] as Panel;
                if (card == null) continue;
                var selected = string.Equals(Convert.ToString(card.Tag), _statusFilter, StringComparison.Ordinal);
                card.BackColor = selected ? Color.FromArgb(240, 247, 255) : Color.White;
                card.Padding = selected ? new Padding(16, 10, 16, 8) : new Padding(16, 12, 16, 8);
            }
        }

        private void ShowImportPage()
        {
            BeginPage("批量导入");
            var hint = new Label { Dock = DockStyle.Top, Height = 50, Text = "每行一个账号，支持中文标签格式以及 -- / --- / ---- 分隔。四段数据没有游戏ID时，自动使用Steam账号名。", ForeColor = Color.FromArgb(72, 91, 116) };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 210, IsSplitterFixed = false };
            _importText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), BorderStyle = BorderStyle.FixedSingle };
            var inputHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            var placeholder = new Label
            {
                Text = "示例：账号----密码----邮箱----邮箱密码\r\n也支持中文标签格式，粘贴后点击“解析预览”",
                AutoSize = true,
                Location = new Point(7, 7),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(140, 150, 164),
                Font = new Font("Consolas", 10),
                Cursor = Cursors.IBeam
            };
            Action updatePlaceholder = () => placeholder.Visible = _importText.TextLength == 0 && !_importText.Focused;
            placeholder.Click += (_, __) => _importText.Focus();
            _importText.Enter += (_, __) => placeholder.Visible = false;
            _importText.Leave += (_, __) => updatePlaceholder();
            _importText.TextChanged += (_, __) => updatePlaceholder();
            inputHost.Controls.Add(_importText);
            inputHost.Controls.Add(placeholder);
            placeholder.BringToFront();
            var importActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48 };
            importActions.Controls.Add(ActionButton("解析预览", ParsePreview, false));
            importActions.Controls.Add(ActionButton("导入有效账号", CommitImport, true));
            _previewCount = new Label { Text = "解析数量：0 条", AutoSize = true, Margin = new Padding(8, 13, 0, 0), ForeColor = Color.FromArgb(72, 91, 116), Font = new Font(Font, FontStyle.Bold) };
            importActions.Controls.Add(_previewCount);
            split.Panel1.Controls.Add(inputHost);
            split.Panel1.Controls.Add(importActions);
            _previewGrid = CreateGrid();
            AddColumn(_previewGrid, "行", "LineNumber", 50);
            AddColumn(_previewGrid, "Steam账号", "Username", 140);
            AddColumn(_previewGrid, "邮箱", "Email", 210);
            AddColumn(_previewGrid, "查询ID", "GameId", 140);
            AddColumn(_previewGrid, "结果", "Error", 250);
            split.Panel2.Controls.Add(_previewGrid);
            _content.Controls.Add(split);
            _content.Controls.Add(hint);
        }

        private void ShowSettingsPage()
        {
            BeginPage("设置");
            var card = new Panel { Dock = DockStyle.Top, Height = 430, BackColor = Color.White, Padding = new Padding(28) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 7 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            layout.RowStyles.Clear();
            for (var i = 0; i < 7; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            _steamPath = new TextBox { Dock = DockStyle.Fill, Text = _data.Settings.SteamPath, Margin = new Padding(0, 12, 12, 8) };
            var browse = ActionButton("选择文件", BrowseSteam, false);
            _startupDelay = new NumericUpDown { Minimum = 2, Maximum = 30, Value = Math.Max(2, Math.Min(30, _data.Settings.StartupDelaySeconds)), Width = 90, Margin = new Padding(0, 10, 0, 0) };
            _closeRecommendations = Option("自动关闭推荐/新闻窗口", _data.Settings.CloseRecommendations);
            _closeFriends = Option("自动关闭好友列表", _data.Settings.CloseFriendsList);
            _openLibrary = Option("登录后自动打开游戏库", _data.Settings.OpenLibrary);
            layout.Controls.Add(FieldLabel("Steam 路径"), 0, 0); layout.Controls.Add(_steamPath, 1, 0); layout.Controls.Add(browse, 2, 0);
            layout.Controls.Add(FieldLabel("启动等待（秒）"), 0, 1); layout.Controls.Add(_startupDelay, 1, 1);
            layout.Controls.Add(FieldLabel("启动后操作"), 0, 2); layout.Controls.Add(_closeRecommendations, 1, 2);
            layout.Controls.Add(new Label(), 0, 3); layout.Controls.Add(_closeFriends, 1, 3);
            layout.Controls.Add(new Label(), 0, 4); layout.Controls.Add(_openLibrary, 1, 4);
            var note = new Label { Text = "登录方式：完全退出旧 Steam 后，通过 steam.exe -login 启动。Steam Guard 和验证码仍由 Steam 官方窗口处理。", ForeColor = Color.FromArgb(72, 91, 116), AutoSize = true, Margin = new Padding(0, 15, 0, 0) };
            layout.Controls.Add(note, 1, 5); layout.SetColumnSpan(note, 2);
            layout.Controls.Add(ActionButton("保存设置", SaveSettings, true), 1, 6);
            card.Controls.Add(layout);
            _content.Controls.Add(card);
        }

        private void BeginPage(string title)
        {
            _pageTitle.Text = title;
            _content.SuspendLayout();
            _content.Controls.Clear();
            _content.ResumeLayout();
        }

        private void RefreshAccounts()
        {
            if (_accountsGrid == null) return;
            foreach (var account in _data.Accounts) account.UiIsCurrent = account.Id == _data.CurrentAccountId;
            var term = (_searchBox?.Text ?? "").Trim();
            var items = _data.Accounts.Where(a =>
                (string.IsNullOrEmpty(_statusFilter) || a.Status == _statusFilter) &&
                (term.Length == 0 || a.Username.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || a.EffectiveGameId.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || a.Status.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            _accountsGrid.DataSource = null;
            _accountsGrid.DataSource = items;
            foreach (DataGridViewRow row in _accountsGrid.Rows)
            {
                var account = row.DataBoundItem as AccountRecord;
                if (account == null || !account.UiIsCurrent) continue;
                row.DefaultCellStyle.BackColor = Color.FromArgb(246, 255, 237);
                row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(246, 255, 237);
                row.DefaultCellStyle.SelectionForeColor = Color.FromArgb(38, 38, 38);
                row.DefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            }
            SetStat("stat_total", _data.Accounts.Count);
            SetStat("stat_normal", _data.Accounts.Count(a => a.Status == "正常"));
            SetStat("stat_temp", _data.Accounts.Count(a => a.Status == "临时封禁"));
            SetStat("stat_permanent", _data.Accounts.Count(a => a.Status == "永久封禁"));
            UpdateStatCardSelection();
        }

        private void SetStat(string name, int value)
        {
            var controls = _content.Controls.Find(name, true);
            if (controls.Length > 0) controls[0].Text = value.ToString();
        }

        private List<AccountRecord> SelectedAccounts()
        {
            var checkedAccounts = _data.Accounts.Where(a => a.UiSelected).ToList();
            if (checkedAccounts.Count > 0) return checkedAccounts;
            var current = _accountsGrid?.CurrentRow?.DataBoundItem as AccountRecord;
            return current == null ? new List<AccountRecord>() : new List<AccountRecord> { current };
        }

        private void SyncCurrentAccountFromSteam()
        {
            var username = SteamService.FindMostRecentAccountName(_data.Settings.SteamPath);
            if (string.IsNullOrWhiteSpace(username)) return;
            var account = _data.Accounts.FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
            if (account != null) _data.CurrentAccountId = account.Id;
        }

        private async Task LoginAccountAsync(AccountRecord account)
        {
            if (MessageBox.Show("切换账号会关闭当前 Steam。是否继续？", "登录确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                await _steam.LaunchAsync(_data.Settings.SteamPath, account.Username, DataStore.Decrypt(account.EncryptedPassword), _data.Settings, _operation.Token);
                account.LastLoginAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                _data.CurrentAccountId = account.Id;
                SaveData(); RefreshAccounts();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void QueryChecked()
        {
            var accounts = _data.Accounts.Where(a => a.UiSelected).ToList();
            if (accounts.Count == 0) { MessageBox.Show("请先勾选需要批量查询的账号。"); return; }
            RunPubgPlusQuery(accounts);
        }

        private void RunPubgPlusQuery(List<AccountRecord> accounts)
        {
            using (var dialog = new PubgPlusQueryForm(accounts.Select(a => a.EffectiveGameId)))
            {
                dialog.ResultReceived += result =>
                {
                    foreach (var account in accounts.Where(a => string.Equals(a.EffectiveGameId, result.GameId, StringComparison.OrdinalIgnoreCase)))
                    {
                        account.Status = result.Status;
                        account.RawStatus = result.RawStatus;
                        account.Level = result.Level;
                        account.LastQueryAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    }
                    SaveData();
                    RefreshAccounts();
                };
                dialog.ShowDialog(this);
            }
        }

        private void DeleteSelected()
        {
            var accounts = SelectedAccounts();
            if (accounts.Count == 0) return;
            var message = accounts.Count == 1 ? "确定删除账号 “" + accounts[0].Username + "” 吗？" : "确定删除选中的 " + accounts.Count + " 个账号吗？";
            if (MessageBox.Show(message, "删除账号", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            foreach (var account in accounts)
            {
                if (_data.CurrentAccountId == account.Id) _data.CurrentAccountId = "";
                _data.Accounts.Remove(account);
            }
            SaveData(); RefreshAccounts();
        }

        private void DeleteAccount(AccountRecord account)
        {
            if (MessageBox.Show("确定删除账号 “" + account.Username + "” 吗？", "删除账号", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (_data.CurrentAccountId == account.Id) _data.CurrentAccountId = "";
            _data.Accounts.Remove(account);
            SaveData();
            RefreshAccounts();
        }

        private void EditAccount(AccountRecord account)
        {
            using (var dialog = new AccountEditDialog(account))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (_data.Accounts.Any(a => a != account && string.Equals(a.Username, dialog.Username, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("这个 Steam 账号已经存在。");
                    return;
                }
                account.Username = dialog.Username;
                if (dialog.Password.Length > 0) account.EncryptedPassword = DataStore.Encrypt(dialog.Password);
                account.Email = dialog.Email;
                if (dialog.EmailPassword.Length > 0) account.EncryptedEmailPassword = DataStore.Encrypt(dialog.EmailPassword);
                account.GameId = dialog.GameId;
                account.Note = dialog.Note;
                SaveData(); RefreshAccounts();
            }
        }

        private void ParsePreview()
        {
            _preview = ImportParser.Parse(_importText.Text);
            foreach (var item in _preview) if (item.IsValid) item.Error = "可导入";
            _previewGrid.DataSource = null; _previewGrid.DataSource = _preview;
            var valid = _preview.Count(item => item.IsValid || item.Error == "可导入");
            if (_previewCount != null) _previewCount.Text = "解析数量：" + _preview.Count + " 条（可导入 " + valid + " 条）";
        }

        private void CommitImport()
        {
            _preview = ImportParser.Parse(_importText.Text);
            var existing = new HashSet<string>(_data.Accounts.Select(a => a.Username), StringComparer.OrdinalIgnoreCase);
            var added = 0; var skipped = 0;
            foreach (var item in _preview)
            {
                if (!item.IsValid || !existing.Add(item.Username)) { skipped++; continue; }
                _data.Accounts.Add(new AccountRecord { Username = item.Username, EncryptedPassword = DataStore.Encrypt(item.Password), Email = item.Email, EncryptedEmailPassword = DataStore.Encrypt(item.EmailPassword), GameId = item.GameId });
                added++;
            }
            SaveData();
            MessageBox.Show("成功导入 " + added + " 个账号，跳过 " + skipped + " 条。", "导入完成");
        }

        private void BrowseSteam()
        {
            using (var dialog = new OpenFileDialog { Filter = "Steam (steam.exe)|steam.exe|可执行文件 (*.exe)|*.exe" })
                if (dialog.ShowDialog() == DialogResult.OK) _steamPath.Text = dialog.FileName;
        }

        private void SaveSettings()
        {
            _data.Settings.SteamPath = _steamPath.Text.Trim();
            _data.Settings.StartupDelaySeconds = (int)_startupDelay.Value;
            _data.Settings.CloseRecommendations = _closeRecommendations.Checked;
            _data.Settings.CloseFriendsList = _closeFriends.Checked;
            _data.Settings.OpenLibrary = _openLibrary.Checked;
            SaveData(); MessageBox.Show("设置已保存。", "保存成功");
        }

        private void SaveData(bool showError = true)
        {
            try { _store.Save(_data); }
            catch (Exception ex) { if (showError) MessageBox.Show("保存失败：" + ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private Button ActionButton(string text, Action action, bool primary)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 34, MinimumSize = new Size(92, 34), Margin = new Padding(0, 5, 10, 5), FlatStyle = FlatStyle.Flat, BackColor = primary ? _blue : Color.White, ForeColor = primary ? Color.White : _navy, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = primary ? _blue : Color.FromArgb(211, 220, 232);
            button.Click += (_, __) => action();
            return button;
        }

        private DataGridView CreateGrid(bool allowCheckEditing = false)
        {
            return new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = !allowCheckEditing,
                EditMode = DataGridViewEditMode.EditOnEnter,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                GridColor = Color.FromArgb(240, 240, 240),
                RowTemplate = { Height = 46 },
                ColumnHeadersHeight = 44,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                EnableHeadersVisualStyles = false,
                ColumnHeadersDefaultCellStyle =
                {
                    BackColor = Color.FromArgb(250, 250, 250),
                    SelectionBackColor = Color.FromArgb(250, 250, 250),
                    ForeColor = Color.FromArgb(89, 89, 89),
                    SelectionForeColor = Color.FromArgb(89, 89, 89),
                    Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                    Padding = new Padding(5, 0, 5, 0)
                },
                DefaultCellStyle =
                {
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(38, 38, 38),
                    SelectionBackColor = Color.White,
                    SelectionForeColor = Color.FromArgb(38, 38, 38),
                    Padding = new Padding(5, 0, 5, 0)
                }
            };
        }

        private static void AddActionColumn(DataGridView grid, string name, string text, bool danger = false) => grid.Columns.Add(new DataGridViewButtonColumn
        {
            Name = name,
            HeaderText = text,
            Text = text,
            UseColumnTextForButtonValue = true,
            Width = 64,
            ReadOnly = true,
            FlatStyle = FlatStyle.Flat,
            DefaultCellStyle =
            {
                BackColor = Color.White,
                ForeColor = danger ? Color.FromArgb(220, 53, 69) : Color.FromArgb(19, 28, 46),
                SelectionBackColor = Color.White,
                SelectionForeColor = danger ? Color.FromArgb(220, 53, 69) : Color.FromArgb(19, 28, 46),
                Padding = new Padding(5, 7, 5, 7)
            }
        });

        private static void AddColumn(DataGridView grid, string header, string property, int width) => grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = header, DataPropertyName = property, MinimumWidth = width, ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells, SortMode = DataGridViewColumnSortMode.NotSortable });
        private Label FieldLabel(string text) => new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = _navy, Font = new Font(Font, FontStyle.Bold) };
        private CheckBox Option(string text, bool value) => new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 15, 0, 0), ForeColor = _navy };
    }
}
