using System;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

[assembly: InternalsVisibleTo("SteamLoginLite.NativeSmoke")]
namespace SteamLoginLite
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var preview = args.Contains("--preview");
            using (var mutex = new Mutex(true, preview ? "Local\\SteamLoginLite.Native.Preview" : "Local\\SteamLoginLite.Native", out var first))
            {
                if (!first) { MessageBox.Show("Steam Login Lite 已在运行，请先关闭原窗口。", "提示"); return; }
                Application.ThreadException += (_, error) => MessageBox.Show(error.Exception.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Application.Run(new MainForm(preview));
            }
        }
    }
}
