using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SteamLoginLite.Models;
using SteamLoginLite.Services;

namespace SteamLoginLite
{
    public partial class MainWindow : Window
    {
        private readonly DataStore _store = new DataStore();
        private readonly SteamService _steam = new SteamService();
        private readonly CancellationTokenSource _operation = new CancellationTokenSource();
        private AppData _data;
        private string _statusFilter = "";

        public ObservableCollection<AccountRecord> Accounts { get; } = new ObservableCollection<AccountRecord>();
        public ObservableCollection<ImportedAccount> Preview { get; } = new ObservableCollection<ImportedAccount>();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            try { _data = _store.Load(); }
            catch (Exception ex)
            {
                _data = new AppData();
                MessageBox.Show(ex.Message, "数据恢复", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            if (string.IsNullOrWhiteSpace(_data.Settings.SteamPath)) _data.Settings.SteamPath = SteamService.FindSteamPath();
            SyncCurrentAccountFromSteam();
            LoadSettings();
            RefreshAccounts();
            Closed += (_, __) =>
            {
                _operation.Cancel();
                SaveData(false);
            };
        }

        private void ShowPage(FrameworkElement page, Button active, string title, string subtitle)
        {
            AccountsPage.Visibility = Visibility.Collapsed;
            ImportPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            page.Visibility = Visibility.Visible;
            AccountsNav.Tag = null;
            ImportNav.Tag = null;
            SettingsNav.Tag = null;
            active.Tag = "Active";
            PageTitle.Text = title;
            PageSubtitle.Text = subtitle;
        }

        private void AccountsNav_Click(object sender, RoutedEventArgs e)
        {
            ShowPage(AccountsPage, AccountsNav, "账号管理", "管理登录凭据、查询状态与启动偏好");
            RefreshAccounts();
        }

        private void ImportNav_Click(object sender, RoutedEventArgs e) =>
            ShowPage(ImportPage, ImportNav, "批量导入", "一次导入多个 Steam 账号，解析确认后再安全保存");

        private void SettingsNav_Click(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            ShowPage(SettingsPage, SettingsNav, "设置", "配置 Steam 启动路径与登录后的自动操作");
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            WindowBorder.CornerRadius = WindowState == WindowState.Maximized ? new CornerRadius(0) : new CornerRadius(14);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchPlaceholder != null) SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            RefreshAccounts();
        }

        private void AccountsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (LastLoginColumn == null || LastQueryColumn == null || NoteColumn == null) return;
            var width = AccountsGrid.ActualWidth;
            LastLoginColumn.Visibility = width >= 1080 ? Visibility.Visible : Visibility.Collapsed;
            LastQueryColumn.Visibility = width >= 1080 ? Visibility.Visible : Visibility.Collapsed;
            NoteColumn.Visibility = width >= 940 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void StatCard_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            _statusFilter = Convert.ToString(button.CommandParameter) ?? "";
            TotalCard.Tag = string.IsNullOrEmpty(_statusFilter) ? "Active" : null;
            NormalCard.Tag = _statusFilter == "正常" ? "Active" : null;
            TempCard.Tag = _statusFilter == "临时封禁" ? "Active" : null;
            PermanentCard.Tag = _statusFilter == "永久封禁" ? "Active" : null;
            RefreshAccounts();
        }

