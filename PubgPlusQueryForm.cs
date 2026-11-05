using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using SteamLoginLite.Models;
using SteamLoginLite.Services;

namespace SteamLoginLite
{
    internal sealed class PubgPlusQueryForm : Form
    {
        private const string PageUrl = "https://pubg.plus/zh-CN/player";
        private readonly QueryBatchState _batch;
        private readonly int _interval;
        private readonly Label _progress = new Label { AutoSize = true };
        private readonly Label _instruction = new Label { AutoSize = true };
        private readonly Panel _browser = new Panel { Dock = DockStyle.Fill };
        private readonly Button _retry = new Button { Text = "重试", AutoSize = true };
        private readonly Button _skip = new Button { Text = "跳过", AutoSize = true };
        private readonly System.Windows.Forms.Timer _fillTimer = new System.Windows.Forms.Timer { Interval = 500 };
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly PubgPlusResponseParser _parser = new PubgPlusResponseParser();
        private readonly CancellationTokenSource _closing = new CancellationTokenSource();
        private readonly Action<CoreWebView2> _fixture;
        private CoreWebView2Environment _environment;
        private WebView2 _web;
        private int _fillTicks;
        private bool _finishing;
        private bool _ready;
        public QueryBatchState Batch => _batch;
        public event Action<QueryAccount, PubgPlusResult> ResultReceived;
        internal event Action<string> Completed;
        internal event Action<string> Trace;
        internal string SmokeStatus => _instruction.Text;
        internal void SmokeSkip() => _skip.PerformClick();
        internal static string CacheDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamLoginLite", "WebView2");

