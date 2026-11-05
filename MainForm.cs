using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
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
        private readonly DataStore _store = new DataStore();
        private SteamService _steam;
        private Action<string, string> _steamHandler;
        private int _loginGeneration;
        private readonly bool _preview;
        private AppData _data;
        private readonly HashSet<string> _selected = new HashSet<string>();
        private readonly TextBox _search = new TextBox { Width = 230 };
        private readonly ComboBox _filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly ComboBox _pageSize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 75 };
        private readonly CheckBox _all = new CheckBox { Text = "全选筛选结果", AutoSize = true };
        private readonly Label _counts = new Label { AutoSize = true, Margin = new Padding(10, 6, 10, 0) };
        private readonly Label _selection = new Label { AutoSize = true, Margin = new Padding(8, 6, 8, 0) };
        private readonly Label _pages = new Label { AutoSize = true, Margin = new Padding(8, 6, 8, 0) };
        private readonly ToolStripStatusLabel _status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly DataGridView _grid = new DataGridView();
        private readonly List<Control> _mutations = new List<Control>();
        private CancellationTokenSource _login = new CancellationTokenSource();
        private bool _readOnly, _rendering, _busy;
        private int _page = 1;
        private string _sort = "";
        private bool _descending;
        internal AppData Data => _data;
        internal DataGridView Grid => _grid;
        internal int SelectionCount => _selected.Count;
        internal int FilteredCount => Filtered().Count;

        public MainForm(bool preview = false)
        {
            _preview = preview;
            Text = "Steam Login Lite · " + typeof(MainForm).Assembly.GetName().Version.ToString(3);
            ClientSize = new Size(1240, 700); MinimumSize = new Size(1060, 600);
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BuildUi();
            if (_preview) { _data = Demo(); Render(); SetStatus("界面预览 · 示例数据，不访问真实账号。正式运行请使用 Windows 绿色包。"); }
            else Reload();
            FormClosing += (_, __) => _login.Cancel();
            FormClosed += (_, __) => { if (_steam != null) _steam.PostLoginStatus -= _steamHandler; _login.Dispose(); };
        }
        private static FlowLayoutPanel Flow() => new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 2, 0, 2) };
        private Button Button(string text, Action action, bool mutation = true)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(75, 27), UseVisualStyleBackColor = true };
            button.Click += (_, __) => Guard(action);
            if (mutation) _mutations.Add(button);
            return button;
        }
        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 4), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var tools = Flow();
            tools.Controls.Add(Button("添加账号", () => Edit(null)));
            tools.Controls.Add(Button("批量导入", Import));
            var refresh = Button("刷新 / 同步本机", () => { if (!_busy) Reload(); }, false); tools.Controls.Add(refresh);
            tools.Controls.Add(Button("偏好设置", Settings)); tools.Controls.Add(_counts);
            var filters = Flow();
            filters.Controls.Add(new Label { Text = "搜索", AutoSize = true, Margin = new Padding(3, 6, 5, 0) }); filters.Controls.Add(_search);
            _filter.Items.AddRange(new object[] { "全部账号", "正常", "未查询", "临时封禁", "永久封禁" }); _filter.SelectedIndex = 0;
            filters.Controls.Add(_filter); filters.Controls.Add(new Label { Text = "可搜索账号、昵称、备注；点击表头排序。", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(10, 6, 0, 0) });
            _search.TextChanged += (_, __) => { _page = 1; Render(); }; _filter.SelectedIndexChanged += (_, __) => { _page = 1; Render(); };
            var selection = Flow(); selection.Controls.Add(_all); selection.Controls.Add(_selection);
            selection.Controls.Add(Button("选择本页", () => { foreach (var account in PageAccounts()) _selected.Add(account.Id); Render(); }));
            selection.Controls.Add(Button("取消选择", () => { _selected.Clear(); Render(); }));
            selection.Controls.Add(Button("批量查询", () => Query(Checked()))); selection.Controls.Add(Button("删除所选", () => Delete(Checked())));
            _all.CheckedChanged += (_, __) => { if (_rendering) return; foreach (var account in Filtered()) { if (_all.Checked) _selected.Add(account.Id); else _selected.Remove(account.Id); } Render(); };
            _grid.Dock = DockStyle.Fill; _grid.AutoGenerateColumns = false; _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false;
            _grid.AllowUserToResizeRows = false; _grid.RowHeadersVisible = false; _grid.MultiSelect = false; _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.BackgroundColor = SystemColors.Window; _grid.BorderStyle = BorderStyle.FixedSingle; _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _grid.RowTemplate.Height = 32; _grid.ColumnHeadersHeight = 32; _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False; _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(223, 235, 252); _grid.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Select", HeaderText = "选", Width = 35, SortMode = DataGridViewColumnSortMode.NotSortable });
            AddColumn("Username", "Steam 账号", 160); AddColumn("Source", "标记", 80); AddColumn("GameId", "游戏昵称 / 查询标识", 165);
            AddColumn("Level", "生存等级", 120); AddColumn("Status", "账号状态", 90); AddColumn("Note", "备注", 155); AddColumn("Time", "最近查询", 115);
            foreach (var action in new[] { new[] { "Login", "登录" }, new[] { "Query", "查询" }, new[] { "Edit", "编辑" } })
                _grid.Columns.Add(new DataGridViewButtonColumn { Name = action[0], HeaderText = "", Text = action[1], UseColumnTextForButtonValue = true, Width = 55, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            _grid.CurrentCellDirtyStateChanged += (_, __) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _grid.CellValueChanged += (_, e) => {
                if (_rendering || e.RowIndex < 0 || e.ColumnIndex != 0) return;
                var row = _grid.Rows[e.RowIndex]; var account = row.Tag as AccountRecord; if (account == null) return;
                if (Convert.ToBoolean(row.Cells[0].Value)) _selected.Add(account.Id); else _selected.Remove(account.Id); UpdateSelection();
            };
            _grid.CellContentClick += async (_, e) => {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || _busy || _readOnly) return;
                var account = _grid.Rows[e.RowIndex].Tag as AccountRecord; if (account == null) return;
                var name = _grid.Columns[e.ColumnIndex].Name;
                if (name == "Login") await LoginAsync(account);
                else if (name == "Query") Guard(() => Query(new List<AccountRecord> { account }));
                else if (name == "Edit") Guard(() => Edit(account));
            };
            _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex > 0 && _grid.Columns[e.ColumnIndex] is DataGridViewTextBoxColumn && !_busy && !_readOnly) Guard(() => Edit(_grid.Rows[e.RowIndex].Tag as AccountRecord)); };
            _grid.ColumnHeaderMouseClick += (_, e) => { var key = _grid.Columns[e.ColumnIndex].Name; if (!new[] { "Username", "Level", "Time" }.Contains(key)) return; _descending = _sort == key ? !_descending : key != "Username"; _sort = key; _page = 1; Render(); };
            var context = new ContextMenuStrip();
            context.Items.Add("复制账号", null, (_, __) => Copy(Current()?.Username)); context.Items.Add("复制查询标识", null, (_, __) => Copy(Current()?.EffectiveGameId));
            context.Items.Add("编辑", null, (_, __) => Guard(() => { if (!_busy && !_readOnly && Current() != null) Edit(Current()); }));
            context.Items.Add("删除此账号", null, (_, __) => Guard(() => { if (!_busy && !_readOnly && Current() != null) Delete(new List<AccountRecord> { Current() }); }));
            _grid.ContextMenuStrip = context;
            _grid.CellMouseDown += (_, e) => { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[Math.Max(1, e.ColumnIndex)]; };
            var paging = Flow();
            paging.Controls.Add(Button("上一页", () => { if (_page > 1) _page--; Render(); }, false));
            paging.Controls.Add(Button("下一页", () => { if (_page * PageSize < FilteredCount) _page++; Render(); }, false));
            paging.Controls.Add(_pages); _pageSize.Items.AddRange(new object[] { 20, 50, 100 }); _pageSize.SelectedIndex = 0; paging.Controls.Add(_pageSize);
            paging.Controls.Add(new Label { Text = "条 / 页", AutoSize = true, Margin = new Padding(3, 6, 0, 0) });
            _pageSize.SelectedIndexChanged += (_, __) => { _page = 1; Render(); };
            root.Controls.Add(tools, 0, 0); root.Controls.Add(filters, 0, 1); root.Controls.Add(selection, 0, 2); root.Controls.Add(_grid, 0, 3); root.Controls.Add(paging, 0, 4);
            var status = new StatusStrip(); status.Items.Add(_status); Controls.Add(root); Controls.Add(status);
        }
        private void AddColumn(string name, string title, int width) => _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, Width = width, ReadOnly = true, SortMode = name == "Username" || name == "Level" || name == "Time" ? DataGridViewColumnSortMode.Programmatic : DataGridViewColumnSortMode.NotSortable });
        private int PageSize => _pageSize.SelectedItem is int value ? value : 20;
        private AccountRecord Current() => _grid.CurrentRow?.Tag as AccountRecord;
        private List<AccountRecord> Checked() => _data.Accounts.Where(a => _selected.Contains(a.Id)).ToList();
        private List<AccountRecord> Filtered()
        {
            if (_data == null) return new List<AccountRecord>();
            var term = _search.Text.Trim(); var status = Convert.ToString(_filter.SelectedItem);
            var accounts = _data.Accounts.Where(a => (status == "全部账号" || a.Status == status) && new[] { a.Username, a.EffectiveGameId, a.Note, a.Email }.Any(s => (s ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
            if (_sort == "Username") accounts = _descending ? accounts.OrderByDescending(a => a.Username, StringComparer.OrdinalIgnoreCase) : accounts.OrderBy(a => a.Username, StringComparer.OrdinalIgnoreCase);
            else if (_sort == "Level") accounts = _descending ? accounts.OrderByDescending(a => a.Level ?? -1) : accounts.OrderBy(a => a.Level ?? -1);
            else if (_sort == "Time") accounts = _descending ? accounts.OrderByDescending(a => a.LastQueryAt) : accounts.OrderBy(a => a.LastQueryAt);
            else accounts = accounts.OrderByDescending(a => a.Id == _data.CurrentAccountId).ThenByDescending(a => a.LastLoginAt);
            return accounts.ToList();
        }
        private List<AccountRecord> PageAccounts() => Filtered().Skip((_page - 1) * PageSize).Take(PageSize).ToList();
        private void Render()
        {
            if (_data == null || IsDisposed) return;
            _rendering = true; _grid.SuspendLayout();
            try
            {
                _selected.RemoveWhere(id => !_data.Accounts.Any(a => a.Id == id));
                _page = Math.Max(1, Math.Min(_page, Math.Max(1, (FilteredCount + PageSize - 1) / PageSize)));
                _grid.Rows.Clear();
                foreach (var account in PageAccounts())
                {
                    var source = (account.Id == _data.CurrentAccountId ? "当前" : "") + (account.IsLocalAccount ? (account.Id == _data.CurrentAccountId ? " · 本机" : "本机") : "");
                    var index = _grid.Rows.Add(_selected.Contains(account.Id), account.Username, source, account.EffectiveGameId, account.Level.HasValue ? (account.Tier.HasValue ? account.Tier + " 阶 · " : "") + account.Level + " 级" : "—", account.Status, account.Note, Stamp(account.LastQueryAt));
                    var row = _grid.Rows[index]; row.Tag = account;
                    row.Cells["Source"].ToolTipText = account.IsLocalAccount ? (account.CanUseSavedLogin ? "本机记录，可尝试使用 Steam 已记住的登录状态" : "本机记录，需补充密码或在 Steam 登录") : "工具保存的账号";
                    row.Cells["Note"].ToolTipText = account.Note; row.Cells["GameId"].ToolTipText = account.EffectiveGameId;
                    if (account.Id == _data.CurrentAccountId) row.DefaultCellStyle.BackColor = Color.FromArgb(235, 248, 239);
                    row.Cells["Status"].Style.ForeColor = account.Status == "正常" ? Color.DarkGreen : account.Status == "永久封禁" ? Color.Firebrick : account.Status == "临时封禁" ? Color.DarkGoldenrod : SystemColors.GrayText;
                }
                foreach (DataGridViewColumn column in _grid.Columns) if (column.SortMode == DataGridViewColumnSortMode.Programmatic)
                    column.HeaderCell.SortGlyphDirection = column.Name == _sort ? (_descending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
                _counts.Text = "全部 " + _data.Accounts.Count + "　正常 " + _data.Accounts.Count(a => a.Status == "正常") + "　未查询 " + _data.Accounts.Count(a => a.Status == "未查询");
                _pages.Text = "第 " + _page + " / " + Math.Max(1, (FilteredCount + PageSize - 1) / PageSize) + " 页，共 " + FilteredCount + " 个账号";
            }
            finally { _grid.ResumeLayout(); _rendering = false; }
            UpdateSelection(); SetEnabled();
        }
        private void UpdateSelection()
        {
            _rendering = true; var accounts = Filtered();
            _all.Checked = accounts.Count > 0 && accounts.All(a => _selected.Contains(a.Id));
            _selection.Text = "已选 " + _selected.Count + " 个"; _rendering = false;
        }
        private static string Stamp(long value) { if (value <= 0) return "—"; try { return DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture); } catch { return "—"; } }
        private void SetStatus(string text) { if (!IsDisposed) _status.Text = text; }
        private void SetEnabled() { foreach (var control in _mutations) control.Enabled = !_busy && !_readOnly; _grid.Enabled = !_busy; _all.Enabled = !_busy && !_readOnly; }
        private void Guard(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); SetStatus(ex.Message); } }
        private void Copy(string value) { if (!string.IsNullOrEmpty(value)) Guard(() => Clipboard.SetText(value)); }
        private void Save() { if (_preview) return; if (_readOnly) throw new InvalidOperationException("账号数据读取失败，已禁止写入，请先恢复原数据。"); _store.Save(_data); }
        private void Reload()
        {
            if (_preview) { Render(); return; }
            try { _data = _store.Load(); _readOnly = false; }
            catch (Exception ex) { _data = new AppData(); _readOnly = true; SetStatus("数据读取失败，已禁止覆盖：" + ex.Message); Render(); return; }
            if (string.IsNullOrWhiteSpace(_data.Settings.SteamPath)) _data.Settings.SteamPath = SteamService.FindSteamPath();
            _data.CurrentAccountId = "";
            try
            {
                var local = SteamLocalAccounts.Read(_data.Settings.SteamPath);
                SteamAccountSync.Merge(_data, local, SteamService.FindActiveAccountName(_data.Settings.SteamPath, local)); Save();
                SetStatus("已同步本机账号。账号数据保存在 Windows 用户目录，删除程序不会删除账号。");
            }
            catch (Exception ex) { SetStatus("本机记录未同步，已有账号已保留：" + ex.Message); }
            Render();
        }
        private void Edit(AccountRecord account)
        {
            var isNew = account == null; account = account ?? new AccountRecord();
            using (var dialog = new AccountEditDialog(account, isNew))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (!isNew) account = _data.Accounts.SingleOrDefault(a => a.Id == account.Id) ?? throw new InvalidOperationException("账号记录已变化，请重新打开编辑。");
                if (_data.Accounts.Any(a => a.Id != account.Id && a.Username.Equals(dialog.Username, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("账号已存在。");
                account.Username = dialog.Username; account.Email = dialog.Email; account.GameId = dialog.GameId; account.Note = dialog.Note;
                if (dialog.Password.Length > 0) account.EncryptedPassword = DataStore.Encrypt(dialog.Password);
                if (dialog.EmailPassword.Length > 0) account.EncryptedEmailPassword = DataStore.Encrypt(dialog.EmailPassword);
                if (isNew) _data.Accounts.Add(account); Save(); if (_preview) Render(); else Reload(); SetStatus("账号已保存。");
            }
        }
        private void Delete(List<AccountRecord> accounts)
        {
            if (accounts.Count == 0) { SetStatus("请先勾选账号。"); return; }
            if (MessageBox.Show(this, "从工具中删除 " + accounts.Count + " 个账号？本机 Steam 记录不受影响，之后刷新可能再次同步。", "删除账号", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var ids = new HashSet<string>(accounts.Select(a => a.Id)); _data.Accounts.RemoveAll(a => ids.Contains(a.Id)); _selected.ExceptWith(ids);
            if (ids.Contains(_data.CurrentAccountId)) _data.CurrentAccountId = ""; Save(); Render();
        }
        private void Import()
        {
            using (var dialog = new ImportDialog())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                var added = 0; var updated = 0; var skipped = 0;
                foreach (var row in dialog.Accounts)
                {
                    var incoming = new AccountRecord { Username = row.Username, EncryptedPassword = DataStore.Encrypt(row.Password), Email = row.Email, EncryptedEmailPassword = DataStore.Encrypt(row.EmailPassword), GameId = row.GameId };
                    var duplicate = _data.Accounts.FirstOrDefault(a => a.Username.Equals(row.Username, StringComparison.OrdinalIgnoreCase));
                    if (duplicate == null) { _data.Accounts.Add(incoming); added++; }
                    else if (SteamAccountSync.FillMissingData(duplicate, incoming)) updated++; else skipped++;
                }
                Save(); Render(); SetStatus("已新增 " + added + " 个，补全本机账号 " + updated + " 个，跳过重复账号 " + skipped + " 个。");
            }
        }
        private void Settings()
        {
            using (var dialog = new SettingsDialog(_data.Settings))
                if (dialog.ShowDialog(this) == DialogResult.OK) { _data.Settings = dialog.Settings; Save(); if (!_preview) Reload(); }
        }
        private async Task LoginAsync(AccountRecord account)
        {
            if (_preview) { SetStatus("示例账号不能登录。"); return; }
            var same = account.Id == _data.CurrentAccountId;
            var text = same ? "尝试沿用当前会话；本机状态变化时可能重新切换。" : "切换会退出 Steam 和正在运行的 PUBG，请先保存游戏进度。可能需要手动完成 Steam 验证。";
            if (MessageBox.Show(this, text, same ? "继续使用 " + account.Username : "切换到 " + account.Username, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            _busy = true; SetEnabled(); _login.Cancel(); _login.Dispose(); _login = new CancellationTokenSource();
            if (_steam != null) _steam.PostLoginStatus -= _steamHandler;
            var generation = ++_loginGeneration; _steam = new SteamService();
            _steamHandler = (state, message) => OnSteamStatus(generation, state, message); _steam.PostLoginStatus += _steamHandler;
            try
            {
                try {
                    var remembered = SteamLocalAccounts.Read(_data.Settings.SteamPath).FirstOrDefault(a => a.Username.Equals(account.Username, StringComparison.OrdinalIgnoreCase));
                    account.IsLocalAccount = remembered != null; account.CanUseSavedLogin = remembered != null && remembered.CanUseSavedLogin;
                } catch { account.IsLocalAccount = account.CanUseSavedLogin = false; }
                if (account.IsLocalAccount && (account.CanUseSavedLogin || SteamService.IsTargetSessionReady(_data.Settings.SteamPath, account.Username)))
                    await _steam.LaunchRememberedAsync(_data.Settings.SteamPath, account.Username, _data.Settings, _login.Token);
                else
                {
                    string password;
                    try { password = DataStore.Decrypt(account.EncryptedPassword); } catch { throw new InvalidOperationException("密码无法解密，请在原 Windows 用户下运行，或编辑账号重新填写密码。"); }
                    if (password.Length == 0) throw new InvalidOperationException("没有可用的本机登录状态，请编辑账号补充密码，或先在 Steam 登录并勾选记住我。");
                    await _steam.LaunchAsync(_data.Settings.SteamPath, account.Username, password, _data.Settings, _login.Token);
                }
                if (IsDisposed || _login.IsCancellationRequested) return;
                account.LastLoginAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                _data.CurrentAccountId = SteamService.IsTargetSessionReady(_data.Settings.SteamPath, account.Username) ? account.Id : ""; Save(); Render();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!IsDisposed) { SetStatus(ex.Message); MessageBox.Show(this, ex.Message, "登录未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
            finally { if (!IsDisposed) { _busy = false; SetEnabled(); } }
        }
        private void OnSteamStatus(int generation, string state, string message)
        {
            if (generation != _loginGeneration || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(() => { if (generation != _loginGeneration || IsDisposed) return; if ((state == "authenticated" || state == "completed") && !_busy) Reload(); if (!_readOnly) SetStatus(message); })); } catch { }
        }
        private void Query(List<AccountRecord> accounts)
        {
            if (_preview) { SetStatus("示例账号不能联网查询。"); return; }
            if (accounts.Count == 0) { SetStatus("请先勾选需要查询的账号。"); return; }
            var snapshot = accounts.Select(a => new QueryAccount(a.Id, a.EffectiveGameId)).ToArray();
            using (var dialog = new PubgPlusQueryForm(snapshot, _data.Settings.QueryIntervalMilliseconds))
            {
                dialog.ResultReceived += (task, result) => {
                    var account = _data.Accounts.SingleOrDefault(a => a.Id == task.Id && a.EffectiveGameId.Equals(task.GameId, StringComparison.OrdinalIgnoreCase));
                    if (account == null) throw new InvalidOperationException("账号或查询标识已变化，未写入结果。");
                    account.Status = result.Status; account.RawStatus = result.RawStatus;
                    if (result.HasLevel) { account.Level = result.Level; account.Tier = result.Tier > 0 ? (int?)result.Tier : null; }
                    account.LastQueryAt = DateTimeOffset.Now.ToUnixTimeMilliseconds(); Save(); Render();
                };
                dialog.Completed += SetStatus; dialog.ShowDialog(this);
            }
        }
        internal void SmokeReplaceData(AppData data) { _data = data; }
        internal void SmokeEditFirst() { Edit(_data.Accounts[0]); }
        internal void SmokeSelectAll() { _all.Checked = true; }
        internal void SmokeNextPage() { _page = 2; Render(); }
        internal void SmokeSearch(string term) { _search.Text = term; }
        internal void SmokeClearSelection() { _selected.Clear(); Render(); }
        private static AppData Demo()
        {
            var data = new AppData();
            for (var i = 0; i < 28; i++) data.Accounts.Add(new AccountRecord {
                Id = "demo-" + i, Username = "steam_player_" + (i + 1).ToString("00"), GameId = "PUBG_Player_" + (i + 1),
                Status = new[] { "正常", "正常", "未查询", "临时封禁", "永久封禁" }[i % 5], Level = i % 5 == 2 ? (int?)null : 42 + i * 11,
                Tier = 1 + i % 5, Note = i % 3 == 0 ? "常用账号" : "", IsLocalAccount = i < 3, CanUseSavedLogin = i < 2,
                LastQueryAt = i % 5 == 2 ? 0 : DateTimeOffset.Now.ToUnixTimeMilliseconds() - i * 3600000L
            });
            data.CurrentAccountId = "demo-0"; return data;
        }
    }
}
