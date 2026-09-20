using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Web;
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
        private int _index;
        private bool _reading;

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

            var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.White, Padding = new Padding(18, 8, 18, 8) };
            _progress.Dock = DockStyle.Top;
            _progress.Height = 28;
            _progress.Font = new Font(Font, FontStyle.Bold);
            _progress.ForeColor = Color.FromArgb(19, 28, 46);
            _instruction.Dock = DockStyle.Bottom;
            _instruction.Height = 28;
            _instruction.Text = "查询 ID 已自动填写，请在网页中手动点击“查询”。网页返回结果后会自动保存等级和封禁状态。";
            _instruction.ForeColor = Color.FromArgb(72, 91, 116);
            header.Controls.Add(_instruction);
            header.Controls.Add(_progress);

            _web.Dock = DockStyle.Fill;
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
            if (_reading || !IsMatchingRequest(e.Request.Uri, CurrentId)) return;
            _reading = true;
            try
            {
                using (var stream = await e.Response.GetContentAsync())
                using (var reader = new StreamReader(stream))
                {
                    var result = _parser.Parse(await reader.ReadToEndAsync(), CurrentId);
                    if (result != null) BeginInvoke(new Action(() => AcceptResult(result)));
                }
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
                return string.Equals(HttpUtility.ParseQueryString(uri.Query)["player_id"], gameId, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private void AcceptResult(PubgPlusResult result)
        {
            ResultReceived?.Invoke(result);
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
