using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using SteamLoginLite.Services;
using SteamLoginLite.Models;

class SessionTest
{
    [DllImport("shell32.dll", SetLastError = true)]
    static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int count);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr pointer);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main()
    {
        var registryPath = @"Software\SteamLoginLite.Tests\" + Guid.NewGuid().ToString("N");
        try {
            using (var key = Registry.CurrentUser.CreateSubKey(registryPath)) {
                key.SetValue("Unrelated", "preserved");
                key.SetValue("RememberPassword", 0, RegistryValueKind.DWord);
                SteamService.SetAutoLoginUser(key, "fixture-user");
                Check((string)key.GetValue("AutoLoginUser") == "fixture-user" && key.GetValueKind("AutoLoginUser") == RegistryValueKind.String, "Standard Steam user is selected with string type");
                Check((string)key.GetValue("AutoLoginUser_steamchina") == "fixture-user", "Steam China compatibility key follows target");
                Check((int)key.GetValue("RememberPassword") == 1 && key.GetValueKind("RememberPassword") == RegistryValueKind.DWord, "Global remember switch uses DWORD");
                SteamService.SetAutoLoginUser(key, "next-user");
                Check((string)key.GetValue("AutoLoginUser") == "next-user" && (string)key.GetValue("Unrelated") == "preserved", "Reselection preserves unrelated registry values");
            }
        } finally { Registry.CurrentUser.DeleteSubKeyTree(registryPath, false); }
        var arguments = new[] { "", "plain", @"a\b", "with spaces", "with\ttab", "with\"quote", "中文🔑", "trailing\\", "slash\\\"quote", "two\\\\\"slashes" };
        foreach (var argument in arguments) {
            int count;
            var parsedArgs = CommandLineToArgvW("steam.exe " + SteamService.QuoteArgument(argument), out count);
            Check(parsedArgs != IntPtr.Zero, "Windows argument parser available");
            try { Check(count == 2 && Marshal.PtrToStringUni(Marshal.ReadIntPtr(parsedArgs, IntPtr.Size)) == argument, "Password characters round-trip through Windows argv"); }
            finally { LocalFree(parsedArgs); }
        }
        var fakeExe = Path.GetTempFileName();
        try {
            var canceled = new CancellationToken(true);
            foreach (var remembered in new[] { false, true }) {
                var service = new SteamService();
                var rejected = false;
                try {
                    (remembered ? service.LaunchRememberedAsync(fakeExe, "fixture", new AppSettings(), canceled)
                        : service.LaunchAsync(fakeExe, "fixture", "fixture-password", new AppSettings(), canceled)).GetAwaiter().GetResult();
                } catch (OperationCanceledException) { rejected = true; }
                Check(rejected, "Already canceled launch performs no process or Steam configuration operations");
            }
        } finally { File.Delete(fakeExe); }
        var settings = new AppSettings();
        Check(SteamService.ShouldCloseSteamWindow("特惠", settings), "Actual Steam promotion title must close");
        Check(SteamService.ShouldCloseSteamWindow("好友与聊天", settings), "Friends window must close");
        Check(SteamService.ShouldCloseSteamWindow("Friends & Chat", settings), "English friends window must close");
        Check(!SteamService.ShouldCloseSteamWindow("Steam", settings), "Main Steam window must remain open");
        settings.CloseRecommendations = false;
        settings.CloseFriendsList = false;
        Check(!SteamService.ShouldCloseSteamWindow("特惠", settings), "News toggle OFF must be respected");
        Check(!SteamService.ShouldCloseSteamWindow("好友与聊天", settings), "Friends toggle OFF must be respected");
        const ulong steamId = 76561197960265851UL; // account ID 123
        Check(!SteamService.MatchesTargetSession(steamId, 0, "target", "target"), "Remembered user is not a logged-in session");
        Check(SteamService.MatchesTargetSession(steamId, 123, "target", "stale"), "ActiveUser must win over stale login registry");
        Check(!SteamService.MatchesTargetSession(steamId, 456, "target", "target"), "Wrong active account must not be accepted");
        Check(SteamService.MatchesTargetSession(null, 123, "NewUser", "newuser"), "First login without VDF must support account-name fallback");
        Check(!SteamService.MatchesTargetSession(null, 123, "target", "other"), "First login still requires matching username");
        var file = Path.GetTempFileName();
        try {
            File.WriteAllText(file, "\"users\" { \"" + steamId + "\" { \"AccountName\" \"target\" \"timestamp\" \"1\" } }");
            var method = typeof(SteamService).GetMethod("FindSteamIdForAccount", BindingFlags.NonPublic | BindingFlags.Static);
            var parsed = (ulong?)method.Invoke(null, new object[] { file, "target" });
            Check(parsed == steamId, "VDF without MostRecent must resolve the target ID");
            Check(SteamService.MatchesTargetSession(parsed, 123, "target", ""), "MostRecent must not gate post-login actions");
        } finally { File.Delete(file); }
        var data = new AppData();
        var existing = new AccountRecord { Username = "PLAYER", Note = "我的备注", GameId = "nickname", EncryptedPassword = "encrypted-fixture", Level = 123, Status = "正常" };
        data.Accounts.Add(existing);
        var records = new System.Collections.Generic.List<SteamLocalAccount> {
            new SteamLocalAccount { Username = "player", SteamId = steamId.ToString(), CanUseSavedLogin = true },
            new SteamLocalAccount { Username = "second", SteamId = "76561197960266184", CanUseSavedLogin = false }
        };
        SteamAccountSync.Merge(data, records, "player");
        Check(data.Accounts.Count == 2 && data.Accounts[0] == existing, "Case-insensitive sync must not duplicate or replace existing record");
        Check(existing.Note == "我的备注" && existing.GameId == "nickname" && existing.EncryptedPassword == "encrypted-fixture" && existing.Level == 123 && existing.Status == "正常", "Existing data and credentials preserved");
        Check(data.CurrentAccountId == existing.Id && existing.IsLocalAccount && existing.CanUseSavedLogin, "Only verified active account marked current");
        var added = data.Accounts[1];
        Check(added.Note == "" && added.EncryptedPassword == "" && !added.CanUseSavedLogin, "Imported accounts have empty notes and no extracted credentials");
        SteamAccountSync.Merge(data, records, "");
        Check(data.Accounts.Count == 2 && data.CurrentAccountId == "", "Idempotent sync and logged-out state clears current marker");
        SteamAccountSync.Merge(data, new System.Collections.Generic.List<SteamLocalAccount>(), "player");
        Check(!existing.IsLocalAccount && !existing.CanUseSavedLogin && data.Accounts.Count == 2 && data.CurrentAccountId == "", "Moving PC loses local flags, preserves account data");
        var imported = new AccountRecord { Username = "SECOND", EncryptedPassword = "new-encrypted", EncryptedEmailPassword = "email-encrypted", Email = "test@example.com", GameId = "new-nickname", Note = "ignored" };
        added.IsLocalAccount = true;
        Check(SteamAccountSync.FillMissingData(added, imported), "Bulk import fills credentials of synced account");
        Check(added.EncryptedPassword == "new-encrypted" && added.Email == "test@example.com" && added.GameId == "new-nickname" && added.Note == "", "Missing fields filled, note untouched");
        Check(!SteamAccountSync.FillMissingData(added, new AccountRecord { EncryptedPassword = "other", GameId = "other", Email = "other@example.com" }), "Bulk import does not overwrite populated fields");
        existing.IsLocalAccount = false;
        Check(!SteamAccountSync.FillMissingData(existing, imported), "Ordinary duplicate import stays unchanged");
        var json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(data);
        Check(!json.Contains("IsLocalAccount") && !json.Contains("CanUseSavedLogin"), "Local eligibility never persisted or migrated");
        var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
        var deleted = new AppData();
        var removeMe = new AccountRecord { Username = "PLAYER", EncryptedPassword = "deleted-secret-ciphertext", Note = "deleted-note", GameId = "old-nickname", Level = 77 };
        deleted.Accounts.Add(removeMe);
        SteamAccountSync.Merge(deleted, records, "player");
        SteamAccountSync.Remove(deleted, new[] { removeMe.Id });
        Check(deleted.Accounts.Count == 1 && deleted.CurrentAccountId == "", "Delete removes current record and marker");
        var deletedJson = serializer.Serialize(deleted);
        Check(!deletedJson.Contains("deleted-secret-ciphertext") && !deletedJson.Contains("deleted-note"), "Deletion retains no removed credentials or notes");
        for (var i = 0; i < 3; i++) {
            deleted = serializer.Deserialize<AppData>(serializer.Serialize(deleted));
            SteamAccountSync.Merge(deleted, records, "player");
            Check(deleted.Accounts.Count == 1 && deleted.CurrentAccountId == "", "Restart/refresh must not resurrect deleted current account");
        }
        Check(deleted.IgnoredLocalAccounts.Count == 1, "Only the name tombstone persists, no duplicate markers");
        var manuallyAdded = new AccountRecord { Username = "pLaYeR", EncryptedPassword = "new-secret-ciphertext", Note = "new-note" };
        SteamAccountSync.RestoreName(deleted, manuallyAdded.Username);
        deleted.Accounts.Add(manuallyAdded);
        SteamAccountSync.Merge(deleted, records, "PLAYER");
        Check(deleted.Accounts.Count == 2 && deleted.CurrentAccountId == manuallyAdded.Id && manuallyAdded.EncryptedPassword == "new-secret-ciphertext", "Manual re-add clears ignore case-insensitively and preserves new credentials");
        SteamAccountSync.Remove(deleted, new[] { manuallyAdded.Id });
        SteamAccountSync.RestoreName(deleted, "PLAYER");
        var reimported = new AccountRecord { Username = "player", EncryptedPassword = "imported-password" };
        deleted.Accounts.Add(reimported);
        SteamAccountSync.Merge(deleted, records, "player");
        Check(deleted.Accounts.Count == 2 && deleted.CurrentAccountId == reimported.Id, "Explicit import can restore ignored account without duplicate");
        SteamAccountSync.Remove(deleted, deleted.Accounts.ConvertAll(a => a.Id));
        Check(deleted.Accounts.Count == 0 && deleted.IgnoredLocalAccounts.Count == 2, "Batch delete suppresses all selected names");
        SteamAccountSync.Merge(deleted, records, "player");
        Check(deleted.Accounts.Count == 0, "Batch-deleted records stay removed");
        deleted.IgnoredLocalAccounts.Clear();
        SteamAccountSync.Merge(deleted, records, "player");
        Check(deleted.Accounts.Count == 2, "Explicit settings restore can resync local records without passwords");
        var legacy = serializer.Deserialize<AppData>("{\"Accounts\":[],\"Settings\":{}}");
        SteamAccountSync.Merge(legacy, records, "player");
        Check(legacy.Accounts.Count == 2, "Old data without ignore field still syncs normally");
        legacy.IgnoredLocalAccounts = null;
        SteamAccountSync.Remove(legacy, new[] { legacy.Accounts[0].Id });
        SteamAccountSync.Merge(legacy, records, "player");
        Check(legacy.Accounts.Count == 1, "Null legacy field repaired safely");
        Console.WriteLine("PASS: delete/restart suppression, credential removal, case-insensitive manual add/import, batch delete, explicit restore and legacy compatibility.");
        Console.WriteLine("PASS: no-MostRecent VDF, active-session matching, stale registry, first login, wrong-account rejection.");
        Console.WriteLine("PASS: isolated registry selection, DWORD remember flag, Windows password argument round-trip and canceled launch.");
    }
}
