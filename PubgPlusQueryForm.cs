using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
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
        private readonly List<string> _gameIds;
        private readonly WebView2 _web = new WebView2();
        private readonly Label _progress = new Label();
        private readonly Label _instruction = new Label();
        private readonly Timer _fillTimer = new Timer { Interval = 500 };
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly PubgPlusResponseParser _parser = new PubgPlusResponseParser();
        private readonly object _responseSync = new object();
        private readonly Dictionary<string, PubgPlusResult> _masteryResults = new Dictionary<string, PubgPlusResult>(StringComparer.OrdinalIgnoreCase);
        private PubgPlusResult _basicResult;
        private int _index;
        private bool _acceptPending;

        public event Action<PubgPlusResult> ResultReceived;

        public PubgPlusQueryForm(IEnumerable<string> gameIds)
        {
            _gameIds = gameIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Text = "PUBG.PLUS 等级与封禁查询";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Width = 1120;
            Height = 760;
            MinimumSize = new Size(900, 620);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(244, 247, 251);

            var header = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = Color.White, Padding = new Padding(22, 10, 22, 10) };
            header.Paint += (_, e) => { using (var pen = new Pen(Color.FromArgb(224, 230, 239))) e.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1); };
            _progress.Dock = DockStyle.Top;
            _progress.Height = 32;
            _progress.Font = new Font(Font.FontFamily, 10.5F, FontStyle.Bold);
            _progress.ForeColor = Color.FromArgb(19, 28, 46);
            _instruction.Dock = DockStyle.Bottom;
            _instruction.Height = 28;
            _instruction.Text = "查询 ID 已自动填写，请在网页中手动点击“查询”。网页返回结果后会自动保存等级和封禁状态。";
            _instruction.ForeColor = Color.FromArgb(102, 116, 136);
            header.Controls.Add(_instruction);
            header.Controls.Add(_progress);

            _web.Dock = DockStyle.Fill;
            _web.BackColor = Color.FromArgb(244, 247, 251);
            Controls.Add(_web);
            Controls.Add(header);
            _fillTimer.Tick += async (_, __) => await FillCurrentIdAsync();
            Shown += async (_, __) => await InitializeAsync();
            FormClosed += (_, __) => _fillTimer.Stop();
        }

        private string CurrentId => _index >= 0 && _index < _gameIds.Count ? _gameIds[_index] : "";

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            if (_gameIds.Count == 0) { Close(); return; }
            try
            {
                var runtimeVersion = CoreWebView2Environment.GetAvailableBrowserVersionString();
                if (string.IsNullOrWhiteSpace(runtimeVersion)) throw new InvalidOperationException("未检测到 WebView2 Runtime");
                var dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "webview2");
                Directory.CreateDirectory(dataDirectory);
                var environment = await CoreWebView2Environment.CreateAsync(null, dataDirectory);
                await _web.EnsureCoreWebView2Async(environment);
                await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("try{localStorage.setItem('pubgSelectedPlatform','steam');}catch(e){}");
                _web.CoreWebView2.WebResourceResponseReceived += OnWebResourceResponseReceived;
                _web.CoreWebView2.ProcessFailed += (_, args) => BeginInvoke(new Action(() =>
                    MessageBox.Show("PUBG.PLUS 网页进程异常：" + args.ProcessFailedKind, "查询窗口异常", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
                _web.NavigationCompleted += (_, __) => _fillTimer.Start();
                UpdateProgress();
                _web.Source = new Uri(PageUrl);
            }
            catch (Exception ex)
            {
                OpenInDefaultBrowser(ex);
            }
        }

        private void OpenInDefaultBrowser(Exception reason)
        {
            _fillTimer.Stop();
            try
            {
                Process.Start(new ProcessStartInfo(PageUrl) { UseShellExecute = true });
                var copied = false;
                try
                {
                    if (!string.IsNullOrWhiteSpace(CurrentId))
                    {
                        Clipboard.SetText(CurrentId);
                        copied = true;
                    }
                }
                catch { }

                MessageBox.Show(
                    "当前电脑无法使用内置浏览器，已改用系统默认浏览器打开 PUBG.PLUS。" +
                    (copied ? "\r\n当前查询 ID 已复制到剪贴板，可直接粘贴查询。" : "") +
                    "\r\n\r\n默认浏览器中的查询结果无法自动回填到切换器；需要自动保存等级和封禁状态时，请安装 Microsoft Edge WebView2 Runtime。" +
                    "\r\n\r\n原因：" + reason.Message,
                    "已使用默认浏览器",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开系统默认浏览器。\r\n\r\n" + ex.Message, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Close();
        }

        private async System.Threading.Tasks.Task FillCurrentIdAsync()
        {
            if (_web.CoreWebView2 == null || string.IsNullOrWhiteSpace(CurrentId)) return;
            var idJson = _json.Serialize(CurrentId);
            var script = "(() => { const i=document.querySelector('.search-input input'); if(!i||i.disabled||i.readOnly)return false; const s=Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set; s.call(i," + idJson + "); i.dispatchEvent(new Event('input',{bubbles:true})); i.dispatchEvent(new Event('change',{bubbles:true})); return i.value===" + idJson + "; })()";
            try
            {
                var result = await _web.ExecuteScriptAsync(script);
                if (string.Equals(result, "true", StringComparison.OrdinalIgnoreCase)) _fillTimer.Stop();
            }
            catch { }
        }

        private async void OnWebResourceResponseReceived(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            var expectedId = CurrentId;
            string requestAccountId;
            var requestKind = PubgPlusRequestMatcher.Classify(e.Request.Uri, expectedId, out requestAccountId);
            if (requestKind == PubgPlusRequestKind.None) return;
            try
            {
                using (var stream = await e.Response.GetContentAsync())
                using (var reader = new StreamReader(stream))
                {
                    var body = await reader.ReadToEndAsync();
                    if (requestKind == PubgPlusRequestKind.Legacy)
                    {
                        QueueAcceptedResult(_parser.Parse(body, expectedId));
                        return;
                    }

                    if (requestKind == PubgPlusRequestKind.Basic)
                    {
                        var basic = _parser.ParseBasic(body, expectedId);
                        if (basic == null) return;
                        lock (_responseSync) _basicResult = basic;
                        ScheduleBasicFallback(expectedId, basic.AccountId);
                    }
                    else
                    {
                        var mastery = _parser.ParseSurvivalMastery(body, requestAccountId);
                        if (mastery == null) return;
                        lock (_responseSync) _masteryResults[mastery.AccountId] = mastery;
                    }

                    TryQueueCombinedResult(expectedId);
                }
            }
            catch { }
        }

        private void TryQueueCombinedResult(string expectedId)
        {
            PubgPlusResult combined = null;
            lock (_responseSync)
            {
                if (_acceptPending || _basicResult == null || !string.Equals(_basicResult.GameId, expectedId, StringComparison.OrdinalIgnoreCase)) return;
                PubgPlusResult mastery;
                if (!_masteryResults.TryGetValue(_basicResult.AccountId, out mastery)) return;
                _acceptPending = true;
                combined = new PubgPlusResult
                {
                    GameId = _basicResult.GameId,
                    AccountId = _basicResult.AccountId,
                    RawStatus = _basicResult.RawStatus,
                    Status = _basicResult.Status,
                    HasLevel = true,
                    Level = mastery.Level,
                    Tier = mastery.Tier
                };
            }
            QueueAcceptedResult(combined);
        }

        private async void ScheduleBasicFallback(string expectedId, string accountId)
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(4000);
                PubgPlusResult fallback = null;
                lock (_responseSync)
                {
                    if (_acceptPending || _basicResult == null ||
                        !string.Equals(_basicResult.GameId, expectedId, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(_basicResult.AccountId, accountId, StringComparison.OrdinalIgnoreCase)) return;
                    _acceptPending = true;
                    fallback = new PubgPlusResult
                    {
                        GameId = _basicResult.GameId,
                        AccountId = _basicResult.AccountId,
                        RawStatus = _basicResult.RawStatus,
                        Status = _basicResult.Status,
                        HasLevel = false
                    };
                }
                QueueAcceptedResult(fallback);
            }
            catch { }
        }

        private void QueueAcceptedResult(PubgPlusResult result)
        {
            if (result == null || IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed && string.Equals(result.GameId, CurrentId, StringComparison.OrdinalIgnoreCase)) AcceptResult(result);
                }));
            }
            catch { }
        }

        private void AcceptResult(PubgPlusResult result)
        {
            ResultReceived?.Invoke(result);
            lock (_responseSync)
            {
                _basicResult = null;
                _masteryResults.Clear();
                _acceptPending = false;
            }
            _index++;
            if (_index >= _gameIds.Count)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            UpdateProgress();
            _fillTimer.Start();
            _web.Source = new Uri(PageUrl);
        }

        private void UpdateProgress()
        {
            _progress.Text = "正在查询 " + (_index + 1) + " / " + _gameIds.Count + "：" + CurrentId;
        }
    }
}
