using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SteamLoginLite.Services
{
    public sealed class SteamLocalAccount
    {
        public string SteamId { get; set; }
        public string Username { get; set; }
        public bool CanUseSavedLogin { get; set; }
    }

    public static class SteamLocalAccounts
    {
        public static string RecordPath(string steamPath) => Path.Combine(Path.GetDirectoryName(steamPath), "config", "loginusers.vdf");

        public static List<SteamLocalAccount> Read(string steamPath)
        {
            if (string.IsNullOrWhiteSpace(steamPath)) return new List<SteamLocalAccount>();
            var path = RecordPath(steamPath);
            return File.Exists(path) ? Parse(File.ReadAllText(path, new UTF8Encoding(false, true))) : new List<SteamLocalAccount>();
        }

        public static List<SteamLocalAccount> Parse(string content)
        {
            var accounts = new List<SteamLocalAccount>();
            var usernames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in SteamVdfConfig.ChildBlocks(content, "users"))
            {
                ulong number;
                if (!ulong.TryParse(id, out number) || number <= 76561197960265728UL || number > 76561202255233023UL) continue;
                var path = new[] { "users", id };
                var name = SteamVdfConfig.ReadValue(content, path, "AccountName").Trim();
                if (name.Length == 0) continue;
                if (!usernames.Add(name)) throw new InvalidDataException("Steam 登录记录包含重复账号名，已停止同步。");
                accounts.Add(new SteamLocalAccount { SteamId = id, Username = name,
                    CanUseSavedLogin = SteamVdfConfig.ReadValue(content, path, "AllowAutoLogin") == "1"
                        && SteamVdfConfig.ReadValue(content, path, "RememberPassword") == "1" });
            }
            return accounts;
        }

        internal static string SelectRememberedAccount(string content, string username)
        {
            var accounts = Parse(content);
            var target = accounts.SingleOrDefault(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (target == null || !target.CanUseSavedLogin)
                throw new InvalidOperationException("本机没有可用的已记住登录记录，请先在 Steam 登录并勾选记住我，或编辑账号补充密码。");
            foreach (var account in accounts)
                content = SteamVdfConfig.EnsureValue(content, new[] { "users", account.SteamId }, "MostRecent", account == target ? "1" : "0");
            return content;
        }

        public static void PrepareLogin(string steamPath, string username)
        {
            var path = RecordPath(steamPath);
            var original = File.ReadAllBytes(path);
            var bom = original.Length >= 3 && original[0] == 239 && original[1] == 187 && original[2] == 191;
            var content = new UTF8Encoding(false, true).GetString(original, bom ? 3 : 0, original.Length - (bom ? 3 : 0));
            var updated = SelectRememberedAccount(content, username);
            if (content == updated) return;
            var temp = path + ".steam-login-lite.tmp";
            try
            {
                File.WriteAllText(temp, updated, new UTF8Encoding(bom));
                if (!File.Exists(path + ".steam-login-lite.bak")) File.Copy(path, path + ".steam-login-lite.bak");
                if (!original.SequenceEqual(File.ReadAllBytes(path))) throw new IOException("Steam 登录记录正在变化，请重试。");
                File.Replace(temp, path, null, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
