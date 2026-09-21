using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using SteamLoginLite.Models;
using SteamLoginLite.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace SteamLoginLite;

public sealed partial class QueryWindow : Window
{
    private const string PageUrl = "https://pubg.plus/zh-CN/player";
    private readonly List<string> _gameIds;
    private readonly PubgPlusResponseParser _parser = new();
    private readonly DispatcherTimer _fillTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _index;
    private bool _reading;

    public event Action<PubgPlusResult> ResultReceived;

    public QueryWindow(IEnumerable<string> gameIds)
    {
        InitializeComponent();
        _gameIds = gameIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        _fillTimer.Tick += async (_, __) => await FillCurrentIdAsync();
        Browser.NavigationCompleted += (_, __) => _fillTimer.Start();
        Activated += OnActivated;
        ConfigureWindow();
    }

    private string CurrentId => _index >= 0 && _index < _gameIds.Count ? _gameIds[_index] : "";

    private void ConfigureWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        appWindow.Resize(new SizeInt32(1120, 760));
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "SteamSwitcher.ico"));
        appWindow.Closing += (_, __) => _fillTimer.Stop();
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        if (_gameIds.Count == 0) { Close(); return; }
        try
        {
            var runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (string.IsNullOrWhiteSpace(runtimeVersion)) throw new InvalidOperationException("未检测到 WebView2 Runtime");
            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.WebResourceResponseReceived += OnWebResourceResponseReceived;
            UpdateProgress();
            Browser.Source = new Uri(PageUrl);
        }
        catch (Exception ex) { OpenInDefaultBrowser(ex); }
    }

    private async Task FillCurrentIdAsync()
    {
        if (Browser.CoreWebView2 == null || string.IsNullOrWhiteSpace(CurrentId)) return;
        var idJson = JsonSerializer.Serialize(CurrentId);
        var script = "(() => { const i=document.querySelector('.search-input input'); if(!i||i.disabled||i.readOnly)return false; const s=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set; s.call(i," + idJson + "); i.dispatchEvent(new Event('input',{bubbles:true})); i.dispatchEvent(new Event('change',{bubbles:true})); return i.value===" + idJson + "; })()";
        try
        {
            var result = await Browser.ExecuteScriptAsync(script);
            if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase)) _fillTimer.Stop();
        }
        catch { }
    }

    private async void OnWebResourceResponseReceived(object sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        if (_reading || !IsMatchingRequest(args.Request.Uri, CurrentId)) return;
        _reading = true;
        try
        {
            using var stream = await args.Response.GetContentAsync();
            using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var result = _parser.Parse(reader.ReadString((uint)stream.Size), CurrentId);
            if (result != null) DispatcherQueue.TryEnqueue(() => AcceptResult(result));
        }
        catch { }
        finally { _reading = false; }
    }

    private static bool IsMatchingRequest(string url, string gameId)
    {
        try
        {
            var uri = new Uri(url);
            if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || !uri.Host.Equals("apiv1.pubg.plus", StringComparison.OrdinalIgnoreCase) || !uri.AbsolutePath.EndsWith("/player/info", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = part.Split('=', 2);
                if (pieces.Length == 2 && Uri.UnescapeDataString(pieces[0]) == "player_id") return string.Equals(Uri.UnescapeDataString(pieces[1].Replace('+', ' ')), gameId, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch { }
        return false;
    }

    private void AcceptResult(PubgPlusResult result)
    {
        ResultReceived?.Invoke(result);
        _index++;
        if (_index >= _gameIds.Count) { Close(); return; }
        UpdateProgress();
        _fillTimer.Start();
        Browser.Source = new Uri(PageUrl);
    }

    private void UpdateProgress() => ProgressText.Text = $"正在查询 {_index + 1} / {_gameIds.Count}：{CurrentId}";

    private void OpenInDefaultBrowser(Exception reason)
    {
        _fillTimer.Stop();
        try
        {
            Process.Start(new ProcessStartInfo(PageUrl) { UseShellExecute = true });
            if (!string.IsNullOrWhiteSpace(CurrentId))
            {
                var package = new DataPackage();
                package.SetText(CurrentId);
                Clipboard.SetContent(package);
            }
            ProgressText.Text = "无法启动内置网页，已使用默认浏览器并复制当前查询 ID：" + reason.Message;
        }
        catch { ProgressText.Text = "无法打开查询网页：" + reason.Message; }
    }
}
