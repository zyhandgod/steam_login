using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamLoginLite.Models;
using SteamLoginLite.Services;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace SteamLoginLite;

public sealed partial class MainWindow : Window
{
    private readonly DataStore _store = new();
    private readonly SteamService _steam = new();
    private readonly ObservableCollection<AccountRecord> _visibleAccounts = new();
    private readonly ObservableCollection<ImportedAccount> _previewItems = new();
    private readonly CancellationTokenSource _operation = new();
    private AppData _data;

    public MainWindow()
    {
        InitializeComponent();
        try { _data = _store.Load(); }
        catch { _data = new AppData(); }
        if (string.IsNullOrWhiteSpace(_data.Settings.SteamPath)) _data.Settings.SteamPath = SteamService.FindSteamPath();
        SyncCurrentAccountFromSteam();
        AccountsList.ItemsSource = _visibleAccounts;
        PreviewList.ItemsSource = _previewItems;
        LoadSettings();
        RefreshAccounts();
        ConfigureWindow();
    }

    private void ConfigureWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        appWindow.Resize(new SizeInt32(1360, 760));
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "SteamSwitcher.ico"));
        appWindow.Closing += (_, __) => { _operation.Cancel(); SaveData(false); };
    }

    private void ShowPage(Grid page, Button selected)
    {
        AccountsPage.Visibility = page == AccountsPage ? Visibility.Visible : Visibility.Collapsed;
        ImportPage.Visibility = page == ImportPage ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == SettingsPage ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { AccountsNav, ImportNav, SettingsNav })
        {
            var active = button == selected;
            button.Background = new SolidColorBrush(active ? ColorHelper.FromArgb(255, 53, 99, 233) : Colors.Transparent);
            button.Foreground = new SolidColorBrush(active ? Colors.White : ColorHelper.FromArgb(255, 184, 196, 216));
            button.BorderThickness = active ? new Thickness(1) : new Thickness(0);
        }
    }

    private void AccountsNav_Click(object sender, RoutedEventArgs e) => ShowPage(AccountsPage, AccountsNav);
    private void ImportNav_Click(object sender, RoutedEventArgs e) => ShowPage(ImportPage, ImportNav);
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowPage(SettingsPage, SettingsNav);

    private void RefreshAccounts()
    {
        var term = SearchBox?.Text?.Trim() ?? "";
        foreach (var account in _data.Accounts) account.UiIsCurrent = account.Id == _data.CurrentAccountId;
        _visibleAccounts.Clear();
        foreach (var account in _data.Accounts.Where(a => term.Length == 0 ||
                     a.Username.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                     a.EffectiveGameId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                     a.Status.Contains(term, StringComparison.OrdinalIgnoreCase)))
            _visibleAccounts.Add(account);
        TotalCount.Text = _data.Accounts.Count.ToString();
        NormalCount.Text = _data.Accounts.Count(a => a.Status == "正常").ToString();
        TempCount.Text = _data.Accounts.Count(a => a.Status == "临时封禁").ToString();
        PermanentCount.Text = _data.Accounts.Count(a => a.Status == "永久封禁").ToString();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => RefreshAccounts();

    private List<AccountRecord> CheckedAccounts() => _data.Accounts.Where(a => a.UiSelected).ToList();

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

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AccountRecord account) return;
        if (!await ConfirmAsync("登录确认", "切换账号会关闭当前 Steam。是否继续？")) return;
        try
        {
            await _steam.LaunchAsync(_data.Settings.SteamPath, account.Username, DataStore.Decrypt(account.EncryptedPassword), _data.Settings, _operation.Token);
            account.LastLoginAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            _data.CurrentAccountId = account.Id;
            SaveData();
            RefreshAccounts();
        }
        catch (Exception ex) { await MessageAsync("启动失败", ex.Message); }
    }

    private void Query_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is AccountRecord account) OpenQuery(new List<AccountRecord> { account });
    }

    private void BatchQuery_Click(object sender, RoutedEventArgs e)
    {
        var accounts = CheckedAccounts();
        if (accounts.Count == 0) { _ = MessageAsync("批量查询", "请先勾选需要查询的账号。"); return; }
        OpenQuery(accounts);
    }

    private void OpenQuery(List<AccountRecord> accounts)
    {
        var window = new QueryWindow(accounts.Select(a => a.EffectiveGameId));
        window.ResultReceived += result =>
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
        window.Activate();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AccountRecord account) return;
        if (!await ConfirmAsync("删除账号", $"确定删除账号“{account.Username}”吗？")) return;
        RemoveAccounts(new[] { account });
    }

    private async void BatchDelete_Click(object sender, RoutedEventArgs e)
    {
        var accounts = CheckedAccounts();
        if (accounts.Count == 0) { await MessageAsync("批量删除", "请先勾选需要删除的账号。"); return; }
        if (!await ConfirmAsync("批量删除", $"确定删除选中的 {accounts.Count} 个账号吗？")) return;
        RemoveAccounts(accounts);
    }

    private void RemoveAccounts(IEnumerable<AccountRecord> accounts)
    {
        foreach (var account in accounts.ToList())
        {
            if (_data.CurrentAccountId == account.Id) _data.CurrentAccountId = "";
            _data.Accounts.Remove(account);
        }
        SaveData();
        RefreshAccounts();
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AccountRecord account) return;
        var username = new TextBox { Text = account.Username };
        var password = new PasswordBox { PlaceholderText = "留空则不修改" };
        var email = new TextBox { Text = account.Email };
        var emailPassword = new PasswordBox { PlaceholderText = "留空则不修改" };
        var gameId = new TextBox { Text = account.GameId };
        var note = new TextBox { Text = account.Note };
        var content = new StackPanel { Spacing = 7, Width = 420 };
        AddEditorField(content, "Steam 账号", username);
        AddEditorField(content, "Steam 密码", password);
        AddEditorField(content, "邮箱", email);
        AddEditorField(content, "邮箱密码", emailPassword);
        AddEditorField(content, "PUBG 查询 ID", gameId);
        AddEditorField(content, "备注", note);
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "编辑账号", Content = content, PrimaryButtonText = "保存", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var newUsername = username.Text.Trim();
        if (newUsername.Length == 0) { await MessageAsync("无法保存", "Steam 账号不能为空。"); return; }
        if (_data.Accounts.Any(a => a != account && string.Equals(a.Username, newUsername, StringComparison.OrdinalIgnoreCase))) { await MessageAsync("无法保存", "这个 Steam 账号已经存在。"); return; }
        account.Username = newUsername;
        if (password.Password.Length > 0) account.EncryptedPassword = DataStore.Encrypt(password.Password);
        account.Email = email.Text.Trim().Replace("\\@", "@");
        if (emailPassword.Password.Length > 0) account.EncryptedEmailPassword = DataStore.Encrypt(emailPassword.Password);
        account.GameId = gameId.Text.Trim();
        account.Note = note.Text.Trim();
        SaveData();
        RefreshAccounts();
    }

    private static void AddEditorField(StackPanel panel, string label, Control input)
    {
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12 });
        panel.Children.Add(input);
    }

    private void ClearImport_Click(object sender, RoutedEventArgs e)
    {
        ImportText.Text = "";
        _previewItems.Clear();
        PreviewCount.Text = "共 0 条　可导入 0 条";
    }

    private void ParseImport_Click(object sender, RoutedEventArgs e) => ParsePreview();

    private void ParsePreview()
    {
        _previewItems.Clear();
        var parsed = ImportParser.Parse(ImportText.Text);
        foreach (var item in parsed)
        {
            if (item.IsValid) item.Error = "可导入";
            _previewItems.Add(item);
        }
        PreviewCount.Text = $"共 {parsed.Count} 条　可导入 {parsed.Count(x => x.IsValid || x.Error == "可导入")} 条";
    }

    private async void CommitImport_Click(object sender, RoutedEventArgs e)
    {
        var parsed = ImportParser.Parse(ImportText.Text);
        var existing = new HashSet<string>(_data.Accounts.Select(a => a.Username), StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var skipped = 0;
        foreach (var item in parsed)
        {
            if (!item.IsValid || !existing.Add(item.Username)) { skipped++; continue; }
            _data.Accounts.Add(new AccountRecord { Username = item.Username, EncryptedPassword = DataStore.Encrypt(item.Password), Email = item.Email, EncryptedEmailPassword = DataStore.Encrypt(item.EmailPassword), GameId = item.GameId });
            added++;
        }
        SaveData();
        RefreshAccounts();
        await MessageAsync("导入完成", $"成功导入 {added} 个账号，跳过 {skipped} 条。");
    }

    private void LoadSettings()
    {
        SteamPathBox.Text = _data.Settings.SteamPath;
        StartupDelayBox.Value = Math.Clamp(_data.Settings.StartupDelaySeconds, 2, 30);
        CloseRecommendationsBox.IsChecked = _data.Settings.CloseRecommendations;
        CloseFriendsBox.IsChecked = _data.Settings.CloseFriendsList;
        OpenLibraryBox.IsChecked = _data.Settings.OpenLibrary;
    }

    private async void BrowseSteam_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file != null) SteamPathBox.Text = file.Path;
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _data.Settings.SteamPath = SteamPathBox.Text.Trim();
        _data.Settings.StartupDelaySeconds = double.IsNaN(StartupDelayBox.Value) ? 8 : (int)StartupDelayBox.Value;
        _data.Settings.CloseRecommendations = CloseRecommendationsBox.IsChecked == true;
        _data.Settings.CloseFriendsList = CloseFriendsBox.IsChecked == true;
        _data.Settings.OpenLibrary = OpenLibraryBox.IsChecked == true;
        SaveData();
        await MessageAsync("保存成功", "设置已保存。");
    }

    private void SaveData(bool showError = true)
    {
        try { _store.Save(_data); }
        catch (Exception ex) { if (showError) _ = MessageAsync("保存失败", ex.Message); }
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = message, PrimaryButtonText = "确定", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task MessageAsync(string title, string message)
    {
        var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = message, CloseButtonText = "知道了" };
        await dialog.ShowAsync();
    }
}
