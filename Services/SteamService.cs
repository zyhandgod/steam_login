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
        public Task PostLoginCompletion { get; private set; } = Task.CompletedTask;
        public event Action<string, string> PostLoginStatus;
        private void Report(string status, string message) { PostLoginStatus?.Invoke(status, message); }

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
            token.ThrowIfCancellationRequested();
            Report("stopping", "正在退出 Steam，准备登录 " + username + "…");
            await StopSteamAsync(token);
            await Task.Delay(1200, token);
            TryApplyLoginPreferences(steamPath, username, settings, false);
            token.ThrowIfCancellationRequested();
            Report("starting", "正在启动 Steam，登录 " + username + "…");
            Process.Start(new ProcessStartInfo
            {
                FileName = steamPath,
                Arguments = "-noreactlogin -login " + QuoteArgument(username) + " " + QuoteArgument(password),
                WorkingDirectory = Path.GetDirectoryName(steamPath),
                UseShellExecute = false
            });
            PostLoginCompletion = RunPostLoginActionsAsync(steamPath, username, settings, token);
        }

        public async Task LaunchRememberedAsync(string steamPath, string username, AppSettings settings, CancellationToken token)
        {
            if (!File.Exists(steamPath)) throw new FileNotFoundException("找不到 Steam，请在设置中选择 steam.exe。", steamPath);
            token.ThrowIfCancellationRequested();
            if (!IsTargetSessionReady(steamPath, username))
            {
                // Validate before stopping the running client; re-read after shutdown.
                var target = SteamLocalAccounts.Read(steamPath).Find(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                if (target == null || !target.CanUseSavedLogin)
                    throw new InvalidOperationException("本机登录记录不可用，请先在 Steam 登录并勾选记住我，或编辑账号补充密码。");
                Report("stopping", "正在退出 Steam，准备切换到 " + username + "…");
                await StopSteamAsync(token);
                await Task.Delay(1200, token);
                Report("preparing", "正在设置目标 Steam 账号：" + username + "…");
                SteamLocalAccounts.PrepareLogin(steamPath, username);
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Valve\Steam"))
                    SetAutoLoginUser(key, username);
                TryApplyLoginPreferences(steamPath, username, settings, false);
                token.ThrowIfCancellationRequested();
                Report("starting", "正在启动 Steam，切换到 " + username + "…");
                Process.Start(new ProcessStartInfo { FileName = steamPath, WorkingDirectory = Path.GetDirectoryName(steamPath), UseShellExecute = false });
            }
            PostLoginCompletion = RunPostLoginActionsAsync(steamPath, username, settings, token);
        }

        internal static void SetAutoLoginUser(RegistryKey key, string username)
        {
            if (key == null) throw new InvalidOperationException("无法打开 Steam 登录设置。");
            key.SetValue("AutoLoginUser", username, RegistryValueKind.String);
            key.SetValue("AutoLoginUser_steamchina", username, RegistryValueKind.String);
            key.SetValue("RememberPassword", 1, RegistryValueKind.DWord);
        }

        public static string FindActiveAccountName(string steamPath, System.Collections.Generic.List<SteamLocalAccount> accounts)
        {
            if (!IsSteamSessionReady(steamPath)) return "";
            try
            {
                var active = ActiveAccountId();
                foreach (var account in accounts)
                    if ((uint)(ulong.Parse(account.SteamId) & uint.MaxValue) == active) return account.Username;
            }
            catch { }
            return "";
        }

        private static PreferenceApplyState TryApplyLoginPreferences(string steamPath, string username, AppSettings settings, bool requireStableFile, uint? authenticatedAccountId = null)
        {
            try
            {
                var steamDirectory = Path.GetDirectoryName(steamPath);
                if (string.IsNullOrWhiteSpace(steamDirectory)) return PreferenceApplyState.Pending;
                var steamId = FindSteamIdForAccount(Path.Combine(steamDirectory, "config", "loginusers.vdf"), username);
                if (!steamId.HasValue && !authenticatedAccountId.HasValue) return PreferenceApplyState.Pending;
                var accountId = authenticatedAccountId ?? (uint)(steamId.Value & uint.MaxValue);
                var localConfig = Path.Combine(steamDirectory, "userdata", accountId.ToString(), "config", "localconfig.vdf");
                if (!File.Exists(localConfig)) return PreferenceApplyState.Pending;
                if (requireStableFile && DateTime.UtcNow - File.GetLastWriteTimeUtc(localConfig) < TimeSpan.FromSeconds(2))
                    return PreferenceApplyState.Pending;
                if (!SteamVdfConfig.ApplyLoginPreferences(localConfig, "UserLocalConfigStore", settings.CloseRecommendations, settings.CloseFriendsList, settings.OpenLibrary))
                    return PreferenceApplyState.Pending;
                var shared = Path.Combine(steamDirectory, "userdata", accountId.ToString(), "7", "remote", "sharedconfig.vdf");
                if (File.Exists(shared) && !SteamVdfConfig.ApplyLoginPreferences(shared, "UserRoamingConfigStore", settings.CloseRecommendations, settings.CloseFriendsList, settings.OpenLibrary))
                    return PreferenceApplyState.Pending;
                return PreferenceApplyState.Applied;
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
            var account = SteamLocalAccounts.Parse(File.ReadAllText(vdfPath, new UTF8Encoding(false, true)))
                .Find(a => a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            return account == null ? (ulong?)null : ulong.Parse(account.SteamId);
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

        internal static bool IsTargetSessionReady(string steamPath, string username)
        {
            try
            {
                var steamId = FindSteamIdForAccount(Path.Combine(Path.GetDirectoryName(steamPath), "config", "loginusers.vdf"), username);
                using (var active = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                using (var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var value = active?.GetValue("ActiveUser");
                    var pid = Convert.ToInt32(active?.GetValue("pid") ?? 0);
                    if (pid <= 0 || !string.Equals(Process.GetProcessById(pid).MainModule.FileName, steamPath, StringComparison.OrdinalIgnoreCase)) return false;
                    return MatchesTargetSession(steamId, value == null ? 0 : unchecked((uint)Convert.ToInt64(value)),
                        username, Convert.ToString(steam?.GetValue("AutoLoginUser")));
                }
            }
            catch { return false; }
        }

        internal static bool MatchesTargetSession(ulong? expectedSteamId, uint activeUser, string username, string autoLoginUser)
        {
            if (activeUser == 0) return false;
            // MostRecent is absent in newer clients. A remembered account is
            // matched directly to ActiveUser; a first login may not be in VDF yet.
            if (expectedSteamId.HasValue) return (uint)(expectedSteamId.Value & uint.MaxValue) == activeUser;
            return !string.IsNullOrWhiteSpace(username) && string.Equals(username, autoLoginUser, StringComparison.OrdinalIgnoreCase);
        }

        private static uint ActiveAccountId()
        {
            using (var active = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                return unchecked((uint)Convert.ToInt64(active.GetValue("ActiveUser")));
        }

        private static bool IsSteamSessionReady(string steamPath)
        {
            try {
                using (var active = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess")) {
                    if (Convert.ToInt64(active?.GetValue("ActiveUser") ?? 0) == 0) return false;
                    var process = Process.GetProcessById(Convert.ToInt32(active?.GetValue("pid") ?? 0));
                    return string.Equals(process.MainModule.FileName, steamPath, StringComparison.OrdinalIgnoreCase);
                }
            } catch { return false; }
        }

        private async Task RunPostLoginActionsAsync(string steamPath, string username, AppSettings settings, CancellationToken token)
        {
            try
            {
                Report("waiting", "正在等待 " + username + " 的 Steam 登录；如出现验证，请在 Steam 中完成。");
                await Task.Delay(Math.Max(2, settings.StartupDelaySeconds) * 1000, token);
                var deadline = DateTime.UtcNow.AddSeconds(180);
                DateTime? readyAt = null;
                var authenticated = false;
                var libraryOpened = false;
                var libraryAttempted = false;
                while (DateTime.UtcNow < deadline && !token.IsCancellationRequested)
                {
                    // Window preferences apply to this Steam client, independently
                    // of whether a newly logged-in account has reached loginusers.vdf.
                    CloseMatchingSteamWindows(settings);
                    if (IsTargetSessionReady(steamPath, username))
                    {
                        if (!readyAt.HasValue) { readyAt = DateTime.UtcNow; Report("applying", "Steam 活动会话已就绪，正在处理窗口…"); }
                        // Only account-specific file writes require verified identity.
                        var targetReady = IsTargetSessionReady(steamPath, username);
                        if (targetReady && !authenticated) { authenticated = true; Report("authenticated", "已确认目标 Steam 会话：" + username + "。"); }
                        var state = targetReady
                            ? TryApplyLoginPreferences(steamPath, username, settings, true, ActiveAccountId())
                            : PreferenceApplyState.Pending;
                        // A new account's config may arrive late. It must not block
                        // opening the library or closing the initial popup windows.
                            if (settings.OpenLibrary && !libraryAttempted)
                            {
                                libraryAttempted = true;
                                try
                                {
                                    Process.Start(new ProcessStartInfo { FileName = steamPath, Arguments = "steam://open/library", WorkingDirectory = Path.GetDirectoryName(steamPath), UseShellExecute = false });
                                    libraryOpened = true;
                                }
                                catch (Exception ex) { LogPreferenceWarning(ex); }
                            }
                            // Watch config changes through Steam's initial cloud/config refresh.
                            if (DateTime.UtcNow - readyAt.Value >= TimeSpan.FromSeconds(30))
                            {
                                var successful = !settings.OpenLibrary || libraryOpened;
                                Report(successful ? "completed" : "failed", successful
                                    ? (state == PreferenceApplyState.Applied ? "登录偏好已保存，Steam 窗口处理已结束。" : "Steam 窗口处理已结束；账号配置尚未就绪，未修改其他账号配置。")
                                    : "已尝试处理 Steam 窗口，但部分配置或游戏库操作未完成，请查看 steam-service.log。");
                                if (!successful) LogPreferenceWarning(new InvalidOperationException("登录后处理：配置状态=" + state + "，游戏库启动次数=" + (libraryOpened ? 1 : 0)));
                                return;
                            }
                    }
                    await Task.Delay(1000, token);
                }
                Report("timeout", "等待 " + username + " 登录超时。请在 Steam 完成验证；登录状态失效时，请在 Steam 重新登录并勾选“记住我”后重试。");
            }
            catch (OperationCanceledException) { Report("canceled", "登录后处理已停止。"); }
            catch (Exception ex) { LogPreferenceWarning(ex); Report("failed", "登录后处理失败，请查看 steam-service.log。"); }
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
                if (ShouldCloseSteamWindow(title, settings)) PostMessage(handle, WmClose, IntPtr.Zero, IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }

        internal static bool ShouldCloseSteamWindow(string title, AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(title)) return false;
            var news = settings.CloseRecommendations && ContainsAny(title, "特惠", "特賣", "Steam 新闻", "Steam - 新闻", "Steam News", "Steam - News", "特别优惠", "Special Offers", "What's New", "推荐", "更新消息", "更新新闻", "Update News");
            var friends = settings.CloseFriendsList && ContainsAny(title, "好友列表", "Friends List", "Friends & Chat", "好友与聊天", "Steam 好友", "Steam Friends", "Friends", "好友");
            return news || friends;
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

        internal static string QuoteArgument(string value)
        {
            // Windows argv escaping: backslashes are doubled only before a quote
            // or the closing delimiter, not in the middle of a password.
            var result = new StringBuilder("\"");
            var slashes = 0;
            foreach (var character in value ?? "")
            {
                if (character == '\\') { slashes++; continue; }
                result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
                result.Append(character);
                slashes = 0;
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
