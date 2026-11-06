using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using SteamLoginLite;
using SteamLoginLite.Models;
using SteamLoginLite.Services;

internal static class NativeSmoke
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    [STAThread]
    private static int Main(string[] args)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try { if (args.Contains("--query")) Query(); else if (args.Contains("--installer")) Installer(); else Ui(args.LastOrDefault() ?? "release/native-preview.png"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Ui(string screenshot)
    {
        Exception failure = null;
        using (var form = new MainForm(true))
        {
            var timer = new Timer { Interval = 400 };
            timer.Tick += (_, __) => {
                timer.Stop();
                try {
                    Check(form.Data.Accounts.Count == 28 && form.Grid.Rows.Count == 20, "Default page and isolated preview data");
                    Check(Convert.ToString(form.Grid.Rows[0].Cells["Level"].Value).Contains("阶"), "Survival tier remains visible");
                    Check(form.Grid.Columns["Note"].Index == form.Grid.Columns["Status"].Index + 1, "Note column must follow ban status");
                    Check(Convert.ToString(form.Grid.Rows[0].Cells["Source"].Value).Contains("当前"), "Verified current marker");
                    Check(Convert.ToString(form.Grid.Rows[1].Cells["Source"].Value).Contains("本机"), "Local source marker");
                    Check(Convert.ToString(form.Grid.Rows[1].Cells["Note"].Value) == "", "Empty note has no fallback");
                    var currentId = form.Data.CurrentAccountId;
                    var nextAccount = form.Data.Accounts[27];
                    var previousLogin = nextAccount.LastLoginAt;
                    form.ApplySteamStatus(0, nextAccount.Id, nextAccount.Username, "stopping", "正在退出 Steam…");
                    Check(form.LoginInProgress, "Login progress is visible before Steam starts");
                    form.ApplySteamStatus(0, nextAccount.Id, nextAccount.Username, "waiting", "等待 Steam 验证…");
                    Check(form.LoginInProgress && nextAccount.LastLoginAt == previousLogin && form.Data.CurrentAccountId == currentId, "Starting Steam is not recorded as a successful login");
                    form.ApplySteamStatus(0, nextAccount.Id, nextAccount.Username, "timeout", "等待超时");
                    Check(!form.LoginInProgress && nextAccount.LastLoginAt == previousLogin, "Timeout stops progress without recording success");
                    form.ApplySteamStatus(0, nextAccount.Id, nextAccount.Username, "waiting", "等待 Steam 验证…");
                    form.ApplySteamStatus(-1, nextAccount.Id, nextAccount.Username, "authenticated", "旧任务");
                    Check(form.LoginInProgress && nextAccount.LastLoginAt == previousLogin, "Late result from old login cannot change progress or login time");
                    using (var other = new Form { Text = "Foreground regression fixture" }) {
                        other.Show(); other.Activate(); Application.DoEvents();
                        form.ApplySteamStatus(0, nextAccount.Id, nextAccount.Username, "authenticated", "Steam 会话已确认"); Application.DoEvents();
                        Check(!form.LoginInProgress && nextAccount.LastLoginAt > previousLogin, "Only confirmed target session records successful login and stops progress");
                        Check(Form.ActiveForm == other && !form.TopMost, "Session refresh must not steal foreground or set topmost");
                        var activeRow = form.Grid.Rows.Cast<DataGridViewRow>().Single(r => (r.Tag as AccountRecord)?.Id == nextAccount.Id);
                        Check(Convert.ToString(activeRow.Cells["Source"].Value).Contains("当前"), "Later login changes current marker without manual refresh");
                        Check(activeRow.DefaultCellStyle.BackColor != Color.Empty && activeRow.DefaultCellStyle.SelectionBackColor != Color.Empty, "Current row stays highlighted even when selected");
                        other.Close();
                    }
                    var renamedAccount = form.Data.Accounts[26];
                    var originalUsername = renamedAccount.Username;
                    var originalLogin = renamedAccount.LastLoginAt;
                    renamedAccount.Username = "renamed-fixture";
                    form.ApplySteamStatus(0, renamedAccount.Id, originalUsername, "authenticated", "旧账号名的登录结果");
                    Check(renamedAccount.LastLoginAt == originalLogin && form.Data.CurrentAccountId == nextAccount.Id, "Renaming an account while waiting prevents old target from recording success");
                    renamedAccount.Username = originalUsername;
                    var confirmedLogin = nextAccount.LastLoginAt;
                    form.ApplySteamStatus(0, nextAccount.Id, nextAccount.Username, "completed", "窗口处理完成");
                    Check(nextAccount.LastLoginAt == confirmedLogin, "Post-login window handling does not record a second login");
                    form.ApplyCurrentSession("");
                    Check(form.Grid.Rows.Cast<DataGridViewRow>().All(r => !Convert.ToString(r.Cells["Source"].Value).Contains("当前")), "Logout clears marker");
                    form.ApplyCurrentSession(currentId);
                    form.SmokeNextPage(); form.SmokeSearch("no-match");
                    form.LocateAccount(nextAccount);
                    Check((form.Grid.CurrentRow.Tag as AccountRecord)?.Id == nextAccount.Id, "Locate existing account clears old search and reveals off-page record");
                    using (var duplicateEditor = new AccountEditDialog(new AccountRecord { Username = nextAccount.Username }, true, name => name == nextAccount.Username)) {
                        duplicateEditor.Show(form); duplicateEditor.AcceptButton.PerformClick();
                        Check(duplicateEditor.DialogResult == DialogResult.OK, "Existing username reaches duplicate lookup without entering a password");
                        duplicateEditor.Close();
                    }
                    form.SmokeSearch("");
                    form.SmokeSelectAll(); Check(form.SelectionCount == 28, "Select all filtered records across pages");
                    form.SmokeNextPage(); Check(form.Grid.Rows.Count == 8 && form.Grid.Rows.Cast<DataGridViewRow>().All(r => Convert.ToBoolean(r.Cells[0].Value)), "Second page retains selection");
                    form.SmokeSearch("PUBG_Player_28"); Check(form.Grid.Rows.Count == 1 && form.FilteredCount == 1, "Nickname search resets page");
                    form.SmokeSearch(""); form.SmokeClearSelection(); Check(form.SelectionCount == 0 && form.Grid.Rows.Count == 20, "Clear selection and pagination reset");
                    using (var settings = new SettingsDialog(form.Data.Settings)) Check(settings.SmokePreferences.All(v => v), "Steam preferences preserved");
                    using (var restoreSettings = new SettingsDialog(form.Data.Settings, true)) {
                        Check(!restoreSettings.RestoreLocalAccounts, "Deleted accounts are not restored by default");
                        var layout = restoreSettings.Controls.OfType<TableLayoutPanel>().Single();
                        var restore = layout.Controls.OfType<CheckBox>().Single(c => c.Text.Contains("恢复已删除"));
                        restore.Checked = true;
                        Check(restoreSettings.RestoreLocalAccounts, "Explicit local account restore available in settings");
                    }
                    var note = form.Grid.GetCellDisplayRectangle(form.Grid.Columns["Note"].Index, 0, false);
                    var editButton = form.Grid.GetCellDisplayRectangle(form.Grid.Columns["Edit"].Index, 0, false);
                    Check(editButton.Width > 0 && editButton.Right <= form.Grid.ClientSize.Width, "All action buttons visible without scrolling");
                    Check(note.Width > 0 && note.Right <= form.Grid.ClientSize.Width, "Notes visible without horizontal scrolling");
                    var oldData = form.Data;
                    using (var editTimer = new Timer { Interval = 150 })
                    {
                        editTimer.Tick += (_, __) => {
                            var editor = Application.OpenForms.OfType<AccountEditDialog>().FirstOrDefault(); if (editor == null) return;
                            editTimer.Stop();
                            try {
                                var replacement = new AppData { Settings = oldData.Settings, CurrentAccountId = oldData.CurrentAccountId,
                                    Accounts = oldData.Accounts.Select(a => new AccountRecord { Id = a.Id, Username = a.Username, GameId = a.GameId, Note = a.Note, Status = a.Status, Level = a.Level, Tier = a.Tier, LastQueryAt = a.LastQueryAt, LastLoginAt = a.LastLoginAt, IsLocalAccount = a.IsLocalAccount, CanUseSavedLogin = a.CanUseSavedLogin }).ToList() };
                                form.SmokeReplaceData(replacement);
                                var layout = editor.Controls.OfType<TableLayoutPanel>().Single();
                                ((TextBox)layout.GetControlFromPosition(1, 5)).Text = "已保存备注";
                                editor.AcceptButton.PerformClick();
                            } catch (Exception ex) { failure = ex; editor.DialogResult = DialogResult.Cancel; editor.Close(); }
                        };
                        editTimer.Start(); form.SmokeEditFirst();
                    }
                    if (failure != null) throw failure;
                    Check(form.Data.Accounts[0].Note == "已保存备注" && oldData.Accounts[0].Note == "常用账号", "Edit survives concurrent snapshot reload by resolving stable account ID");
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot)));
                    using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(screenshot); }
                    Console.WriteLine("PASS: native UI, 28 isolated demo accounts, notes, markers, pagination, cross-page selection, search, preferences, screenshot.");
                } catch (Exception ex) { failure = ex; }
                finally { form.Close(); }
            };
            form.Shown += (_, __) => timer.Start(); Application.Run(form); timer.Dispose();
        }
        if (failure != null) throw failure;
    }
    private static void Installer()
    {
        var path = Path.Combine(Path.GetTempPath(), "steam-webview-installer-test-" + Guid.NewGuid() + ".exe");
        var invalid = path + ".invalid";
        try {
            using (var client = new WebClient()) client.DownloadFile(WebView2Runtime.DownloadUrl, path);
            WebView2Runtime.VerifyMicrosoftInstaller(path);
            File.WriteAllText(invalid, "not a signed Microsoft executable");
            var rejected = false;
            try { WebView2Runtime.VerifyMicrosoftInstaller(invalid); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Unsigned installer must never run");
            Console.WriteLine("PASS: current official Microsoft bootstrapper HTTPS download, WinVerifyTrust chain/publisher validation, unsigned-file rejection.");
        } finally { if (File.Exists(path)) File.Delete(path); if (File.Exists(invalid)) File.Delete(invalid); }
    }
    private static void Query()
    {
        var accounts = new[] { new QueryAccount("first", "fixture_same"), new QueryAccount("second", "fixture_same"), new QueryAccount("skip", "fixture_skip") };
        var streams = new List<MemoryStream>(); var saved = new List<string>(); var requests = new List<string>(); var completed = false; var timedOut = false;
        using (var dialog = new PubgPlusQueryForm(accounts, 500, core => {
            core.WebMessageReceived += (_, e) => Console.WriteLine("Fixture page: " + e.WebMessageAsJson);
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) => {
                var uri = new Uri(e.Request.Uri); string body; string type;
                if (uri.Host == "pubg.plus") {
                    type = "text/html; charset=utf-8";
                    body = "<!doctype html><html><meta charset='utf-8'><body><div class='search-input'><input placeholder='玩家昵称'></div><button disabled>查询</button><script>const b=document.querySelector('button');setTimeout(()=>b.disabled=false,200);window.addEventListener('unhandledrejection',e=>chrome.webview.postMessage(String(e.reason)));b.onclick=async()=>{const n=document.querySelector('input').value;const basic=await fetch('https://apiv1.pubg.plus/steam/player/basic?player_id='+encodeURIComponent(n));const r=await basic.json();await (await fetch('https://apiv1.pubg.plus/steam/player/survival_mastery?acc_id='+r.data.id)).json()};</script></body></html>";
                } else if (uri.Host == "apiv1.pubg.plus") {
                    type = "application/json"; requests.Add(uri.AbsolutePath);
                    if (uri.AbsolutePath.EndsWith("/basic")) {
                        var name = System.Web.HttpUtility.ParseQueryString(uri.Query)["player_id"];
                        body = name == "fixture_skip" ? "{}" : "{\"code\":0,\"data\":{\"id\":\"account.fixture\",\"attributes\":{\"name\":\"fixture_same\",\"banType\":\"Innocent\"}}}";
                    } else body = "{\"code\":0,\"data\":{\"id\":\"account.fixture\",\"attributes\":{\"level\":77,\"tier\":2}}}";
                } else { type = "text/plain"; body = "blocked in fixture"; }
                var stream = new MemoryStream(Encoding.UTF8.GetBytes(body)); streams.Add(stream);
                e.Response = core.Environment.CreateWebResourceResponse(stream, 200, "OK", "Content-Type: " + type + "\r\nAccess-Control-Allow-Origin: *\r\nCache-Control: no-store\r\n");
            };
        }))
        {
            dialog.Trace += Console.WriteLine;
            dialog.ResultReceived += (task, result) => {
                Console.WriteLine("Save fixture: " + result.GameId + " level=" + result.Level + " tier=" + result.Tier + " hasLevel=" + result.HasLevel + " status=" + result.Status);
                Check(task.Id == (saved.Count == 0 ? "first" : "second"), "Same nickname tasks preserve distinct account IDs");
                Check(result.GameId == "fixture_same" && result.HasLevel && result.Level == 77 && result.Tier == 2 && result.Status == "正常", "Trusted basic/mastery response merges correctly");
                saved.Add(task.Id);
            };
            dialog.Completed += _ => completed = true;
            var started = DateTime.UtcNow;
            using (var timeout = new Timer { Interval = 200 })
            {
                timeout.Tick += (_, __) => {
                    if (!dialog.Batch.Finished && dialog.Batch.Current.Id == "skip") dialog.SmokeSkip();
                    if (DateTime.UtcNow - started > TimeSpan.FromSeconds(90)) { timedOut = true; Console.Error.WriteLine("Query timeout: " + dialog.SmokeStatus); dialog.Close(); }
                };
                dialog.Shown += (_, __) => timeout.Start(); Application.Run(dialog); timeout.Stop();
            }
            Check(!timedOut && completed && saved.SequenceEqual(new[] { "first", "second" }) && dialog.Batch.Saved == 2 && dialog.Batch.Skipped == 1, "Autofill, automatic submit, skip and automatic close");
            Check(requests.Count(p => p.EndsWith("/basic")) >= 2, "Real Chromium fixture requests observed");
        }
        foreach (var stream in streams) stream.Dispose();
        Console.WriteLine("PASS: real WebView2, disabled-button wait, autofill/submit, duplicate nicknames with separate account IDs, bound basic/mastery, skip and auto-close.");
    }
}
