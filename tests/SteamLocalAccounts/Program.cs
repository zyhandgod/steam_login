using System;
using System.IO;
using System.Linq;
using System.Text;
using SteamLoginLite.Services;

static class LocalAccountsTest
{
    const string First = "76561197960265851", Second = "76561197960266184";
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Expected invalid VDF rejection"); }
    static bool Flag(SteamLocalAccount account, string name)
    {
        var property = typeof(SteamLocalAccount).GetProperty(name);
        Check(property != null, "Expose parsed " + name + " flag");
        return (bool)property.GetValue(account);
    }
    static string NewFormat(string remember = "1") => "\"users\" { \"" + First + "\" { \"AccountName\" \"Player\" \"RememberPassword\" \"" + remember + "\" \"AutoLogin\" \"0\" } \"" + Second + "\" { \"AccountName\" \"second\" \"RememberPassword\" \"1\" \"AutoLogin\" \"1\" } }";
    static void NewFormatEligibility()
    {
        var accounts = SteamLocalAccounts.Parse(NewFormat());
        Check(accounts[0].CanUseSavedLogin && accounts[1].CanUseSavedLogin, "New AutoLogin-only records retain remembered credentials even when not selected");
        Check(Flag(accounts[0], "RememberPassword") && !Flag(accounts[0], "AutoLogin") && !Flag(accounts[0], "AllowAutoLogin") && !Flag(accounts[0], "MostRecent"), "New unselected record exposes false selection flags");
        Check(Flag(accounts[1], "AutoLogin") && Flag(accounts[1], "AllowAutoLogin") && Flag(accounts[1], "MostRecent"), "Missing legacy fields fall back to AutoLogin");
        Check(!SteamLocalAccounts.Parse(NewFormat("0"))[0].CanUseSavedLogin, "New format RememberPassword=0 is not eligible");
        Check(!SteamLocalAccounts.Parse(NewFormat().Replace("\"RememberPassword\" \"1\"", ""))[0].CanUseSavedLogin, "Missing RememberPassword is not eligible");
        var noSelection = SteamLocalAccounts.Parse(NewFormat().Replace("\"AutoLogin\" \"0\"", "").Replace("\"AutoLogin\" \"1\"", ""));
        Check(noSelection.All(a => a.CanUseSavedLogin && !Flag(a, "AutoLogin") && !Flag(a, "AllowAutoLogin") && !Flag(a, "MostRecent")), "Missing selection flags do not discard remembered credentials");
    }
    static void ConflictingFlags()
    {
        var source = NewFormat().Replace("\"AutoLogin\" \"0\"", "\"AutoLogin\" \"0\" \"AllowAutoLogin\" \"1\" \"MostRecent\" \"1\"")
            .Replace("\"AutoLogin\" \"1\"", "\"AutoLogin\" \"1\" \"AllowAutoLogin\" \"0\" \"MostRecent\" \"0\"");
        var accounts = SteamLocalAccounts.Parse(source);
        Check(accounts.All(a => a.CanUseSavedLogin), "AllowAutoLogin=0 is a switch, not absent credentials");
        Check(!Flag(accounts[0], "AutoLogin") && Flag(accounts[0], "AllowAutoLogin") && Flag(accounts[0], "MostRecent"), "Explicit AutoLogin=0 takes precedence over legacy flag");
        Check(Flag(accounts[1], "AutoLogin") && !Flag(accounts[1], "AllowAutoLogin") && !Flag(accounts[1], "MostRecent"), "Explicit legacy fields retain their own values");
        var empty = SteamLocalAccounts.Parse(NewFormat().Replace("\"AutoLogin\" \"0\"", "\"AutoLogin\" \"\" \"AllowAutoLogin\" \"1\""))[0];
        Check(!Flag(empty, "AutoLogin"), "An explicit empty AutoLogin is false, not a missing-field fallback");
    }
    static void NewFormatSelection()
    {
        var source = NewFormat() + "\n// trailing comment\n";
        source = source.Replace("\"AccountName\" \"Player\"", "\"AccountName\" \"Player\" \"WantsOfflineMode\" \"1\" \"SkipOfflineModeWarning\" \"1\" \"Token\" \"test-fixture\"");
        var updated = SteamLocalAccounts.SelectRememberedAccount(source, "PLAYER");
        foreach (var id in new[] { First, Second })
        {
            var path = new[] { "users", id };
            Check(SteamVdfConfig.ReadValue(updated, path, "AutoLogin") == (id == First ? "1" : "0"), "Only target AutoLogin selected");
            Check(SteamVdfConfig.ReadValue(updated, path, "MostRecent") == (id == First ? "1" : "0"), "Legacy MostRecent matches selection");
            Check(SteamVdfConfig.ReadValue(updated, path, "AllowAutoLogin") == "1", "Legacy AllowAutoLogin enabled for valid accounts");
            Check(SteamVdfConfig.ReadValue(updated, path, "RememberPassword") == "1", "RememberPassword preserved");
        }
        foreach (var key in new[] { "WantsOfflineMode", "SkipOfflineModeWarning" })
            Check(SteamVdfConfig.ReadValue(updated, new[] { "users", First }, key) == "1", "Offline preference preserved");
        Check(SteamVdfConfig.ReadValue(updated, new[] { "users", First }, "Token") == "test-fixture" && updated.EndsWith("// trailing comment\n"), "Unrelated data and comments preserved");
        Check(SteamLocalAccounts.SelectRememberedAccount(updated, "Player") == updated, "New selection idempotent");
        var noRemember = NewFormat("0");
        try { SteamLocalAccounts.SelectRememberedAccount(noRemember, "Player"); throw new Exception("Forgotten account accepted"); } catch (InvalidOperationException) { }
        var selectingOther = SteamLocalAccounts.SelectRememberedAccount(noRemember, "second");
        Check(SteamVdfConfig.ReadValue(selectingOther, new[] { "users", First }, "RememberPassword") == "0" && !SteamLocalAccounts.Parse(selectingOther)[0].CanUseSavedLogin, "Selecting another account does not invent remembered credentials");
    }
    static void ChooserConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "steam-chooser-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "config"));
        var login = Path.Combine(root, "config", "loginusers.vdf");
        var config = Path.Combine(root, "config", "config.vdf");
        var exe = Path.Combine(root, "steam.exe");
        var legacy = NewFormat().Replace("\"AutoLogin\" \"0\"", "\"AllowAutoLogin\" \"1\"").Replace("\"AutoLogin\" \"1\"", "\"AllowAutoLogin\" \"1\"");
        try
        {
            File.WriteAllText(login, legacy);
            var original = "\"InstallConfigStore\" { \"keep\" \"unchanged\" }\r\n\"WebStorage\"\r\n{\r\n\t// chooser preference\r\n\t\"Auth\"\r\n\t{\r\n\t\t\"AlwaysShowUserChooser\" \"1\"\r\n\t\t\"RememberedAccount\" \"fixture\"\r\n\t}\r\n}\r\n";
            File.WriteAllText(config, original, new UTF8Encoding(true));
            var bytes = File.ReadAllBytes(config);
            SteamLocalAccounts.PrepareLogin(exe, "second");
            var updated = File.ReadAllText(config);
            Check(SteamVdfConfig.ReadValue(updated, new[] { "WebStorage", "Auth" }, "AlwaysShowUserChooser") == "0", "Existing Steam chooser is disabled");
            Check(updated.Contains("// chooser preference") && updated.Contains("\"keep\" \"unchanged\"") && updated.Contains("\"RememberedAccount\" \"fixture\""), "Chooser update preserves other roots, values and comments");
            Check(File.ReadAllBytes(config).Take(3).SequenceEqual(new byte[] {239,187,191}), "Chooser BOM preserved");
            Check(File.ReadAllBytes(config + ".steam-login-lite.bak").SequenceEqual(bytes), "Chooser original backup preserved");
            var after = File.ReadAllBytes(config);
            SteamLocalAccounts.PrepareLogin(exe, "Player");
            Check(File.ReadAllBytes(config).SequenceEqual(after), "Chooser idempotent");
            Check(File.ReadAllBytes(config + ".steam-login-lite.bak").SequenceEqual(bytes), "Chooser backup never overwritten");
            foreach (var missing in new[] { "\"InstallConfigStore\" { \"keep\" \"same\" }", "\"WebStorage\" { \"Other\" \"same\" }" })
            {
                File.WriteAllText(config, missing);
                SteamLocalAccounts.PrepareLogin(exe, "second");
                Check(File.ReadAllText(config) == missing, "Absent WebStorage/Auth is skipped without creating a tree");
            }
            File.WriteAllText(config, "\"WebStorage\" { \"Auth\" { \"RememberedAccount\" \"fixture\" } }");
            SteamLocalAccounts.PrepareLogin(exe, "second");
            Check(SteamVdfConfig.ReadValue(File.ReadAllText(config), new[] { "WebStorage", "Auth" }, "AlwaysShowUserChooser") == "0", "Existing Auth receives missing chooser preference");
            foreach (var malformed in new[] { "\"WebStorage\" { \"Auth\" {", "\"WebStorage\" { \"Auth\" \"wrong type\" }", "\"WebStorage\" { \"Auth\" { } \"Auth\" { } }", "\"WebStorage\" { \"Auth\" { \"AlwaysShowUserChooser\" \"1\" \"AlwaysShowUserChooser\" \"0\" } }" })
            {
                File.WriteAllText(config, malformed);
                var beforeLogin = File.ReadAllBytes(login);
                Reject(() => SteamLocalAccounts.PrepareLogin(exe, "Player"));
                Check(File.ReadAllText(config) == malformed && File.ReadAllBytes(login).SequenceEqual(beforeLogin), "Malformed chooser rejected before changing either file");
            }
            File.Delete(config);
            SteamLocalAccounts.PrepareLogin(exe, "Player");
            Check(!File.Exists(config), "Missing config.vdf is not created");
            Check(!File.Exists(config + ".steam-login-lite.tmp") && !File.Exists(login + ".steam-login-lite.tmp"), "All temporary files cleaned");
        }
        finally { Directory.Delete(root, true); }
    }
    static void WrappedChooserConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "steam-wrapped-chooser-test-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "config"));
        var login = Path.Combine(root, "config", "loginusers.vdf");
        var config = Path.Combine(root, "config", "config.vdf");
        var exe = Path.Combine(root, "steam.exe");
        try
        {
            File.WriteAllText(login, NewFormat().Replace("\"AutoLogin\" \"0\"", "\"AllowAutoLogin\" \"1\"").Replace("\"AutoLogin\" \"1\"", "\"AllowAutoLogin\" \"1\""));
            var original = "\"InstallConfigStore\"\n{\n\t\"Software\" { \"Valve\" { \"Steam\" { \"Language\" \"schinese\" } } }\n\t\"WebStorage\"\n\t{\n\t\t\"Auth\"\n\t\t{\n\t\t\t\"AlwaysShowUserChooser\" \"1\"\n\t\t\t\"other\" \"keep\"\n\t\t}\n\t}\n}\n";
            File.WriteAllText(config, original, new UTF8Encoding(false));
            SteamLocalAccounts.PrepareLogin(exe, "second");
            var updated = File.ReadAllText(config);
            Check(SteamVdfConfig.ReadValue(updated, new[] { "InstallConfigStore", "WebStorage", "Auth" }, "AlwaysShowUserChooser") == "0", "Actual InstallConfigStore/WebStorage/Auth chooser is disabled");
            Check(SteamVdfConfig.ReadValue(updated, new[] { "InstallConfigStore", "Software", "Valve", "Steam" }, "Language") == "schinese" && updated.Contains("\"other\" \"keep\""), "Wrapped chooser preserves Steam settings");
            Check(!File.ReadAllBytes(config).Take(3).SequenceEqual(new byte[] {239,187,191}), "Wrapped chooser retains absence of BOM");
            foreach (var missing in new[] { "\"InstallConfigStore\" { \"Software\" { } }", "\"InstallConfigStore\" { \"WebStorage\" { \"Other\" \"keep\" } }" })
            {
                File.WriteAllText(config, missing);
                SteamLocalAccounts.PrepareLogin(exe, "Player");
                Check(File.ReadAllText(config) == missing, "Wrapped missing chooser path is skipped");
            }
            foreach (var malformed in new[] { "\"InstallConfigStore\" { \"WebStorage\" \"wrong type\" }", "\"InstallConfigStore\" { \"WebStorage\" { \"Auth\" { } \"Auth\" { } } }", "\"InstallConfigStore\" { \"WebStorage\" { \"Auth\" { } } \"Other\" { broken } }" })
            {
                File.WriteAllText(config, malformed);
                var beforeLogin = File.ReadAllBytes(login);
                Reject(() => SteamLocalAccounts.PrepareLogin(exe, "second"));
                Check(File.ReadAllText(config) == malformed && File.ReadAllBytes(login).SequenceEqual(beforeLogin), "Malformed wrapped configuration is unchanged");
            }
        }
        finally { Directory.Delete(root, true); }
    }
    static void AtomicWriteConflict()
    {
        var root = Path.Combine(Path.GetTempPath(), "steam-vdf-conflict-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "config.vdf");
        try
        {
            var old = "\"WebStorage\" { \"Auth\" { \"AlwaysShowUserChooser\" \"1\" } }";
            File.WriteAllText(path, old, new UTF8Encoding(false));
            var snapshot = File.ReadAllBytes(path);
            var current = old + "\n// Steam changed this file\n";
            File.WriteAllText(path, current, new UTF8Encoding(false));
            try { SteamVdfConfig.WriteUpdatedFile(path, snapshot, old.Replace("\"1\"", "\"0\"")); throw new Exception("Stale snapshot overwrote Steam changes"); } catch (IOException) { }
            Check(File.ReadAllText(path) == current && !File.Exists(path + ".steam-login-lite.tmp"), "Atomic write refuses a stale snapshot and cleans its temp file");
        }
        finally { Directory.Delete(root, true); }
    }
    static void Main()
    {
        var failed = 0;
        foreach (var test in new Action[] { NewFormatEligibility, ConflictingFlags, NewFormatSelection, ChooserConfiguration, WrappedChooserConfiguration, AtomicWriteConflict })
        {
            try { test(); Console.WriteLine("PASS: " + test.Method.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL: " + test.Method.Name + ": " + ex.Message); }
        }
        if (failed > 0) throw new Exception(failed + " compatibility regression tests failed");
        const string first = "76561197960265851", second = "76561197960266184";
        var fixture = "\"users\"\n{\n\t\"" + first + "\"\n\t{\n\t\t\"AccountName\" \"Player\"\n\t\t\"PersonaName\" \"中文 { \\\"昵称\\\" }\"\n\t\t\"RememberPassword\" \"1\"\n\t\t\"AllowAutoLogin\" \"1\"\n\t\t\"MostRecent\" \"1\"\n\t\t\"Token\" \"fixture-do-not-copy\"\n\t}\n\t// comment { ignored }\n\t\"" + second + "\"\n\t{\n\t\t\"AccountName\" \"second\"\n\t\t\"RememberPassword\" \"1\"\n\t\t\"AllowAutoLogin\" \"1\"\n\t}\n}\n";
        var accounts = SteamLocalAccounts.Parse(fixture);
        Check(accounts.Count == 2 && accounts[0].Username == "Player" && accounts[1].CanUseSavedLogin, "List all accounts without requiring MostRecent");
        Check(SteamLocalAccounts.Parse(fixture.Replace("\"AllowAutoLogin\" \"1\"", "\"AllowAutoLogin\" \"0\""))[0].CanUseSavedLogin, "Disabled auto-login does not discard remembered credentials");
        Check(Flag(accounts[0], "AutoLogin") && Flag(accounts[0], "AllowAutoLogin") && Flag(accounts[0], "MostRecent"), "Old AllowAutoLogin remains supported");
        Check(!SteamLocalAccounts.Parse(fixture.Replace("\"RememberPassword\" \"1\"", "\"RememberPassword\" \"0\""))[0].CanUseSavedLogin, "Forgotten password not eligible");
        Check(!SteamLocalAccounts.Parse(fixture.Replace("\"RememberPassword\" \"1\"", ""))[0].CanUseSavedLogin, "Missing flag not eligible");
        var updated = SteamLocalAccounts.SelectRememberedAccount(fixture, "SECOND");
        Check(SteamVdfConfig.ReadValue(updated, new[] { "users", first }, "MostRecent") == "0", "Previous account deselected");
        Check(SteamVdfConfig.ReadValue(updated, new[] { "users", second }, "MostRecent") == "1", "Selected account marked");
        Check(SteamVdfConfig.ReadValue(updated, new[] { "users", first }, "Token") == "fixture-do-not-copy", "Unrelated data preserved");
        Check(SteamLocalAccounts.SelectRememberedAccount(updated, "second") == updated, "Selection idempotent");
        try { SteamLocalAccounts.SelectRememberedAccount(fixture, "missing"); throw new Exception("missing accepted"); } catch (InvalidOperationException) { }
        Check(SteamVdfConfig.ReadValue(SteamLocalAccounts.SelectRememberedAccount(fixture.Replace("\"AllowAutoLogin\" \"1\"", "\"AllowAutoLogin\" \"0\""), "Player"), new[] { "users", first }, "AllowAutoLogin") == "1", "Selection re-enables legacy switch for remembered accounts");
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
