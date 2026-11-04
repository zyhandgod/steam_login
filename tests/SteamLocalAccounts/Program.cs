using System;
using System.IO;
using System.Linq;
using System.Text;
using SteamLoginLite.Services;

static class LocalAccountsTest
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected invalid VDF rejection"); }
    static void Main()
    {
        const string first = "76561197960265851", second = "76561197960266184";
        var fixture = "\"users\"\n{\n\t\"" + first + "\"\n\t{\n\t\t\"AccountName\" \"Player\"\n\t\t\"PersonaName\" \"中文 { \\\"昵称\\\" }\"\n\t\t\"RememberPassword\" \"1\"\n\t\t\"AllowAutoLogin\" \"1\"\n\t\t\"MostRecent\" \"1\"\n\t\t\"Token\" \"fixture-do-not-copy\"\n\t}\n\t// comment { ignored }\n\t\"" + second + "\"\n\t{\n\t\t\"AccountName\" \"second\"\n\t\t\"RememberPassword\" \"1\"\n\t\t\"AllowAutoLogin\" \"1\"\n\t}\n}\n";
        var accounts = SteamLocalAccounts.Parse(fixture);
        Check(accounts.Count == 2 && accounts[0].Username == "Player" && accounts[1].CanUseSavedLogin, "List all accounts without requiring MostRecent");
        Check(!SteamLocalAccounts.Parse(fixture.Replace("\"AllowAutoLogin\" \"1\"", "\"AllowAutoLogin\" \"0\""))[0].CanUseSavedLogin, "Disabled auto-login not eligible");
        Check(!SteamLocalAccounts.Parse(fixture.Replace("\"RememberPassword\" \"1\"", "\"RememberPassword\" \"0\""))[0].CanUseSavedLogin, "Forgotten password not eligible");
        Check(!SteamLocalAccounts.Parse(fixture.Replace("\"RememberPassword\" \"1\"", ""))[0].CanUseSavedLogin, "Missing flag not eligible");
        var updated = SteamLocalAccounts.SelectRememberedAccount(fixture, "SECOND");
        Check(SteamVdfConfig.ReadValue(updated, new[] { "users", first }, "MostRecent") == "0", "Previous account deselected");
        Check(SteamVdfConfig.ReadValue(updated, new[] { "users", second }, "MostRecent") == "1", "Selected account marked");
        Check(SteamVdfConfig.ReadValue(updated, new[] { "users", first }, "Token") == "fixture-do-not-copy", "Unrelated data preserved");
        Check(SteamLocalAccounts.SelectRememberedAccount(updated, "second") == updated, "Selection idempotent");
        try { SteamLocalAccounts.SelectRememberedAccount(fixture, "missing"); throw new Exception("missing accepted"); } catch (InvalidOperationException) { }
        try { SteamLocalAccounts.SelectRememberedAccount(fixture.Replace("\"AllowAutoLogin\" \"1\"", "\"AllowAutoLogin\" \"0\""), "Player"); throw new Exception("disabled accepted"); } catch (InvalidOperationException) { }
        Reject(() => SteamLocalAccounts.Parse(fixture.Replace("\"second\"", "\"PLAYER\"")));
        Reject(() => SteamLocalAccounts.Parse(fixture.Replace("\"AccountName\" \"Player\"", "\"AccountName\" \"Player\" \"AccountName\" \"other\"")));
        Reject(() => SteamLocalAccounts.Parse(fixture.Substring(0, fixture.Length - 3)));
        Reject(() => SteamLocalAccounts.Parse("\"users\" \"bad\""));
        var root = Path.Combine(Path.GetTempPath(), "steam-local-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "config"));
        var path = Path.Combine(root, "config", "loginusers.vdf");
        var exe = Path.Combine(root, "steam.exe");
        try
        {
            Check(SteamLocalAccounts.Read(exe).Count == 0 && SteamLocalAccounts.Read("").Count == 0, "Missing records safe");
            File.WriteAllText(path, fixture, new UTF8Encoding(true));
            var original = File.ReadAllBytes(path);
            SteamLocalAccounts.PrepareLogin(exe, "second");
            Check(File.ReadAllBytes(path + ".steam-login-lite.bak").SequenceEqual(original), "Original backup preserved");
            Check(File.ReadAllBytes(path).Take(3).SequenceEqual(new byte[] {239,187,191}), "BOM preserved");
            SteamLocalAccounts.PrepareLogin(exe, "Player");
            Check(File.ReadAllBytes(path + ".steam-login-lite.bak").SequenceEqual(original), "Backup never overwritten");
            var before = File.ReadAllBytes(path);
            try { SteamLocalAccounts.PrepareLogin(exe, "unknown"); } catch (InvalidOperationException) { }
            Check(File.ReadAllBytes(path).SequenceEqual(before), "Failed selection makes no changes");
            Check(!File.Exists(path + ".steam-login-lite.tmp"), "Temporary file cleaned");
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine("PASS: local account enumeration, missing/disabled flags, exact selection, no-MostRecent records, comments/escaping, duplicates/malformed VDF rejection, preservation, BOM, backup, idempotence and atomic write.");
    }
}