        public PubgPlusQueryForm(IReadOnlyList<QueryAccount> accounts, int interval, Action<CoreWebView2> fixture = null)
        {
            _batch = new QueryBatchState(accounts); _interval = Math.Max(500, Math.Min(30000, interval)); _fixture = fixture;
            Text = "PUBG.PLUS 查询";
            try { Icon = Icon.ExtractAssociatedIcon(typeof(MainForm).Assembly.Location); } catch { } ClientSize = new Size(1080, 720); MinimumSize = new Size(900, 600);
            Font = new Font("Microsoft YaHei UI", 9F); AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
            var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10), FlowDirection = FlowDirection.TopDown, WrapContents = false };
            header.Controls.Add(_progress); header.Controls.Add(_instruction);
            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var cancel = new Button { Text = "结束查询", AutoSize = true };
            actions.Controls.Add(_retry); actions.Controls.Add(_skip); actions.Controls.Add(cancel); header.Controls.Add(actions);
            Controls.Add(_browser); Controls.Add(header);
            Resize += (_, __) => { _instruction.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 28), 0); _progress.MaximumSize = _instruction.MaximumSize; };
            _instruction.MaximumSize = new Size(ClientSize.Width - 28, 0);
            _retry.Click += async (_, __) => { if (_ready) await BeginCurrentAsync(); else await InitializeAsync(); };
            _skip.Click += async (_, __) => { if (!_ready) return; _batch.Advance(false); await BeginCurrentAsync(); };
            cancel.Click += (_, __) => Close();
            _fillTimer.Tick += async (_, __) => await FillAsync();
            Shown += async (_, __) => await InitializeAsync();
            FormClosing += (_, __) => { if (!_batch.Finished) Completed?.Invoke("查询已结束：已保存 " + _batch.Saved + " 个，跳过 " + _batch.Skipped + " 个。"); _closing.Cancel(); _batch.Cancel(); _fillTimer.Stop(); _web?.Dispose(); };
            FormClosed += (_, __) => { _fillTimer.Dispose(); _closing.Dispose(); };
        }

        private void Status(string text) { if (!IsDisposed && !Disposing) _instruction.Text = text; }
        private async Task InitializeAsync()
        {
            _retry.Enabled = _skip.Enabled = false;
            try
            {
                await WebView2Runtime.EnsureAsync(Status, _closing.Token);
                if (_closing.IsCancellationRequested) return;
                Directory.CreateDirectory(CacheDirectory);
                _environment = await CoreWebView2Environment.CreateAsync(null, CacheDirectory, new CoreWebView2EnvironmentOptions());
                if (_closing.IsCancellationRequested) return;
                _ready = true; await BeginCurrentAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Status("查询初始化失败：" + ex.Message + " 点击重试可重新安装或初始化，不会转到外部浏览器。"); }
            finally { if (!IsDisposed) { _retry.Enabled = true; _skip.Enabled = _ready; } }
        }

        private async Task BeginCurrentAsync()
        {
            _fillTimer.Stop(); _finishing = false;
            var generation = _batch.Restart();
            _web?.Dispose(); _web = null; _browser.Controls.Clear();
            if (_batch.Finished) { FinishBatch(); return; }
            _retry.Enabled = _skip.Enabled = false;
            var task = _batch.Current;
            var web = new WebView2 { Dock = DockStyle.Fill };
            _web = web; _browser.Controls.Add(web);
            _progress.Text = "查询 " + (_batch.Index + 1) + " / " + _batch.Accounts.Count + "：" + task.GameId + "　已保存 " + _batch.Saved + "，跳过 " + _batch.Skipped;
            Status("正在加载查询页面…");
            try
            {
                await web.EnsureCoreWebView2Async(_environment);
                if (!Current(generation, web)) return;
                var core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false; core.Settings.IsStatusBarEnabled = false;
                core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
                core.DownloadStarting += (_, args) => args.Cancel = true;
                core.NewWindowRequested += (_, args) => args.Handled = true;
                core.NavigationStarting += (_, args) => { if (!AllowedPage(args.Uri)) args.Cancel = true; };
                core.ProcessFailed += (_, __) => { if (Current(generation, web)) { _fillTimer.Stop(); Status("查询网页进程已停止，请点击重试。"); } };
                core.WebResourceResponseReceived += async (_, args) => await ResponseAsync(args, generation, web, task.GameId);
                core.NavigationCompleted += (_, args) => {
                    if (!Current(generation, web)) return;
                    if (!args.IsSuccess) { Status("页面加载失败，可点击重试；请检查网络。"); return; }
                    _fillTicks = 0; _fillTimer.Start(); Status("正在填写并提交查询；验证码请在页面中手动完成。");
                };
                await core.AddScriptToExecuteOnDocumentCreatedAsync("try{localStorage.setItem('pubgSelectedPlatform','steam');}catch(e){}");
                if (!Current(generation, web)) return;
                _fixture?.Invoke(core);
                core.Navigate(PageUrl);
            }
            catch (Exception ex) { if (Current(generation, web)) Status("查询页面初始化失败：" + ex.Message + "，请重试。"); }
            finally { if (Current(generation, web)) _retry.Enabled = _skip.Enabled = true; }
        }
        private static bool AllowedPage(string url)
        {
            Uri uri;
            return url == "about:blank" || (Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.Host.Equals("pubg.plus", StringComparison.OrdinalIgnoreCase));
        }
        private bool Current(int generation, WebView2 web) => !IsDisposed && !Disposing && !_closing.IsCancellationRequested && _batch.IsCurrent(generation) && ReferenceEquals(_web, web);
        private static string Script()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SteamLoginLite.Assets.query-autofill.js"))
            using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
        }
        private bool _filling;
        private async Task FillAsync()
        {
            if (_filling || _web?.CoreWebView2 == null || _batch.Finished || _finishing) return;
            var web = _web; var generation = _batch.Generation; _filling = true;
            try
            {
                var result = await web.ExecuteScriptAsync(Script().Replace("__QUERY_ID_JSON__", _json.Serialize(_batch.Current.GameId)));
                if (!Current(generation, web)) return;
                if (result == "\"submitted\"") { _fillTimer.Stop(); Status("已提交，正在等待查询结果；如有验证码请手动完成，成功后会自动保存并继续。"); }
                else if (result == "\"manual\"" || ++_fillTicks >= 60) { _fillTimer.Stop(); Status("请在页面中完成验证码或点击查询；读取结果后会自动保存并继续。"); }
            }
            catch { if (Current(generation, web)) { _fillTimer.Stop(); Status("自动填写未完成，可在网页手动输入并查询，或点击重试。"); } }
            finally { _filling = false; }
        }

        private async Task ResponseAsync(CoreWebView2WebResourceResponseReceivedEventArgs args, int generation, WebView2 web, string expected)
        {
            if (!Current(generation, web) || args.Response.StatusCode < 200 || args.Response.StatusCode >= 300) return;
            string accountId; var kind = PubgPlusRequestMatcher.Classify(args.Request.Uri, expected, out accountId);
            if (kind == PubgPlusRequestKind.None) return;
            Trace?.Invoke("Response " + kind + " generation " + generation + " " + args.Request.Uri);
            try
            {
                string body;
                using (var source = await args.Response.GetContentAsync())
                {
                    if (!Current(generation, web) || source == null) return;
                    using (var target = new MemoryStream())
                    {
                        var buffer = new byte[32768]; int count;
                        while ((count = await source.ReadAsync(buffer, 0, buffer.Length, _closing.Token)) > 0)
                        { if (target.Length + count > 2 * 1024 * 1024) return; target.Write(buffer, 0, count); }
                        body = Encoding.UTF8.GetString(target.ToArray());
                    }
                }
                if (!Current(generation, web)) return;
                PubgPlusResult result;
                if (kind == PubgPlusRequestKind.Legacy) result = _batch.Accept(generation, _parser.Parse(body, expected));
                else if (kind == PubgPlusRequestKind.Basic)
                {
                    var basic = _parser.ParseBasic(body, expected);
                    result = _batch.AddBasic(generation, basic);
                    if (basic != null && result == null) { Status("已读取账号状态，正在等待等级…"); _ = FallbackAsync(generation, web); }
                }
                else result = _batch.AddMastery(generation, _parser.ParseSurvivalMastery(body, accountId));
                if (result != null) { Trace?.Invoke("Result: " + result.GameId + " level=" + result.Level + " tier=" + result.Tier + " hasLevel=" + result.HasLevel + " status=" + result.Status); await AcceptAsync(result, generation, web); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Trace?.Invoke("Response failure: " + ex.Message); if (Current(generation, web)) Status("查询响应读取失败，请点击重试或跳过。"); }
        }
        private async Task FallbackAsync(int generation, WebView2 web)
        {
            try
            {
                await Task.Delay(8000, _closing.Token);
                if (!Current(generation, web)) return;
                var result = _batch.Fallback(generation);
                if (result != null) { Trace?.Invoke("Result: " + result.GameId + " level=" + result.Level + " tier=" + result.Tier + " hasLevel=" + result.HasLevel + " status=" + result.Status); await AcceptAsync(result, generation, web); }
            }
            catch (OperationCanceledException) { }
        }
        private async Task AcceptAsync(PubgPlusResult result, int generation, WebView2 web)
        {
            if (!Current(generation, web) || _finishing) return;
            _finishing = true; _fillTimer.Stop(); _retry.Enabled = _skip.Enabled = false;
            try
            {
                ResultReceived?.Invoke(_batch.Current, result);
                Status("查询结果已保存，准备继续…");
                _batch.Advance(true);
                await Task.Delay(_interval, _closing.Token);
                if (!IsDisposed && !_closing.IsCancellationRequested) await BeginCurrentAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (Current(generation, web)) { _batch.SaveFailed(generation); _finishing = false; Status("结果保存失败：" + ex.Message + "；请重试。"); _retry.Enabled = _skip.Enabled = true; } }
        }
        private void FinishBatch()
        {
            var summary = "查询结束：已保存 " + _batch.Saved + " 个，跳过 " + _batch.Skipped + " 个。";
            Completed?.Invoke(summary); DialogResult = DialogResult.OK; Close();
        }
    }
}
