using System;
using System.IO;
using System.Reflection;
using SteamLoginLite.Services;
using SteamLoginLite.Models;

class SessionTest
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main()
    {
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
        Console.WriteLine("PASS: no-MostRecent VDF, active-session matching, stale registry, first login, wrong-account rejection.");
    }
}
