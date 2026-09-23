using System;
using System.IO;
using System.Text;

namespace SteamLoginLite.Services
{
    public static class SteamVdfConfig
    {
        public static bool ApplyPopupPreferences(string path, bool disableNews, bool disableFriends)
        {
            if (!disableNews && !disableFriends) return true;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

            var bytes = File.ReadAllBytes(path);
            var hasUtf8Bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var utf8 = new UTF8Encoding(false, true);
            var original = utf8.GetString(bytes, hasUtf8Bom ? 3 : 0, bytes.Length - (hasUtf8Bom ? 3 : 0));
            var updated = original;
            if (disableNews)
                updated = EnsureValue(updated, new[] { "UserLocalConfigStore", "system", "news" }, "NotifyAvailableGames", "0");
            if (disableFriends)
                updated = EnsureValue(updated, new[] { "UserLocalConfigStore", "friends" }, "SignIntoFriends", "0");
            if (string.Equals(original, updated, StringComparison.Ordinal)) return true;

            var temp = path + ".steam-login-lite.tmp";
            var backup = path + ".steam-login-lite.bak";
            try
            {
                File.WriteAllText(temp, updated, new UTF8Encoding(hasUtf8Bom));
                File.Replace(temp, path, backup, true);
                return true;
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        internal static string EnsureValue(string content, string[] blockPath, string key, string value)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (blockPath == null || blockPath.Length == 0) throw new ArgumentException("VDF 节点路径不能为空。", nameof(blockPath));

            var parent = new BlockSpan(-1, content.Length);
            for (var depth = 0; depth < blockPath.Length; depth++)
            {
                MemberSpan member;
                var status = FindDirectMember(content, parent, blockPath[depth], out member);
                if (status == FindMemberStatus.Invalid)
                    throw new InvalidDataException("Steam localconfig.vdf 结构无效，已停止修改。节点：" + blockPath[depth]);
                if (status == FindMemberStatus.Found && !member.IsBlock)
                    throw new InvalidDataException("Steam localconfig.vdf 节点类型冲突，已停止修改。节点：" + blockPath[depth]);
                if (status == FindMemberStatus.NotFound)
                {
                    if (depth == 0)
                        throw new InvalidDataException("Steam localconfig.vdf 缺少 UserLocalConfigStore 根节点，已停止修改。");
                    content = InsertBlock(content, parent, blockPath[depth]);
                    return EnsureValue(content, blockPath, key, value);
                }
                parent = new BlockSpan(member.OpenBrace, member.CloseBrace);
            }

            MemberSpan existing;
            var existingStatus = FindDirectMember(content, parent, key, out existing);
            if (existingStatus == FindMemberStatus.Invalid || (existingStatus == FindMemberStatus.Found && existing.IsBlock))
                throw new InvalidDataException("Steam localconfig.vdf 配置项结构无效，已停止修改。配置项：" + key);
            if (existingStatus == FindMemberStatus.Found)
                return content.Remove(existing.ValueStart, existing.ValueLength).Insert(existing.ValueStart, Quote(value));

            var closingIndent = LineIndent(content, parent.CloseBrace);
            var indent = closingIndent + "\t";
            return content.Insert(parent.CloseBrace - closingIndent.Length, indent + Quote(key) + "\t\t" + Quote(value) + NewLine(content));
        }

        private static string InsertBlock(string content, BlockSpan parent, string name)
        {
            var closingIndent = LineIndent(content, parent.CloseBrace);
            var indent = closingIndent + "\t";
            var newline = NewLine(content);
            return content.Insert(parent.CloseBrace - closingIndent.Length, indent + Quote(name) + newline + indent + "{" + newline + indent + "}" + newline);
        }

        private static FindMemberStatus FindDirectMember(string content, BlockSpan parent, string wantedKey, out MemberSpan result)
        {
            var index = parent.OpenBrace + 1;
            var found = false;
            result = default(MemberSpan);
            while (index < parent.CloseBrace)
            {
                SkipTrivia(content, ref index, parent.CloseBrace);
                if (index >= parent.CloseBrace) break;
                if (content[index] != '"') return FindMemberStatus.Invalid;

                string parsedKey;
                int keyStart, keyLength;
                if (!TryReadQuoted(content, ref index, parent.CloseBrace, out parsedKey, out keyStart, out keyLength))
                    return FindMemberStatus.Invalid;
                SkipTrivia(content, ref index, parent.CloseBrace);
                if (index >= parent.CloseBrace) return FindMemberStatus.Invalid;

                MemberSpan candidate;
                if (content[index] == '{')
                {
                    var open = index;
                    var close = FindMatchingBrace(content, open, parent.CloseBrace);
                    if (close < 0) return FindMemberStatus.Invalid;
                    candidate = MemberSpan.Block(open, close);
                    index = close + 1;
                }
                else if (content[index] == '"')
                {
                    string ignored;
                    int valueStart, valueLength;
                    if (!TryReadQuoted(content, ref index, parent.CloseBrace, out ignored, out valueStart, out valueLength))
                        return FindMemberStatus.Invalid;
                    candidate = MemberSpan.Value(valueStart, valueLength);
                }
                else return FindMemberStatus.Invalid;

                if (!string.Equals(parsedKey, wantedKey, StringComparison.OrdinalIgnoreCase)) continue;
                if (found) return FindMemberStatus.Invalid;
                result = candidate;
                found = true;
            }
            return found ? FindMemberStatus.Found : FindMemberStatus.NotFound;
        }

        private static bool TryReadQuoted(string content, ref int index, int limit, out string value, out int start, out int length)
        {
            start = index;
            length = 0;
            value = "";
            if (index >= limit || content[index] != '"') return false;
            index++;
            var builder = new StringBuilder();
            while (index < limit)
            {
                var character = content[index++];
                if (character == '"')
                {
                    length = index - start;
                    value = builder.ToString();
                    return true;
                }
                if (character == '\\')
                {
                    if (index >= limit) return false;
                    builder.Append(content[index++]);
                }
                else builder.Append(character);
            }
            return false;
        }

        private static int FindMatchingBrace(string content, int openBrace, int limit)
        {
            var depth = 0;
            var quoted = false;
            for (var index = openBrace; index < limit; index++)
            {
                var character = content[index];
                if (quoted)
                {
                    if (character == '\\') index++;
                    else if (character == '"') quoted = false;
                    continue;
                }
                if (character == '"') { quoted = true; continue; }
                if (character == '/' && index + 1 < limit && content[index + 1] == '/')
                {
                    index += 2;
                    while (index < limit && content[index] != '\n') index++;
                    continue;
                }
                if (character == '{') depth++;
                else if (character == '}')
                {
                    depth--;
                    if (depth == 0) return index;
                    if (depth < 0) return -1;
                }
            }
            return -1;
        }

        private static void SkipTrivia(string content, ref int index, int limit)
        {
            while (index < limit)
            {
                if (char.IsWhiteSpace(content[index])) { index++; continue; }
                if (content[index] == '/' && index + 1 < limit && content[index + 1] == '/')
                {
                    index += 2;
                    while (index < limit && content[index] != '\n') index++;
                    continue;
                }
                break;
            }
        }

        private static string LineIndent(string content, int position)
        {
            var lineStart = position;
            while (lineStart > 0 && content[lineStart - 1] != '\n' && content[lineStart - 1] != '\r') lineStart--;
            var end = lineStart;
            while (end < position && (content[end] == ' ' || content[end] == '\t')) end++;
            return content.Substring(lineStart, end - lineStart);
        }

        private static string NewLine(string content) => content.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
        private static string Quote(string value) => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private enum FindMemberStatus { NotFound, Found, Invalid }

        private struct BlockSpan
        {
            public BlockSpan(int openBrace, int closeBrace) { OpenBrace = openBrace; CloseBrace = closeBrace; }
            public int OpenBrace;
            public int CloseBrace;
        }

        private struct MemberSpan
        {
            public bool IsBlock;
            public int OpenBrace;
            public int CloseBrace;
            public int ValueStart;
            public int ValueLength;
            public static MemberSpan Block(int open, int close) => new MemberSpan { IsBlock = true, OpenBrace = open, CloseBrace = close };
            public static MemberSpan Value(int start, int length) => new MemberSpan { ValueStart = start, ValueLength = length };
        }
    }
}
