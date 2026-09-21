using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SteamLoginLite.Services
{
    public sealed class ImportedAccount
    {
        public int LineNumber { get; set; }
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Email { get; set; } = "";
        public string EmailPassword { get; set; } = "";
        public string GameId { get; set; } = "";
        public string Error { get; set; } = "";
        public bool IsValid => string.IsNullOrEmpty(Error);
    }

    public static class ImportParser
    {
        private static readonly Regex Separator = new Regex(@"-{2,}", RegexOptions.Compiled);

        public static List<ImportedAccount> Parse(string text)
        {
            var result = new List<ImportedAccount>();
            var lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                var item = line.Contains("账号") && line.Contains("密码") ? ParseLabels(line) : ParseSeparated(line);
                item.LineNumber = i + 1;
                Validate(item);
                result.Add(item);
            }
            return result;
        }

        private static ImportedAccount ParseSeparated(string line)
        {
            var parts = Separator.Split(line).Select(Clean).ToArray();
            if (parts.Length != 2 && parts.Length != 4 && parts.Length != 5)
                return new ImportedAccount { Error = "需要2段、4段或5段数据" };
            return new ImportedAccount
            {
                Username = parts[0], Password = parts[1],
                Email = parts.Length >= 4 ? parts[2] : "",
                EmailPassword = parts.Length >= 4 ? parts[3] : "",
                GameId = parts.Length == 5 && parts[4].Length > 0 ? parts[4] : parts[0]
            };
        }

        private static ImportedAccount ParseLabels(string line)
        {
            var pattern = @"^账号(?<u>.*?)密码(?<p>.*?)(?:邮箱账号(?<e>.*?)邮箱密码(?<ep>.*?)(?:邮箱地址(?<domain>.*?))?)?(?:游戏(?:ID|昵称)(?<g>.*))?$";
            var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (!match.Success) return new ImportedAccount { Error = "无法识别中文标签格式" };
            var email = Clean(match.Groups["e"].Value);
            var domain = Clean(match.Groups["domain"].Value);
            if (!email.Contains("@") && domain.Length > 0) email += "@" + domain;
            var username = Clean(match.Groups["u"].Value);
            return new ImportedAccount
            {
                Username = username, Password = Clean(match.Groups["p"].Value), Email = email,
                EmailPassword = Clean(match.Groups["ep"].Value),
                GameId = Clean(match.Groups["g"].Value).Length > 0 ? Clean(match.Groups["g"].Value) : username
            };
        }

        private static string Clean(string value) => (value ?? "").Trim().Replace("\\@", "@");

        private static void Validate(ImportedAccount item)
        {
            if (!string.IsNullOrEmpty(item.Error)) return;
            if (string.IsNullOrWhiteSpace(item.Username)) item.Error = "Steam账号为空";
            else if (string.IsNullOrWhiteSpace(item.Password)) item.Error = "Steam密码为空";
            else if (!string.IsNullOrWhiteSpace(item.Email) && !item.Email.Contains("@")) item.Error = "邮箱格式不正确";
        }
    }
}
