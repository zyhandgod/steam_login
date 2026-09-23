using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SteamLoginLite.Models;

namespace SteamLoginLite.Services
{
    public sealed class SteamService
    {
        private const uint WmClose = 0x0010;

        public static string FindSteamPath()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var path = Convert.ToString(key?.GetValue("SteamExe"))?.Replace('/', '\\');
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
                }
            }
            catch { }
            foreach (var path in new[] { @"C:\Program Files (x86)\Steam\steam.exe", @"C:\Program Files\Steam\steam.exe" })
                if (File.Exists(path)) return path;
            return "";
        }

        public static string FindMostRecentAccountName(string steamPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(steamPath))
                {
                    var steamDirectory = Path.GetDirectoryName(steamPath);
                    if (!string.IsNullOrWhiteSpace(steamDirectory))
                    {
                        var vdfPath = Path.Combine(steamDirectory, "config", "loginusers.vdf");
                        var recent = ReadMostRecentAccount(vdfPath);
                        if (!string.IsNullOrWhiteSpace(recent)) return recent;
                    }
                }
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var autoLoginUser = Convert.ToString(key?.GetValue("AutoLoginUser"));
                    if (!string.IsNullOrWhiteSpace(autoLoginUser)) return autoLoginUser.Trim();
                }
                var userNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    Environment.UserName,
                    Environment.GetEnvironmentVariable("USER") ?? ""
                };
                foreach (var userName in userNames)
                {
                    if (string.IsNullOrWhiteSpace(userName)) continue;
                    foreach (var vdfPath in new[]
                    {
                        @"Z:\Users\" + userName + @"\Library\Application Support\Steam\config\loginusers.vdf",
                        @"Z:\home\" + userName + @"\.steam\steam\config\loginusers.vdf"
                    })
                    {
                        var recent = ReadMostRecentAccount(vdfPath);
                        if (!string.IsNullOrWhiteSpace(recent)) return recent;
                    }
                }
            }
            catch { }
            return "";
        }

        private static string ReadMostRecentAccount(string vdfPath)
        {
            if (!File.Exists(vdfPath)) return "";
            var content = File.ReadAllText(vdfPath, Encoding.UTF8);
            foreach (Match block in Regex.Matches(content, "\"\\d+\"\\s*\\{(?<body>.*?)\\}", RegexOptions.Singleline))
            {
                var body = block.Groups["body"].Value;
                if (!Regex.IsMatch(body, "\"MostRecent\"\\s*\"1\"", RegexOptions.IgnoreCase)) continue;
                var account = Regex.Match(body, "\"AccountName\"\\s*\"(?<name>[^\"]+)\"", RegexOptions.IgnoreCase);
                if (account.Success) return account.Groups["name"].Value;
            }
            return "";
        }

        public async Task LaunchAsync(string steamPath, string username, string password, AppSettings settings, CancellationToken token)
        {
            if (!File.Exists(steamPath)) throw new FileNotFoundException("找不到 Steam，请在设置中选择 steam.exe。", steamPath);
            await StopSteamAsync(token);
            await Task.Delay(1200, token);
            TryApplyLoginPreferences(steamPath, username, settings, false);
            Process.Start(new ProcessStartInfo
            {
                FileName = steamPath,
                Arguments = "-noreactlogin -login " + Quote(username) + " " + Quote(password),
                WorkingDirectory = Path.GetDirectoryName(steamPath),
                UseShellExecute = false
            });
            _ = RunPostLoginActionsAsync(steamPath, username, settings, token);
        }

        private static PreferenceApplyState TryApplyLoginPreferences(string steamPath, string username, AppSettings settings, bool requireStableFile)
        {
            if (!settings.CloseRecommendations && !settings.CloseFriendsList) return PreferenceApplyState.Applied;
            try
            {
                var steamDirectory = Path.GetDirectoryName(steamPath);
                if (string.IsNullOrWhiteSpace(steamDirectory)) return PreferenceApplyState.Pending;
                var steamId = FindSteamIdForAccount(Path.Combine(steamDirectory, "config", "loginusers.vdf"), username);
                if (!steamId.HasValue) return PreferenceApplyState.Pending;
                var accountId = (uint)(steamId.Value & uint.MaxValue);
                var localConfig = Path.Combine(steamDirectory, "userdata", accountId.ToString(), "config", "localconfig.vdf");
                if (!File.Exists(localConfig)) return PreferenceApplyState.Pending;
                if (requireStableFile && DateTime.UtcNow - File.GetLastWriteTimeUtc(localConfig) < TimeSpan.FromSeconds(2))
                    return PreferenceApplyState.Pending;
                return SteamVdfConfig.ApplyPopupPreferences(localConfig, settings.CloseRecommendations, settings.CloseFriendsList)
                    ? PreferenceApplyState.Applied
                    : PreferenceApplyState.Pending;
            }
            catch (InvalidDataException ex)
            {
                LogPreferenceWarning(ex);
                return PreferenceApplyState.Failed;
            }
            catch (DecoderFallbackException ex)
            {
                LogPreferenceWarning(ex);
                return PreferenceApplyState.Failed;
            }
            catch (UnauthorizedAccessException ex)
            {
                LogPreferenceWarning(ex);
                return PreferenceApplyState.Failed;
            }
            catch (IOException ex)
            {
                LogTransientPreferenceWarning(ex);
                return PreferenceApplyState.Pending;
            }
            catch (Exception ex)
            {
                LogPreferenceWarning(ex);
                return PreferenceApplyState.Failed;
            }
        }

        private static ulong? FindSteamIdForAccount(string vdfPath, string username)
        {
            if (!File.Exists(vdfPath) || string.IsNullOrWhiteSpace(username)) return null;
            var content = File.ReadAllText(vdfPath, Encoding.UTF8);
            foreach (Match block in Regex.Matches(content, "\"(?<steamId>\\d+)\"\\s*\\{(?<body>.*?)\\}", RegexOptions.Singleline))
            {
                var account = Regex.Match(block.Groups["body"].Value, "\"AccountName\"\\s*\"(?<name>[^\"]+)\"", RegexOptions.IgnoreCase);
                ulong steamId;
                if (account.Success && string.Equals(account.Groups["name"].Value, username, StringComparison.OrdinalIgnoreCase) &&
                    ulong.TryParse(block.Groups["steamId"].Value, out steamId)) return steamId;
            }
            return null;
        }

        private static async Task StopSteamAsync(CancellationToken token)
        {
            var processNames = new[] { "steam", "steamwebhelper", "TslGame", "ExecPubg", "TslGame_BE", "zksvc" };
            foreach (var name in processNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try { if (!process.HasExited) process.CloseMainWindow(); } catch { }
                }
            }
            await Task.Delay(900, token);
            foreach (var name in processNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try { if (!process.HasExited) process.Kill(); } catch { }
                }
            }
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline && (Process.GetProcessesByName("steam").Length > 0 || Process.GetProcessesByName("steamwebhelper").Length > 0))
                await Task.Delay(250, token);
            if (Process.GetProcessesByName("steam").Length > 0 || Process.GetProcessesByName("steamwebhelper").Length > 0)
                throw new InvalidOperationException("Steam 未能完全退出，请手动关闭 Steam 后重试。");
        }

        private static async Task RunPostLoginActionsAsync(string steamPath, string username, AppSettings settings, CancellationToken token)
        {
            try
            {
                await Task.Delay(Math.Max(2, settings.StartupDelaySeconds) * 1000, token);
                var deadline = DateTime.UtcNow.AddSeconds(35);
                var libraryOpened = false;
                var preferenceRetriesEnabled = settings.CloseRecommendations || settings.CloseFriendsList;
                while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
                {
                    CloseMatchingSteamWindows(settings);
                    if (preferenceRetriesEnabled)
                    {
                        var state = TryApplyLoginPreferences(steamPath, username, settings, true);
                        if (state == PreferenceApplyState.Failed) preferenceRetriesEnabled = false;
                    }
                    if (settings.OpenLibrary && !libraryOpened)
                    {
                        try { Process.Start(new ProcessStartInfo("steam://open/library") { UseShellExecute = true }); libraryOpened = true; }
                        catch { }
                    }
                    await Task.Delay(1000, token);
                }
            }
            catch { }
        }

        private static void LogPreferenceWarning(Exception exception)
        {
            try
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamLoginLite");
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "steam-service.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " 自动关闭 Steam 弹窗配置失败：" + exception.GetType().Name + " - " + exception.Message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch { }
        }

        private static readonly object PreferenceLogLock = new object();
        private static DateTime _lastTransientPreferenceLogAt = DateTime.MinValue;

        private static void LogTransientPreferenceWarning(Exception exception)
        {
            lock (PreferenceLogLock)
            {
                if (DateTime.UtcNow - _lastTransientPreferenceLogAt < TimeSpan.FromSeconds(10)) return;
                _lastTransientPreferenceLogAt = DateTime.UtcNow;
            }
            LogPreferenceWarning(exception);
        }

        private enum PreferenceApplyState { Pending, Applied, Failed }

        private static void CloseMatchingSteamWindows(AppSettings settings)
        {
            EnumWindows((handle, _) =>
            {
                GetWindowThreadProcessId(handle, out var pid);
                string processName;
                try { processName = Process.GetProcessById((int)pid).ProcessName; } catch { return true; }
                if (!processName.Equals("steam", StringComparison.OrdinalIgnoreCase) && !processName.Equals("steamwebhelper", StringComparison.OrdinalIgnoreCase)) return true;
                var title = GetTitle(handle);
                if (title.Length == 0) return true;
                var closeRecommendation = settings.CloseRecommendations && ContainsAny(title, "Steam 新闻", "Steam News", "特别优惠", "Special Offers", "What's New", "推荐");
                var closeFriends = settings.CloseFriendsList && ContainsAny(title, "好友列表", "Friends List", "Friends & Chat", "好友与聊天");
                if (closeRecommendation || closeFriends) PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }

        private static bool ContainsAny(string value, params string[] candidates)
        {
            foreach (var item in candidates) if (value.IndexOf(item, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static string GetTitle(IntPtr handle)
        {
            var length = GetWindowTextLength(handle);
            if (length <= 0) return "";
            var builder = new StringBuilder(length + 1);
            GetWindowText(handle, builder, builder.Capacity);
            return builder.ToString();
        }

        private static string Quote(string value) => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
