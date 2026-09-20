using System;
using SteamLoginLite.Services;

internal static class Program
{
    private static int Main()
    {
        Check("user----pass----mail\\@example.com----123456", "user", "user", "mail@example.com");
        Check("user---pass---mail@example.com---123456---pubgName", "user", "pubgName", "mail@example.com");
        Check("user--pass--mail@example.com--123456", "user", "user", "mail@example.com");
        Check("账号user密码pass邮箱账号mail\\@example.com邮箱密码123456邮箱地址example.com", "user", "user", "mail@example.com");

        var invalid = ImportParser.Parse("only-one-part");
        if (invalid.Count != 1 || invalid[0].IsValid) return Fail("invalid input accepted");

        var many = ImportParser.Parse("a----b----a@b.com----c\r\nd----e----d@e.com----f");
        if (many.Count != 2 || !many[0].IsValid || !many[1].IsValid) return Fail("multi-line parse failed");

        CheckStatus("Not banned", "正常");
        CheckStatus("Temporarily banned", "临时封禁");
        CheckStatus("TemporaryBan", "临时封禁");
        CheckStatus("Permanently banned", "永久封禁");
        CheckStatus("PermanentBan", "永久封禁");

        Console.WriteLine("Smoke tests passed: import 6/6, status 5/5");
        return 0;
    }

    private static void Check(string input, string username, string gameId, string email)
    {
        var result = ImportParser.Parse(input);
        if (result.Count != 1 || !result[0].IsValid || result[0].Username != username || result[0].GameId != gameId || result[0].Email != email)
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
}
