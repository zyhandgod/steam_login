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
        Console.WriteLine("PASS: no-MostRecent VDF, active-session matching, stale registry, first login, wrong-account rejection.");
    }
}
