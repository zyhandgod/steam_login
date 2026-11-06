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
        public bool RememberPassword { get; set; }
        public bool AutoLogin { get; set; }
        public bool AllowAutoLogin { get; set; }
        public bool MostRecent { get; set; }
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
                var autoLogin = SteamVdfConfig.ReadOptionalValue(content, path, "AutoLogin");
                var allowAutoLogin = SteamVdfConfig.ReadOptionalValue(content, path, "AllowAutoLogin");
                var selected = (autoLogin ?? allowAutoLogin) == "1";
                var remembered = SteamVdfConfig.ReadValue(content, path, "RememberPassword") == "1";
                accounts.Add(new SteamLocalAccount { SteamId = id, Username = name,
                    RememberPassword = remembered, AutoLogin = selected,
                    AllowAutoLogin = allowAutoLogin == null ? selected : allowAutoLogin == "1",
                    MostRecent = (SteamVdfConfig.ReadOptionalValue(content, path, "MostRecent") ?? (selected ? "1" : "0")) == "1",
                    CanUseSavedLogin = remembered });
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
            {
                var path = new[] { "users", account.SteamId };
                var selected = account == target ? "1" : "0";
                content = SteamVdfConfig.EnsureValue(content, path, "AutoLogin", selected);
                content = SteamVdfConfig.EnsureValue(content, path, "AllowAutoLogin", "1");
                content = SteamVdfConfig.EnsureValue(content, path, "MostRecent", selected);
            }
            return content;
        }

        public static void PrepareLogin(string steamPath, string username)
        {
            var path = RecordPath(steamPath);
            var original = File.ReadAllBytes(path);
            var content = SteamVdfConfig.DecodeUtf8(original);
            var updated = SelectRememberedAccount(content, username);
            var configPath = Path.Combine(Path.GetDirectoryName(path), "config.vdf");
            byte[] configOriginal = null;
            string configUpdated = null;
            if (File.Exists(configPath))
            {
                configOriginal = File.ReadAllBytes(configPath);
                configUpdated = SteamVdfConfig.DecodeUtf8(configOriginal);
                // Validate both documents before writing. Steam normally wraps these
                // nodes in InstallConfigStore; accept unwrapped WebStorage too.
                configUpdated = SteamVdfConfig.EnsureValueInExistingBlock(configUpdated, new[] { "InstallConfigStore", "WebStorage", "Auth" }, "AlwaysShowUserChooser", "0");
                configUpdated = SteamVdfConfig.EnsureValueInExistingBlock(configUpdated, new[] { "WebStorage", "Auth" }, "AlwaysShowUserChooser", "0");
            }
            SteamVdfConfig.WriteUpdatedFile(path, original, updated);
            if (configOriginal != null) SteamVdfConfig.WriteUpdatedFile(configPath, configOriginal, configUpdated);
        }
    }
}
