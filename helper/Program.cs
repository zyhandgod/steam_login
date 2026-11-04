using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using SteamLoginLite.Models;
using SteamLoginLite.Services;

internal static class Program
{
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
    public sealed class Request {
        public string Command { get; set; }
        public string Text { get; set; }
        public string[] Ids { get; set; }
        public AccountRecord Account { get; set; }
        public string Password { get; set; }
        public string EmailPassword { get; set; }
        public AppSettings Settings { get; set; }
        public PubgPlusResult Result { get; set; }
    }
    static void SyncLocalAccounts(AppData data)
    {
        data.CurrentAccountId = "";
        var local = SteamLocalAccounts.Read(data.Settings.SteamPath);
        SteamAccountSync.Merge(data, local, SteamService.FindActiveAccountName(data.Settings.SteamPath, local));
    }

    static int Main() {
        Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
        SteamService loginService = null;
        try {
            var r = Json.Deserialize<Request>(Console.ReadLine());
            var store = new DataStore(Environment.GetEnvironmentVariable("STEAM_LOGIN_ROOT")); var data = store.Load(); object extra = null;
            if (string.IsNullOrWhiteSpace(data.Settings.SteamPath)) data.Settings.SteamPath = SteamService.FindSteamPath();
            string syncWarning = null;
            try { SyncLocalAccounts(data); }
            catch (Exception) { syncWarning = "无法读取本机 Steam 登录记录，已有账号数据已保留；请确认 Steam 路径及登录记录是否可用。"; }
            switch (r.Command) {
                case "load":
                    extra = new { localSyncWarning = syncWarning };
                    break;
                case "save-account":
                    var a = r.Account; if (a == null || string.IsNullOrWhiteSpace(a.Username)) throw new Exception("请输入 Steam 账号。");
                    var old = data.Accounts.FirstOrDefault(x => x.Id == a.Id);
                    if (data.Accounts.Any(x => x.Id != a.Id && x.Username.Equals(a.Username.Trim(), StringComparison.OrdinalIgnoreCase))) throw new Exception("账号已存在。");
                    if (old == null && string.IsNullOrEmpty(r.Password)) throw new Exception("请输入密码。");
                    if (old == null) { old = new AccountRecord(); data.Accounts.Add(old); }
                    old.Username = a.Username.Trim(); old.GameId = (a.GameId ?? "").Trim(); old.Email = a.Email ?? ""; old.Note = a.Note ?? "";
                    if (!string.IsNullOrEmpty(r.Password)) old.EncryptedPassword = DataStore.Encrypt(r.Password);
                    if (!string.IsNullOrEmpty(r.EmailPassword)) old.EncryptedEmailPassword = DataStore.Encrypt(r.EmailPassword);
                    break;
                case "import":
                    var parsed = ImportParser.Parse(r.Text); var errors = parsed.Where(x => !x.IsValid).Select(x => "第 " + x.LineNumber + " 行：" + x.Error).ToArray();
                    if (errors.Length > 0) throw new Exception(string.Join("\n", errors));
                    int added = 0, updated = 0, skipped = 0;
                    foreach (var row in parsed) {
                        var incoming = new AccountRecord { Username = row.Username, EncryptedPassword = DataStore.Encrypt(row.Password), Email = row.Email, EncryptedEmailPassword = DataStore.Encrypt(row.EmailPassword), GameId = row.GameId };
                        var duplicate = data.Accounts.FirstOrDefault(x => x.Username.Equals(row.Username, StringComparison.OrdinalIgnoreCase));
                        if (duplicate != null) {
                            if (SteamAccountSync.FillMissingData(duplicate, incoming)) updated++; else skipped++;
                            continue;
                        }
                        data.Accounts.Add(incoming); added++;
                    }
                    extra = new { added, updated, skipped }; break;
                case "delete":
                    data.Accounts.RemoveAll(x => (r.Ids ?? new string[0]).Contains(x.Id));
                    if (!data.Accounts.Any(x => x.Id == data.CurrentAccountId)) data.CurrentAccountId = "";
                    break;
                case "settings":
                    if (r.Settings == null) throw new Exception("设置不能为空。");
                    r.Settings.StartupDelaySeconds = Math.Max(2, Math.Min(120, r.Settings.StartupDelaySeconds));
                    r.Settings.QueryIntervalMilliseconds = Math.Max(500, Math.Min(30000, r.Settings.QueryIntervalMilliseconds));
                    data.Settings = r.Settings; break;
                case "result":
                    foreach (var record in data.Accounts.Where(x => (r.Ids ?? new string[0]).Contains(x.Id) && x.EffectiveGameId.Equals(r.Result.GameId, StringComparison.OrdinalIgnoreCase))) {
                        record.Status = r.Result.Status; record.RawStatus = r.Result.RawStatus;
                        if (r.Result.HasLevel) { record.Level = r.Result.Level; record.Tier = r.Result.Tier > 0 ? (int?)r.Result.Tier : null; }
                        record.LastQueryAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    } break;
                case "login":
                    var selected = data.Accounts.FirstOrDefault(x => (r.Ids ?? new string[0]).Contains(x.Id));
                    if (selected == null) throw new Exception("账号不存在。");
                    loginService = new SteamService();
                    // Status messages are streamed separately from the command reply.
                    loginService.PostLoginStatus += (status, message) => {
                        lock (Json) { Console.WriteLine(Json.Serialize(new { Event = "steam-status", Status = status, Message = message })); Console.Out.Flush(); }
                    };
                    if (selected.IsLocalAccount && (selected.CanUseSavedLogin || SteamService.IsTargetSessionReady(data.Settings.SteamPath, selected.Username)))
                        loginService.LaunchRememberedAsync(data.Settings.SteamPath, selected.Username, data.Settings, CancellationToken.None).GetAwaiter().GetResult();
                    else {
                        string password;
                        try { password = DataStore.Decrypt(selected.EncryptedPassword); } catch { throw new Exception("密码无法解密，请在原 Windows 用户下运行，或编辑账号重新填写密码。"); }
                        if (string.IsNullOrEmpty(password)) throw new Exception("没有可用的本机登录状态，请编辑账号补充密码，或先在 Steam 中登录并勾选记住我。");
                        loginService.LaunchAsync(data.Settings.SteamPath, selected.Username, password, data.Settings, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    selected.LastLoginAt = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    data.CurrentAccountId = SteamService.IsTargetSessionReady(data.Settings.SteamPath, selected.Username) ? selected.Id : "";
                    break;
                default: throw new Exception("未知操作。");
            }
            store.Save(data);
            var accounts = data.Accounts.Select(x => new { x.Id, x.Username, x.Email, x.GameId, x.Level, x.Tier, x.Status, x.RawStatus, x.Note, x.LastLoginAt, x.LastQueryAt, x.IsLocalAccount, x.CanUseSavedLogin, x.HasPassword });
            lock (Json) { Console.WriteLine(Json.Serialize(new { ok = true, data = new { Accounts = accounts, data.Settings, data.CurrentAccountId }, extra })); Console.Out.Flush(); }
            if (loginService != null) loginService.PostLoginCompletion.GetAwaiter().GetResult();
            return 0;
        } catch (Exception ex) { Console.WriteLine(Json.Serialize(new { ok = false, error = ex.Message })); return 1; }
    }
}