        private void RefreshAccounts()
        {
            if (_data == null || AccountsGrid == null) return;
            foreach (var account in _data.Accounts) account.UiIsCurrent = account.Id == _data.CurrentAccountId;
            var term = (SearchBox?.Text ?? "").Trim();
            var items = _data.Accounts.Where(account =>
                (string.IsNullOrEmpty(_statusFilter) || account.Status == _statusFilter) &&
                (term.Length == 0 || account.Username.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 account.EffectiveGameId.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 account.Status.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            Accounts.Clear();
            foreach (var account in items) Accounts.Add(account);
            TotalCount.Text = _data.Accounts.Count.ToString();
            NormalCount.Text = _data.Accounts.Count(account => account.Status == "正常").ToString();
            TempCount.Text = _data.Accounts.Count(account => account.Status == "临时封禁").ToString();
            PermanentCount.Text = _data.Accounts.Count(account => account.Status == "永久封禁").ToString();
        }

        private List<AccountRecord> SelectedAccounts()
        {
            var checkedAccounts = _data.Accounts.Where(account => account.UiSelected).ToList();
            if (checkedAccounts.Count > 0) return checkedAccounts;
            var current = AccountsGrid.CurrentItem as AccountRecord;
            return current == null ? new List<AccountRecord>() : new List<AccountRecord> { current };
        }

        private void SyncCurrentAccountFromSteam()
        {
            var username = SteamService.FindMostRecentAccountName(_data.Settings.SteamPath);
            if (string.IsNullOrWhiteSpace(username)) return;
            var account = _data.Accounts.FirstOrDefault(item => string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase));
            if (account == null)
            {
                account = new AccountRecord { Username = username, GameId = username, Note = "从本机 Steam 自动识别" };
                _data.Accounts.Insert(0, account);
            }
            _data.CurrentAccountId = account.Id;
            SaveData(false);
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            var account = ((Button)sender).Tag as AccountRecord;
            if (account == null) return;
            if (MessageBox.Show("切换账号会关闭当前 Steam。是否继续？", "登录确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try
            {
                await _steam.LaunchAsync(_data.Settings.SteamPath, account.Username, DataStore.Decrypt(account.EncryptedPassword), _data.Settings, _operation.Token);
                account.LastLoginAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                _data.CurrentAccountId = account.Id;
                SaveData();
                RefreshAccounts();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void Query_Click(object sender, RoutedEventArgs e)
        {
            var account = ((Button)sender).Tag as AccountRecord;
            if (account != null) RunPubgPlusQuery(new List<AccountRecord> { account });
        }

        private void QueryChecked_Click(object sender, RoutedEventArgs e)
        {
            var accounts = _data.Accounts.Where(account => account.UiSelected).ToList();
            if (accounts.Count == 0)
            {
                MessageBox.Show("请先勾选需要批量查询的账号。", "批量查询", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            RunPubgPlusQuery(accounts);
        }

        private void RunPubgPlusQuery(List<AccountRecord> accounts)
        {
            using (var dialog = new PubgPlusQueryForm(accounts.Select(account => account.EffectiveGameId)))
            {
                dialog.ResultReceived += result =>
                {
                    foreach (var account in accounts.Where(item => string.Equals(item.EffectiveGameId, result.GameId, StringComparison.OrdinalIgnoreCase)))
                    {
                        account.Status = result.Status;
                        account.RawStatus = result.RawStatus;
                        account.Level = result.Level;
                        account.LastQueryAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    }
                    SaveData();
                    RefreshAccounts();
                };
                dialog.ShowDialog();
            }
        }

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            var account = ((Button)sender).Tag as AccountRecord;
            if (account == null) return;
            var dialog = new AccountEditWindow(account) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            if (_data.Accounts.Any(item => item != account && string.Equals(item.Username, dialog.Username, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("这个 Steam 账号已经存在。", "无法保存", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            account.Username = dialog.Username;
            if (dialog.Password.Length > 0) account.EncryptedPassword = DataStore.Encrypt(dialog.Password);
            account.Email = dialog.Email;
            if (dialog.EmailPassword.Length > 0) account.EncryptedEmailPassword = DataStore.Encrypt(dialog.EmailPassword);
            account.GameId = dialog.GameId;
            account.Note = dialog.Note;
            SaveData();
            RefreshAccounts();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var account = ((Button)sender).Tag as AccountRecord;
            if (account != null) DeleteAccounts(new List<AccountRecord> { account });
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e) => DeleteAccounts(SelectedAccounts());

        private void DeleteAccounts(List<AccountRecord> accounts)
        {
            if (accounts.Count == 0) return;
            var text = accounts.Count == 1 ? "确定删除账号 “" + accounts[0].Username + "” 吗？" : "确定删除选中的 " + accounts.Count + " 个账号吗？";
            if (MessageBox.Show(text, "删除账号", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            foreach (var account in accounts)
            {
                if (_data.CurrentAccountId == account.Id) _data.CurrentAccountId = "";
                _data.Accounts.Remove(account);
            }
            SaveData();
            RefreshAccounts();
        }

        private void CopyValue_Click(object sender, RoutedEventArgs e)
        {
            var value = Convert.ToString(((Button)sender).Tag);
            if (!string.IsNullOrWhiteSpace(value)) Clipboard.SetText(value);
        }

        private void ImportText_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ImportPlaceholder != null) ImportPlaceholder.Visibility = string.IsNullOrEmpty(ImportText.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearImport_Click(object sender, RoutedEventArgs e)
        {
            ImportText.Clear();
            Preview.Clear();
            UpdateImportCounts();
        }

        private void ParsePreview_Click(object sender, RoutedEventArgs e)
        {
            Preview.Clear();
            foreach (var item in ImportParser.Parse(ImportText.Text))
            {
                if (item.IsValid) item.Error = "可导入";
                Preview.Add(item);
            }
            UpdateImportCounts();
        }

        private void UpdateImportCounts()
        {
            var valid = Preview.Count(item => item.Error == "可导入");
            ImportTotal.Text = "共 " + Preview.Count + " 条";
            ImportValid.Text = "可导入 " + valid + " 条";
        }

        private void CommitImport_Click(object sender, RoutedEventArgs e)
        {
            var parsed = ImportParser.Parse(ImportText.Text);
            var existing = new HashSet<string>(_data.Accounts.Select(account => account.Username), StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var skipped = 0;
            foreach (var item in parsed)
            {
                if (!item.IsValid || !existing.Add(item.Username)) { skipped++; continue; }
                _data.Accounts.Add(new AccountRecord
                {
                    Username = item.Username,
                    EncryptedPassword = DataStore.Encrypt(item.Password),
                    Email = item.Email,
                    EncryptedEmailPassword = DataStore.Encrypt(item.EmailPassword),
                    GameId = item.GameId
                });
                added++;
            }
            SaveData();
            MessageBox.Show("成功导入 " + added + " 个账号，跳过 " + skipped + " 条。", "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
            RefreshAccounts();
        }

        private void LoadSettings()
        {
            if (_data == null || SteamPathBox == null) return;
            SteamPathBox.Text = _data.Settings.SteamPath;
            StartupDelayBox.Text = _data.Settings.StartupDelaySeconds.ToString();
            CloseRecommendationsToggle.IsChecked = _data.Settings.CloseRecommendations;
            CloseFriendsToggle.IsChecked = _data.Settings.CloseFriendsList;
            OpenLibraryToggle.IsChecked = _data.Settings.OpenLibrary;
        }

        private void BrowseSteam_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Steam (steam.exe)|steam.exe|可执行文件 (*.exe)|*.exe" };
            if (dialog.ShowDialog(this) == true) SteamPathBox.Text = dialog.FileName;
        }

        private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
        {
            SteamPathBox.Text = SteamService.FindSteamPath();
            StartupDelayBox.Text = "8";
            CloseRecommendationsToggle.IsChecked = true;
            CloseFriendsToggle.IsChecked = true;
            OpenLibraryToggle.IsChecked = true;
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            int delay;
            if (!int.TryParse(StartupDelayBox.Text.Trim(), out delay) || delay < 2 || delay > 30)
            {
                MessageBox.Show("启动等待时间请输入 2 到 30 之间的整数。", "设置有误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _data.Settings.SteamPath = SteamPathBox.Text.Trim();
            _data.Settings.StartupDelaySeconds = delay;
            _data.Settings.CloseRecommendations = CloseRecommendationsToggle.IsChecked == true;
            _data.Settings.CloseFriendsList = CloseFriendsToggle.IsChecked == true;
            _data.Settings.OpenLibrary = OpenLibraryToggle.IsChecked == true;
            SaveData();
            MessageBox.Show("设置已保存。", "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveData(bool showError = true)
        {
            try { _store.Save(_data); }
            catch (Exception ex)
            {
                if (showError) MessageBox.Show("保存失败：" + ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
