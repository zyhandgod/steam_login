using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace SteamLoginLite.Services
{
    public static class WebView2Runtime
    {
        public const string DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
        public static string AvailableVersion()
        {
            try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (WebView2RuntimeNotFoundException) { return ""; }
        }

        public static async Task EnsureAsync(Action<string> status, CancellationToken token)
        {
            if (!string.IsNullOrEmpty(AvailableVersion())) return;
            status("首次查询需要 Microsoft WebView2，正在下载官方安装器（只需安装一次）…");
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamLoginLite", "installer");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "WebView2-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
                using (var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();
                    if (response.RequestMessage.RequestUri.Scheme != "https") throw new InvalidOperationException("WebView2 安装器下载地址不安全。");
                    using (var source = await response.Content.ReadAsStreamAsync())
                    using (var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[65536]; var total = 0; int count;
                        while ((count = await source.ReadAsync(buffer, 0, buffer.Length, token)) != 0)
                        {
                            total += count;
                            if (total > 10 * 1024 * 1024) throw new InvalidOperationException("安装器下载大小异常，已停止。");
                            await target.WriteAsync(buffer, 0, count, token);
                        }
                    }
                }
                token.ThrowIfCancellationRequested();
                VerifyMicrosoftInstaller(path);
                status("正在安装 Microsoft WebView2，请稍候；Windows 若请求授权，请完成系统提示…");
                using (var process = Process.Start(new ProcessStartInfo(path, "/silent /install") { UseShellExecute = true }))
                {
                    var deadline = DateTime.UtcNow.AddMinutes(5);
                    while (!process.HasExited)
                    {
                        token.ThrowIfCancellationRequested();
                        if (DateTime.UtcNow > deadline) throw new TimeoutException("WebView2 安装尚未完成，请稍后重新查询。");
                        await Task.Delay(500, token);
                    }
                }
                for (var i = 0; i < 10; i++)
                {
                    if (!string.IsNullOrEmpty(AvailableVersion())) { status("WebView2 已就绪，正在继续查询…"); return; }
                    await Task.Delay(500, token);
                }
                throw new InvalidOperationException("WebView2 尚不可用，请重试查询；也可从 Microsoft 官方网站安装 Evergreen Runtime 后重试。");
            }
            finally { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        }

        internal static void VerifyMicrosoftInstaller(string path)
        {
            var file = new TrustFile { Size = (uint)Marshal.SizeOf(typeof(TrustFile)), Path = Marshal.StringToCoTaskMemUni(path) };
            var pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
            try
            {
                Marshal.StructureToPtr(file, pointer, false);
                var data = new TrustData { Size = (uint)Marshal.SizeOf(typeof(TrustData)), UiChoice = 2, UnionChoice = 1, File = pointer, ProviderFlags = 0x1000 };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) throw new InvalidOperationException("WebView2 安装器签名验证失败，未运行下载文件。");
                using (var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path)))
                    if (!certificate.GetNameInfo(X509NameType.SimpleName, false).Equals("Microsoft Corporation", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("安装器不是 Microsoft 发布，已停止安装。");
            }
            finally { Marshal.FreeCoTaskMem(file.Path); Marshal.FreeHGlobal(pointer); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TrustFile { public uint Size; public IntPtr Path; public IntPtr Handle; public IntPtr KnownSubject; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TrustData
        {
            public uint Size; public IntPtr Policy; public IntPtr Sip; public uint UiChoice; public uint Revocation;
            public uint UnionChoice; public IntPtr File; public uint StateAction; public IntPtr State;
            public IntPtr Url; public uint ProviderFlags; public uint UiContext;
        }
        [DllImport("wintrust.dll", ExactSpelling = true)]
        private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    }
}
