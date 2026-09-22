using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
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
        private readonly Color _surface = Color.White;
        private readonly Color _border = Color.FromArgb(224, 230, 239);
        private readonly Color _muted = Color.FromArgb(102, 116, 136);
        private readonly DataStore _store = new DataStore();
        private readonly SteamService _steam = new SteamService();
        private AppData _data;
        private readonly Panel _content = new Panel();
        private readonly Label _pageTitle = new Label();
        private DataGridView _accountsGrid;
        private DataGridView _actionsGrid;
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
        private readonly Dictionary<int, Image> _tierIcons = new Dictionary<int, Image>();
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
            FormClosed += (_, __) =>
            {
                foreach (var image in _tierIcons.Values) image.Dispose();
                _tierIcons.Clear();
            };
        }

        private void BuildShell()
        {
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 224, BackColor = _navy, Padding = new Padding(16, 20, 16, 18) };
            var brand = new Panel { Height = 64, Dock = DockStyle.Top, BackColor = _navy, Padding = new Padding(42, 0, 0, 0) };
            Image brandLogo = null;
            try { brandLogo = Icon?.ToBitmap(); } catch { }
            var brandTitle = new Label { Text = "Steam切换器", ForeColor = Color.White, Font = new Font(Font.FontFamily, 15, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            brand.Controls.Add(brandTitle);
            brand.Paint += (_, e) =>
            {
                if (brandLogo != null)
                {
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.DrawImage(brandLogo, new Rectangle(0, 18, 28, 28));
                }
                else using (var brush = new SolidBrush(_blue)) e.Graphics.FillEllipse(brush, 0, 20, 24, 24);
            };
            brand.Disposed += (_, __) => brandLogo?.Dispose();
            sidebar.Controls.Add(NavButton("设置", NavIcon.Settings, ShowSettingsPage));
            sidebar.Controls.Add(NavButton("批量导入", NavIcon.Import, ShowImportPage));
            sidebar.Controls.Add(NavButton("账号管理", NavIcon.Accounts, ShowAccountsPage));
            sidebar.Controls.Add(brand);

            var header = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = _surface, Padding = new Padding(30, 0, 28, 0) };
            var pageIdentity = new Panel { Dock = DockStyle.Left, Width = 520 };
            _pageTitle.Dock = DockStyle.Top;
            _pageTitle.AutoSize = false;
            _pageTitle.Height = 46;
            _pageTitle.TextAlign = ContentAlignment.BottomLeft;
            _pageTitle.Font = new Font(Font.FontFamily, 18, FontStyle.Bold);
            _pageTitle.ForeColor = _navy;
            var pageSubtitle = new Label { Text = "管理登录凭据、查询状态与启动偏好", Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.TopLeft, ForeColor = _muted, Font = new Font(Font.FontFamily, 8.5F) };
            pageIdentity.Controls.Add(pageSubtitle);
            pageIdentity.Controls.Add(_pageTitle);
            var portable = new Label { Text = "本机安全存储\r\n升级自动保留数据", Dock = DockStyle.Right, Width = 190, TextAlign = ContentAlignment.MiddleRight, ForeColor = Color.FromArgb(72, 91, 116), Font = new Font(Font.FontFamily, 8.5F) };
            portable.Paint += (_, e) => { using (var brush = new SolidBrush(Color.FromArgb(35, 171, 112))) e.Graphics.FillEllipse(brush, portable.Width - 86, 30, 7, 7); };
            header.Controls.Add(portable);
            header.Controls.Add(pageIdentity);

            _content.Dock = DockStyle.Fill;
            _content.Padding = new Padding(30, 24, 30, 28);
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
            UiStyle.Round(button, 8);
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
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 3, 0, 0) };
            var searchLabel = new Label { Text = "筛选账号", AutoSize = true, Margin = new Padding(0, 13, 10, 0), ForeColor = _muted, Font = new Font(Font, FontStyle.Bold) };
            var searchHost = new Panel { Width = 250, Height = 34, Margin = new Padding(0, 7, 14, 0), BackColor = Color.White, Padding = new Padding(10, 7, 10, 5) };
            _searchBox = new TextBox { Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 10), BorderStyle = BorderStyle.None, BackColor = Color.White };
            _searchBox.TextChanged += (_, __) => RefreshAccounts();
            searchHost.Controls.Add(_searchBox);
            searchHost.Paint += (_, e) => UiStyle.DrawRoundedBorder(e.Graphics, new Rectangle(0, 0, searchHost.Width - 1, searchHost.Height - 1), 8, _border);
            UiStyle.Round(searchHost, 8);
            toolbar.Controls.Add(searchLabel);
            toolbar.Controls.Add(searchHost);
            toolbar.Controls.Add(ActionButton("批量查询", QueryChecked, false));
            toolbar.Controls.Add(ActionButton("批量删除", DeleteSelected, false));

            _accountsGrid = CreateGrid(true);
            _accountsGrid.ScrollBars = ScrollBars.Horizontal;
            _accountsGrid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "选择", DataPropertyName = "UiSelected", Width = 48, ReadOnly = false });
            AddColumn(_accountsGrid, "账号", "Username", 125);
            AddColumn(_accountsGrid, "查询ID", "EffectiveGameId", 125);
            _accountsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "TierLevel",
                HeaderText = "等级",
                DataPropertyName = "LevelText",
                MinimumWidth = 125,
                Width = 125,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _accountsGrid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "BanStatus",
                HeaderText = "封禁状态",
                DataPropertyName = "Status",
                MinimumWidth = 105,
                Width = 105,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            AddColumn(_accountsGrid, "最后登录", "LastLoginText", 145);
            AddColumn(_accountsGrid, "最后查询", "LastQueryText", 145);
            AddColumn(_accountsGrid, "备注", "Note", 100);
            _accountsGrid.CellPainting += PaintTierLevelCell;
            _accountsGrid.CellPainting += PaintBanStatusCell;
            _accountsGrid.CurrentCellDirtyStateChanged += (_, __) =>
            {
                if (_accountsGrid.IsCurrentCellDirty) _accountsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _actionsGrid = CreateGrid();
            _actionsGrid.Dock = DockStyle.Right;
            _actionsGrid.Width = 273;
            _actionsGrid.ScrollBars = ScrollBars.Vertical;
            AddActionColumn(_actionsGrid, "LoginAction", "登录");
            AddActionColumn(_actionsGrid, "QueryAction", "查询");
            AddActionColumn(_actionsGrid, "EditAction", "编辑");
            AddActionColumn(_actionsGrid, "DeleteAction", "删除", true);
            _actionsGrid.CellContentClick += async (_, e) =>
            {
                if (e.RowIndex < 0) return;
                var action = _actionsGrid.Columns[e.ColumnIndex].Name;
                if (action != "LoginAction" && action != "QueryAction" && action != "EditAction" && action != "DeleteAction") return;
                var account = _actionsGrid.Rows[e.RowIndex].DataBoundItem as AccountRecord;
                if (account == null) return;
                if (action == "LoginAction") { await LoginAccountAsync(account); return; }
                if (action == "EditAction") { EditAccount(account); return; }
                if (action == "DeleteAction") { DeleteAccount(account); return; }
                RunPubgPlusQuery(new List<AccountRecord> { account });
            };
            _accountsGrid.Scroll += (_, e) => SyncGridScroll(_accountsGrid, _actionsGrid, e.ScrollOrientation);
            _actionsGrid.Scroll += (_, e) => SyncGridScroll(_actionsGrid, _accountsGrid, e.ScrollOrientation);
            _accountsGrid.MouseWheel += (_, e) => ScrollAccountRows(e.Delta > 0 ? -3 : 3);
            var gridHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            gridHost.Controls.Add(_accountsGrid);
            gridHost.Controls.Add(_actionsGrid);
            _actionsGrid.BringToFront();
            _content.Controls.Add(gridHost);
            _content.Controls.Add(toolbar);
            _content.Controls.Add(CreateSummaryPanel());
            RefreshAccounts();
        }

        private Control CreateSummaryPanel()
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 106, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 0, 0, 12) };
            panel.Controls.Add(StatCard("stat_total", "账号总数", _data.Accounts.Count.ToString(), _navy, ""));
            panel.Controls.Add(StatCard("stat_normal", "正常", _data.Accounts.Count(a => a.Status == "正常").ToString(), Color.FromArgb(14, 159, 110), "正常"));
            panel.Controls.Add(StatCard("stat_temp", "临时封禁", _data.Accounts.Count(a => a.Status == "临时封禁").ToString(), Color.FromArgb(220, 53, 69), "临时封禁"));
            panel.Controls.Add(StatCard("stat_permanent", "永久封禁", _data.Accounts.Count(a => a.Status == "永久封禁").ToString(), Color.FromArgb(220, 53, 69), "永久封禁"));
            return panel;
        }

        private Control StatCard(string name, string title, string value, Color valueColor, string filter)
        {
            var card = new Panel { Name = name + "_card", Width = 178, Height = 88, BackColor = _surface, Margin = new Padding(0, 0, 14, 16), Padding = new Padding(16, 12, 16, 8), Cursor = Cursors.Hand, Tag = filter };
            card.Paint += (_, e) =>
            {
                var selected = string.Equals(Convert.ToString(card.Tag), _statusFilter, StringComparison.Ordinal);
                UiStyle.DrawRoundedBorder(e.Graphics, new Rectangle(0, 0, card.Width - 1, card.Height - 1), 10, selected ? _blue : _border);
                using (var brush = new SolidBrush(valueColor)) e.Graphics.FillRectangle(brush, 0, 8, 4, card.Height - 16);
            };
            UiStyle.Round(card, 10);
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
                card.Invalidate();
            }
        }

        private void ShowImportPage()
        {
            BeginPage("批量导入");
            var hint = new Label { Dock = DockStyle.Top, Height = 50, Text = "每行一个账号，支持“账号xxx密码xxx”或使用 -- / --- / ---- 分隔的2段、4段、5段格式。没有游戏ID时自动使用Steam账号名。", ForeColor = _muted, Padding = new Padding(0, 2, 0, 0) };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 220, IsSplitterFixed = false, BackColor = _background };
            _importText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, Font = new Font(Font.FontFamily, 10), BorderStyle = BorderStyle.None };
            var inputHost = new Panel { Dock = DockStyle.Fill, BackColor = _surface, Padding = new Padding(10) };
            var placeholder = new Label
            {
                Text = "示例：fxols54967--sltm34244M\r\n或：账号fxols54967密码sltm34244M",
                AutoSize = false,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(140, 150, 164),
                Font = new Font(Font.FontFamily, 9.5F),
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
            Action layoutPlaceholder = () => placeholder.SetBounds(18, 14, Math.Max(40, inputHost.ClientSize.Width - 36), 44);
            inputHost.Resize += (_, __) => layoutPlaceholder();
            inputHost.Paint += (_, e) => UiStyle.DrawRoundedBorder(e.Graphics, new Rectangle(0, 0, inputHost.Width - 1, inputHost.Height - 1), 10, _border);
            UiStyle.Round(inputHost, 10);
            layoutPlaceholder();
            var importActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, BackColor = _background, Padding = new Padding(0, 5, 0, 0) };
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
            var card = new Panel { Dock = DockStyle.Top, Height = 430, BackColor = _surface, Padding = new Padding(28) };
            card.Paint += (_, e) => UiStyle.DrawRoundedBorder(e.Graphics, new Rectangle(0, 0, card.Width - 1, card.Height - 1), 12, _border);
            UiStyle.Round(card, 12);
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
            var note = new Label { Text = "登录方式：完全退出旧 Steam 后，通过 steam.exe -login 启动。Steam Guard 和验证码仍由 Steam 官方窗口处理。", ForeColor = _muted, AutoSize = true, Margin = new Padding(0, 15, 0, 0) };
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
            if (_actionsGrid != null)
            {
                _actionsGrid.DataSource = null;
                _actionsGrid.DataSource = items;
            }
            foreach (DataGridViewRow row in _accountsGrid.Rows)
            {
                var account = row.DataBoundItem as AccountRecord;
                if (account == null || !account.UiIsCurrent) continue;
                ApplyCurrentAccountStyle(row);
            }
            if (_actionsGrid != null)
            {
                foreach (DataGridViewRow row in _actionsGrid.Rows)
                {
                    var account = row.DataBoundItem as AccountRecord;
                    if (account == null || !account.UiIsCurrent) continue;
                    ApplyCurrentAccountStyle(row);
                }
            }
            SetStat("stat_total", _data.Accounts.Count);
            SetStat("stat_normal", _data.Accounts.Count(a => a.Status == "正常"));
            SetStat("stat_temp", _data.Accounts.Count(a => a.Status == "临时封禁"));
            SetStat("stat_permanent", _data.Accounts.Count(a => a.Status == "永久封禁"));
            UpdateStatCardSelection();
        }

        private void ApplyCurrentAccountStyle(DataGridViewRow row)
        {
            row.DefaultCellStyle.BackColor = Color.FromArgb(246, 255, 237);
            row.DefaultCellStyle.SelectionBackColor = Color.FromArgb(246, 255, 237);
            row.DefaultCellStyle.SelectionForeColor = Color.FromArgb(38, 38, 38);
            row.DefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
        }

        private bool _syncingGridScroll;

        private void SyncGridScroll(DataGridView source, DataGridView target, ScrollOrientation orientation)
        {
            if (_syncingGridScroll || orientation != ScrollOrientation.VerticalScroll || source == null || target == null || source.RowCount == 0 || target.RowCount == 0) return;
            var first = source.FirstDisplayedScrollingRowIndex;
            if (first < 0 || first >= target.RowCount) return;
            try
            {
                _syncingGridScroll = true;
                target.FirstDisplayedScrollingRowIndex = first;
            }
            finally { _syncingGridScroll = false; }
        }

        private void ScrollAccountRows(int offset)
        {
            if (_accountsGrid == null || _actionsGrid == null || _accountsGrid.RowCount == 0) return;
            var current = Math.Max(0, _accountsGrid.FirstDisplayedScrollingRowIndex);
            var target = Math.Max(0, Math.Min(_accountsGrid.RowCount - 1, current + offset));
            try
            {
                _syncingGridScroll = true;
                _accountsGrid.FirstDisplayedScrollingRowIndex = target;
                _actionsGrid.FirstDisplayedScrollingRowIndex = target;
            }
            finally { _syncingGridScroll = false; }
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
            if (account == null)
            {
                account = new AccountRecord { Username = username, GameId = username, Note = "从本机 Steam 自动识别" };
                _data.Accounts.Insert(0, account);
            }
            _data.CurrentAccountId = account.Id;
            SaveData(false);
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
                        account.Tier = result.Tier > 0 ? (int?)result.Tier : null;
                        account.LastQueryAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    }
                    SaveData();
                    RefreshAccounts();
                };
                dialog.ShowDialog(this);
            }
        }

        private void PaintTierLevelCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _accountsGrid.Columns[e.ColumnIndex].Name != "TierLevel") return;
            var account = _accountsGrid.Rows[e.RowIndex].DataBoundItem as AccountRecord;
            if (account == null) return;

            e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border | DataGridViewPaintParts.SelectionBackground);
            var selected = (_accountsGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].State & DataGridViewElementStates.Selected) != 0;
            var textColor = selected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;
            var icon = account.Tier.HasValue ? GetTierIcon(account.Tier.Value) : null;
            var left = e.CellBounds.Left + 8;
            if (icon != null)
            {
                var iconSize = Math.Min(28, e.CellBounds.Height - 10);
                e.Graphics.DrawImage(icon, new Rectangle(left, e.CellBounds.Top + (e.CellBounds.Height - iconSize) / 2, iconSize, iconSize));
                left += iconSize + 5;
            }
            var textBounds = new Rectangle(left, e.CellBounds.Top, Math.Max(1, e.CellBounds.Right - left - 4), e.CellBounds.Height);
            TextRenderer.DrawText(e.Graphics, account.LevelText, e.CellStyle.Font ?? Font, textBounds, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            e.Handled = true;
        }

        private Image GetTierIcon(int tier)
        {
            Image cached;
            if (_tierIcons.TryGetValue(tier, out cached)) return cached;
            if (tier < 1 || tier > 5) return null;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SteamLoginLite.Assets.tier" + tier + ".png"))
            {
                if (stream == null) return null;
                using (var source = Image.FromStream(stream)) cached = new Bitmap(source);
            }
            _tierIcons[tier] = cached;
            return cached;
        }

        private void PaintBanStatusCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || _accountsGrid.Columns[e.ColumnIndex].Name != "BanStatus") return;
            var account = _accountsGrid.Rows[e.RowIndex].DataBoundItem as AccountRecord;
            if (account == null) return;

            e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border | DataGridViewPaintParts.SelectionBackground);
            var status = account.Status ?? "";
            Color accent;
            if (status == "正常") accent = Color.FromArgb(46, 213, 115);
            else if (status == "临时封禁") accent = Color.FromArgb(255, 165, 2);
            else if (status == "永久封禁") accent = Color.FromArgb(255, 71, 87);
            else accent = Color.FromArgb(99, 115, 136);

            using (var statusFont = new Font(e.CellStyle.Font ?? Font, FontStyle.Bold))
            {
                var measured = TextRenderer.MeasureText(status, statusFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                var tagWidth = Math.Min(e.CellBounds.Width - 12, measured.Width + 18);
                var tagHeight = Math.Min(26, e.CellBounds.Height - 10);
                var tagBounds = new Rectangle(e.CellBounds.Left + 6, e.CellBounds.Top + (e.CellBounds.Height - tagHeight) / 2, tagWidth, tagHeight);
                var previousMode = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = UiStyle.RoundedPath(tagBounds, 4))
                using (var fill = new SolidBrush(Color.FromArgb(51, accent)))
                using (var border = new Pen(Color.FromArgb(128, accent)))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                }
                e.Graphics.SmoothingMode = previousMode;
                TextRenderer.DrawText(e.Graphics, status, statusFont, tagBounds, accent,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
            e.Handled = true;
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
            var normal = primary ? _blue : _surface;
            var hover = primary ? Color.FromArgb(56, 82, 210) : Color.FromArgb(245, 248, 252);
            var button = new Button { Text = text, AutoSize = true, Height = 36, MinimumSize = new Size(92, 36), Margin = new Padding(0, 5, 10, 5), FlatStyle = FlatStyle.Flat, BackColor = normal, ForeColor = primary ? Color.White : _navy, Cursor = Cursors.Hand, Padding = new Padding(12, 0, 12, 0) };
            button.FlatAppearance.BorderColor = primary ? _blue : _border;
            button.FlatAppearance.MouseOverBackColor = hover;
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(45, 68, 176) : Color.FromArgb(235, 240, 247);
            UiStyle.Round(button, 8);
            button.Click += (_, __) => action();
            return button;
        }

        private DataGridView CreateGrid(bool allowCheckEditing = false)
        {
            var grid = new DataGridView
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
                ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
                GridColor = Color.FromArgb(232, 236, 243),
                AlternatingRowsDefaultCellStyle = { BackColor = Color.FromArgb(251, 252, 254) },
                RowTemplate = { Height = 46 },
                ColumnHeadersHeight = 44,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                EnableHeadersVisualStyles = false,
                ColumnHeadersDefaultCellStyle =
                {
                    BackColor = Color.FromArgb(247, 249, 252),
                    SelectionBackColor = Color.FromArgb(247, 249, 252),
                    ForeColor = Color.FromArgb(65, 78, 98),
                    SelectionForeColor = Color.FromArgb(65, 78, 98),
                    Font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold),
                    Padding = new Padding(5, 0, 5, 0)
                },
                DefaultCellStyle =
                {
                    BackColor = Color.White,
                    ForeColor = Color.FromArgb(38, 38, 38),
                    SelectionBackColor = Color.FromArgb(239, 245, 255),
                    SelectionForeColor = Color.FromArgb(38, 38, 38),
                    Padding = new Padding(5, 0, 5, 0)
                }
            };
            EnableGridCopy(grid);
            UiStyle.Round(grid, 10);
            return grid;
        }

        private static void EnableGridCopy(DataGridView grid)
        {
            var menu = new ContextMenuStrip();
            var copy = new ToolStripMenuItem("复制单元格内容");
            copy.Click += (_, __) => CopyCurrentCell(grid);
            menu.Items.Add(copy);
            menu.Opening += (_, __) => copy.Enabled = grid.CurrentCell != null && grid.CurrentCell.FormattedValue != null;
            grid.ContextMenuStrip = menu;
            grid.CellMouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && e.ColumnIndex >= 0) grid.CurrentCell = grid[e.ColumnIndex, e.RowIndex];
            };
            grid.KeyDown += (_, e) =>
            {
                if (!e.Control || e.KeyCode != Keys.C) return;
                CopyCurrentCell(grid);
                e.SuppressKeyPress = true;
            };
        }

        private static void CopyCurrentCell(DataGridView grid)
        {
            var value = Convert.ToString(grid.CurrentCell?.FormattedValue);
            if (string.IsNullOrEmpty(value)) return;
            try { Clipboard.SetText(value); } catch { }
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
