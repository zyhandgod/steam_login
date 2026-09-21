using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SteamLoginLite.Models;

namespace SteamLoginLite.Services
{
    public sealed class DataStore
    {
        private readonly string _directory;
        private readonly string _path;
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };

        public DataStore()
        {
            _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamLoginLite");
            _path = Path.Combine(_directory, "accounts.json");
        }

        public AppData Load()
        {
            try
            {
                TryMigrateLegacyData();
                if (!File.Exists(_path)) return new AppData();
                var result = JsonSerializer.Deserialize<AppData>(File.ReadAllText(_path, Encoding.UTF8), _json) ?? new AppData();
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
            File.WriteAllText(temp, JsonSerializer.Serialize(data, _json), new UTF8Encoding(false));
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

        private void TryMigrateLegacyData()
        {
            if (File.Exists(_path)) return;
            try
            {
                var candidates = new List<string>();
                var baseDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                AddLegacyCandidate(baseDirectory, candidates);

                var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var parent = Directory.GetParent(baseDirectory)?.FullName;
                if (!string.IsNullOrWhiteSpace(parent)) roots.Add(parent);
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrWhiteSpace(desktop)) roots.Add(desktop);
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrWhiteSpace(profile)) roots.Add(Path.Combine(profile, "Downloads"));

                foreach (var root in roots) CollectLegacyCandidates(root, 3, candidates);
                var source = candidates
                    .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(_path), StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(source)) return;
                Directory.CreateDirectory(_directory);
                File.Copy(source, _path, false);
            }
            catch { }
        }

        private static void CollectLegacyCandidates(string directory, int depth, List<string> candidates)
        {
            if (depth < 0 || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
            AddLegacyCandidate(directory, candidates);
            if (depth == 0) return;
            string[] children;
            try { children = Directory.GetDirectories(directory); }
            catch { return; }
            foreach (var child in children)
            {
                var name = Path.GetFileName(child);
                if (name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("obj", StringComparison.OrdinalIgnoreCase)) continue;
                CollectLegacyCandidates(child, depth - 1, candidates);
            }
        }

        private static void AddLegacyCandidate(string applicationDirectory, List<string> candidates)
        {
            try
            {
                var candidate = Path.Combine(applicationDirectory, "data", "accounts.json");
                if (!File.Exists(candidate)) return;
                var hasApplication = File.Exists(Path.Combine(applicationDirectory, "SteamLoginLite.exe")) ||
                                     Directory.GetFiles(applicationDirectory, "*.exe").Any(file => Path.GetFileName(file).IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0);
                if (hasApplication) candidates.Add(candidate);
            }
            catch { }
        }
    }
}
