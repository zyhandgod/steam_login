using System;
using System.Linq;
using SteamLoginLite.Services;

internal static class ImportParserTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        foreach (var width in new[] { 1, 2, 3, 4, 8, 50 })
        {
            var separator = new string('-', width);
            var two = ImportParser.Parse("player" + separator + "password").Single();
            Check(two.IsValid && two.Username == "player" && two.Password == "password" && two.GameId == "player" && two.Email == "" && two.EmailPassword == "", "Two fields with separator width " + width);
            var three = ImportParser.Parse("player" + separator + "password" + separator + "游戏昵称").Single();
            Check(three.IsValid && three.Username == "player" && three.Password == "password" && three.GameId == "游戏昵称" && three.Email == "" && three.EmailPassword == "", "Third field is nickname, not email, with separator width " + width);
        }

        var mixed = ImportParser.Parse("  player --- password - 昵称  ").Single();
        Check(mixed.IsValid && mixed.Username == "player" && mixed.Password == "password" && mixed.GameId == "昵称", "Different separator widths and outer spaces on the same line");
        var emptyNickname = ImportParser.Parse("player----password----").Single();
        Check(emptyNickname.IsValid && emptyNickname.GameId == "player", "Empty optional nickname falls back to username");
        var nicknameWithLabels = ImportParser.Parse("player----password----账号密码测试").Single();
        Check(nicknameWithLabels.IsValid && nicknameWithLabels.GameId == "账号密码测试", "Nickname containing Chinese labels still uses separated format");

        var four = ImportParser.Parse("player-password-mail\\@example.com-mailpass").Single();
        Check(four.IsValid && four.Email == "mail@example.com" && four.EmailPassword == "mailpass" && four.GameId == "player", "Original four-field email format");
        var five = ImportParser.Parse("player----password--mail@example.com---mailpass-昵称").Single();
        Check(five.IsValid && five.Email == "mail@example.com" && five.EmailPassword == "mailpass" && five.GameId == "昵称", "Original five-field format with mixed separator widths");
        var labeled = ImportParser.Parse("账号player密码pass-with-hyphens游戏昵称nick-name").Single();
        Check(labeled.IsValid && labeled.Username == "player" && labeled.Password == "pass-with-hyphens" && labeled.GameId == "nick-name", "Chinese label format preserves hyphens inside field values");

        foreach (var source in new[] { "player", "player----", "----password", "player---- ----nickname", "a-b-mail-c-d-extra", "a-b-notanemail-c" })
            Check(!ImportParser.Parse(source).Single().IsValid, "Malformed or incomplete row is rejected");
        Check(ImportParser.Parse(null).Count == 0 && ImportParser.Parse(" \r\n\t").Count == 0, "Empty input produces no rows");
        var rows = ImportParser.Parse("\r\nfirst-pass\r\n\r\nsecond----secret---昵称\rthird--secret--mail@example.com--mailpass\nfourth---secret---mail@example.com---mailpass---昵称");
        Check(rows.Count == 4 && rows.All(a => a.IsValid), "Two-, three-, four- and five-field records coexist in one batch");
        Check(rows.Select(a => a.LineNumber).SequenceEqual(new[] { 2, 4, 5, 6 }), "Blank lines and mixed newlines retain original error line numbers");
        Check(rows[1].GameId == "昵称" && rows[2].EmailPassword == "mailpass" && rows[3].GameId == "昵称", "Mixed batch keeps nicknames and email credentials in the right fields");
        Console.WriteLine("PASS: flexible hyphen separators, two/three-field imports, legacy email formats, nickname mapping, label format, validation and mixed-line batches.");
    }
}
