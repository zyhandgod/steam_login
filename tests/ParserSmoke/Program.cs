using System;
using System.IO;
using System.Text;
using SteamLoginLite.Services;

internal static class Program
{
    private static int Main()
    {
        Check("user----pass----mail\\@example.com----123456", "user", "pass", "user", "mail@example.com");
        Check("user---pass---mail@example.com---123456---pubgName", "user", "pass", "pubgName", "mail@example.com");
        Check("user--pass--mail@example.com--123456", "user", "pass", "user", "mail@example.com");
        Check("账号user密码pass邮箱账号mail\\@example.com邮箱密码123456邮箱地址example.com", "user", "pass", "user", "mail@example.com");
        Check("fxols54967--sltm34244M", "fxols54967", "sltm34244M", "fxols54967", "");
        Check("账号fxols54967密码sltm34244M", "fxols54967", "sltm34244M", "fxols54967", "");

        var invalid = ImportParser.Parse("only-one-part");
        if (invalid.Count != 1 || invalid[0].IsValid) return Fail("invalid input accepted");

        var many = ImportParser.Parse("a----b----a@b.com----c\r\nd----e----d@e.com----f");
        if (many.Count != 2 || !many[0].IsValid || !many[1].IsValid) return Fail("multi-line parse failed");

        CheckStatus("Not banned", "正常");
        CheckStatus("Temporarily banned", "临时封禁");
        CheckStatus("TemporaryBan", "临时封禁");
        CheckStatus("Permanently banned", "永久封禁");
        CheckStatus("PermanentBan", "永久封禁");

        CheckSteamPopupPreferences();

        Console.WriteLine("Smoke tests passed: import 8/8, status 5/5, Steam popup preferences 7/7");
        return 0;
    }

    private static void Check(string input, string username, string password, string gameId, string email)
    {
        var result = ImportParser.Parse(input);
        if (result.Count != 1 || !result[0].IsValid || result[0].Username != username || result[0].Password != password || result[0].GameId != gameId || result[0].Email != email)
            throw new Exception("Parse mismatch: " + input);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static void CheckStatus(string raw, string expected)
    {
        var actual = BanStatusNormalizer.Normalize(raw);
        if (actual != expected) throw new Exception("Status mismatch: " + raw + " => " + actual);
    }

    private static void CheckSteamPopupPreferences()
    {
        const string input = "// keep this comment\r\n\"UserLocalConfigStore\"\r\n{\r\n\t\"unrelated\"\t\t\"keep\\\"me\"\r\n\t\"system\"\r\n\t{\r\n\t\t\"news\"\r\n\t\t{\r\n\t\t\t\"NotifyAvailableGames\"\t\t\"1\"\r\n\t\t}\r\n\t}\r\n}\r\n";
        var output = SteamVdfConfig.EnsureValue(input, new[] { "UserLocalConfigStore", "system", "news" }, "NotifyAvailableGames", "0");
        output = SteamVdfConfig.EnsureValue(output, new[] { "UserLocalConfigStore", "friends" }, "SignIntoFriends", "0");
        if (!output.Contains("\"NotifyAvailableGames\"\t\t\"0\"") || !output.Contains("\"SignIntoFriends\"\t\t\"0\"") || !output.Contains("\"unrelated\"\t\t\"keep\\\"me\""))
            throw new Exception("Steam popup preferences were not written correctly.");

        ExpectInvalid("\"UserLocalConfigStore\"\r\n{\r\n", "unmatched brace");
        ExpectInvalid("\"UserLocalConfigStore\" \"scalar\"\r\n", "root scalar conflict");
        ExpectInvalid("\"UserLocalConfigStore\"{}\r\n\"UserLocalConfigStore\"{}\r\n", "duplicate root");
        ExpectInvalid("\"UserLocalConfigStore\"{\"system\" \"scalar\"}\r\n", "child scalar conflict");
        ExpectInvalid("\"UserLocalConfigStore\"{\"system\"{\"news\"{\"NotifyAvailableGames\"\"1\"\"NotifyAvailableGames\"\"0\"}}}\r\n", "duplicate setting");

        var tempDirectory = Path.Combine(Path.GetTempPath(), "SteamLoginLite-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            var path = Path.Combine(tempDirectory, "localconfig.vdf");
            File.WriteAllText(path, input, new UTF8Encoding(true));
            var originalBytes = File.ReadAllBytes(path);
            if (!SteamVdfConfig.ApplyPopupPreferences(path, true, true)) throw new Exception("Popup preference file update failed.");
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 3 || bytes[0] != 0xEF || bytes[1] != 0xBB || bytes[2] != 0xBF) throw new Exception("UTF-8 BOM was not preserved.");
            var backupPath = path + ".steam-login-lite.bak";
            if (!File.Exists(backupPath)) throw new Exception("VDF backup was not created.");
            if (Convert.ToBase64String(File.ReadAllBytes(backupPath)) != Convert.ToBase64String(originalBytes)) throw new Exception("VDF backup does not match the original file.");

            const string invalid = "\"UserLocalConfigStore\"{\"system\" \"scalar\"}";
            File.WriteAllText(path, invalid, new UTF8Encoding(false));
            try
            {
                SteamVdfConfig.ApplyPopupPreferences(path, true, false);
                throw new Exception("Invalid VDF file was accepted.");
            }
            catch (InvalidDataException) { }
            if (File.ReadAllText(path, Encoding.UTF8) != invalid) throw new Exception("Invalid VDF file was modified.");
        }
        finally { try { Directory.Delete(tempDirectory, true); } catch { } }
    }

    private static void ExpectInvalid(string input, string caseName)
    {
        try
        {
            SteamVdfConfig.EnsureValue(input, new[] { "UserLocalConfigStore", "system", "news" }, "NotifyAvailableGames", "0");
            throw new Exception("Invalid VDF accepted: " + caseName);
        }
        catch (InvalidDataException) { }
    }
}
