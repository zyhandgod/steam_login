using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SteamLoginLite.Launcher;

internal static class Program
{
    private const string PayloadMagic = "SLPAY001";
    private const int HashLength = 32;
    private const int FooterLength = HashLength + sizeof(long) + 8;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var launcherPath = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法获取当前程序路径。");

            using var launcher = File.Open(launcherPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (launcher.Length <= FooterLength)
            {
                throw new InvalidDataException("程序内未找到运行组件，请重新下载。");
            }

            launcher.Seek(-FooterLength, SeekOrigin.End);
            var footer = new byte[FooterLength];
            launcher.ReadExactly(footer);

            var magic = Encoding.ASCII.GetString(footer, HashLength + sizeof(long), 8);
            if (!string.Equals(magic, PayloadMagic, StringComparison.Ordinal))
            {
                throw new InvalidDataException("程序内运行组件标记无效，请重新下载。");
            }

            var expectedHash = footer.AsSpan(0, HashLength).ToArray();
            var payloadLength = BinaryPrimitives.ReadInt64LittleEndian(
                footer.AsSpan(HashLength, sizeof(long)));
            var payloadOffset = launcher.Length - FooterLength - payloadLength;
            if (payloadLength <= 0 || payloadOffset < 0)
            {
                throw new InvalidDataException("程序内运行组件长度无效，请重新下载。");
            }

            var hashText = Convert.ToHexString(expectedHash).ToLowerInvariant();
            var appRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteamLoginLite");
            var runtimeRoot = Path.Combine(appRoot, "runtime");
            var targetDirectory = Path.Combine(runtimeRoot, hashText[..16]);
            var targetExecutable = Path.Combine(targetDirectory, "SteamLoginLite.exe");
            var markerPath = Path.Combine(targetDirectory, "payload.sha256");

            Directory.CreateDirectory(runtimeRoot);
            using var mutex = new Mutex(false, $"Local\\SteamLoginLite_{hashText[..16]}");
            mutex.WaitOne();
            try
            {
                if (!IsReady(targetExecutable, markerPath, hashText))
                {
                    InstallPayload(
                        launcher,
                        payloadOffset,
                        payloadLength,
                        expectedHash,
                        runtimeRoot,
                        targetDirectory,
                        markerPath,
                        hashText);
                }
            }
            finally
            {
                mutex.ReleaseMutex();
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = targetExecutable,
                WorkingDirectory = targetDirectory,
                UseShellExecute = false
            };
            foreach (var argument in args)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process.Start(startInfo)
                ?? throw new InvalidOperationException("程序启动失败。");
            CleanupOldRuntimes(runtimeRoot, targetDirectory);
            return 0;
        }
        catch (Exception exception)
        {
            MessageBoxW(
                IntPtr.Zero,
                $"Steam切换器无法启动。\n\n{exception.Message}",
                "Steam切换器",
                0x10);
            return 1;
        }
    }

    private static bool IsReady(string executable, string marker, string expectedHash)
    {
        if (!File.Exists(executable) || !File.Exists(marker)) return false;
        return string.Equals(
            File.ReadAllText(marker).Trim(),
            expectedHash,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void InstallPayload(
        FileStream launcher,
        long payloadOffset,
        long payloadLength,
        byte[] expectedHash,
        string runtimeRoot,
        string targetDirectory,
        string markerPath,
        string hashText)
    {
        var token = Guid.NewGuid().ToString("N");
        var temporaryZip = Path.Combine(runtimeRoot, $"payload-{token}.zip");
        var temporaryDirectory = Path.Combine(runtimeRoot, $"extract-{token}");

        try
        {
            launcher.Position = payloadOffset;
            using (var output = File.Create(temporaryZip))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[1024 * 1024];
                var remaining = payloadLength;
                while (remaining > 0)
                {
                    var read = launcher.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0) throw new EndOfStreamException("运行组件读取不完整。");
                    output.Write(buffer, 0, read);
                    hasher.AppendData(buffer, 0, read);
                    remaining -= read;
                }

                var actualHash = hasher.GetHashAndReset();
                if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                {
                    throw new InvalidDataException("运行组件校验失败，请重新下载程序。");
                }
            }

            Directory.CreateDirectory(temporaryDirectory);
            ZipFile.ExtractToDirectory(temporaryZip, temporaryDirectory, true);

            if (Directory.Exists(targetDirectory)) Directory.Delete(targetDirectory, true);
            Directory.Move(temporaryDirectory, targetDirectory);
            File.WriteAllText(markerPath, hashText);
        }
        finally
        {
            if (File.Exists(temporaryZip)) File.Delete(temporaryZip);
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        }
    }

    private static void CleanupOldRuntimes(string runtimeRoot, string currentDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(runtimeRoot))
        {
            if (string.Equals(directory, currentDirectory, StringComparison.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileName(directory);
            if (name.Length != 16 || !name.All(Uri.IsHexDigit)) continue;
            try
            {
                Directory.Delete(directory, true);
            }
            catch
            {
                // 正在运行的旧版本可能仍锁定文件，下次启动时再清理。
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
