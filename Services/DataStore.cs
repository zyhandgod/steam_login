using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using SteamLoginLite.Models;

namespace SteamLoginLite.Services
{
    public sealed class DataStore
    {
        private readonly string _directory;
        private readonly string _path;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        public DataStore()
        {
            _directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
            _path = Path.Combine(_directory, "accounts.json");
        }

        public AppData Load()
        {
            try
            {
                if (!File.Exists(_path)) return new AppData();
                var result = _json.Deserialize<AppData>(File.ReadAllText(_path, Encoding.UTF8)) ?? new AppData();
                if (result.Accounts == null) result.Accounts = new System.Collections.Generic.List<AccountRecord>();
                if (result.Settings == null) result.Settings = new AppSettings();
                return result;
            }
            catch (Exception ex)
            {
                TryBackupBrokenFile();
                throw new InvalidDataException("账号数据读取失败，原文件已备份。", ex);
            }
        }

        public void Save(AppData data)
        {
            Directory.CreateDirectory(_directory);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, _json.Serialize(data), new UTF8Encoding(false));
            if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak", true);
            else File.Move(temp, _path);
        }

        public static string Encrypt(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }

        public static string Decrypt(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }

        private void TryBackupBrokenFile()
        {
            try
            {
                if (File.Exists(_path)) File.Copy(_path, _path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
            }
            catch { }
        }
    }
}
