using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SteamLoginLite.Services
{
    public static class SteamVdfConfig
    {
        public static bool ApplyPopupPreferences(string path, bool disableNews, bool disableFriends)
        {
            if (!disableNews && !disableFriends) return true;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

            return ApplyLoginPreferences(path, "UserLocalConfigStore", disableNews, disableFriends, null);
        }

        // The same hierarchy is used by Steam's local and roaming configuration.
        public static bool ApplyLoginPreferences(string path, string root, bool disableNews, bool disableFriends, bool? openLibrary)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            var bytes = File.ReadAllBytes(path);
            var original = DecodeUtf8(bytes);
            var updated = EnsureValue(original, new[] { root, "system", "news" }, "NotifyAvailableGames", disableNews ? "0" : "1");
            updated = EnsureValue(updated, new[] { root, "friends" }, "SignIntoFriends", disableFriends ? "0" : "1");
            if (openLibrary.HasValue)
                updated = EnsureValue(updated, new[] { root, "Software", "Valve", "Steam" }, "SteamDefaultDialog", openLibrary.Value ? "#app_games" : "#store");
            WriteUpdatedFile(path, bytes, updated);
            return true;
        }

        internal static string DecodeUtf8(byte[] bytes)
        {
            var offset = HasUtf8Bom(bytes) ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }

        internal static void WriteUpdatedFile(string path, byte[] original, string updated)
        {
            if (string.Equals(DecodeUtf8(original), updated, StringComparison.Ordinal)) return;
            var temp = path + ".steam-login-lite.tmp";
            var backup = path + ".steam-login-lite.bak";
            try
            {
                File.WriteAllText(temp, updated, new UTF8Encoding(HasUtf8Bom(original)));
                if (!File.Exists(backup)) File.Copy(path, backup, false);
                if (!File.ReadAllBytes(path).SequenceEqual(original))
                    throw new IOException("Steam 配置正在变化，将稍后重试。");
                File.Replace(temp, path, null, true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        private static bool HasUtf8Bom(byte[] bytes) => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        internal static string ReadValue(string content, string[] blockPath, string key)
            => ReadOptionalValue(content, blockPath, key) ?? "";

        internal static string ReadOptionalValue(string content, string[] blockPath, string key)
        {
            var block = ResolveBlock(content, blockPath);
            MemberSpan member;
            var status = FindDirectMember(content, block, key, out member);
            if (status == FindMemberStatus.Invalid || (status == FindMemberStatus.Found && member.IsBlock))
                throw new InvalidDataException("Steam 登录记录结构无效。");
            if (status == FindMemberStatus.NotFound) return null;
            var index = member.ValueStart;
            string value; int start, length;
            if (!TryReadQuoted(content, ref index, content.Length, out value, out start, out length))
                throw new InvalidDataException("Steam 登录记录结构无效。");
            return value;
        }

        internal static string EnsureValueInExistingBlock(string content, string[] blockPath, string key, string value)
        {
            var parent = new BlockSpan(-1, content.Length);
            ValidateMembers(content, parent);
            foreach (var name in blockPath)
            {
                MemberSpan member;
                var status = FindDirectMember(content, parent, name, out member);
                if (status == FindMemberStatus.Invalid || (status == FindMemberStatus.Found && !member.IsBlock))
                    throw new InvalidDataException("Steam 配置节点结构无效：" + name);
                if (status == FindMemberStatus.NotFound) return content;
                parent = new BlockSpan(member.OpenBrace, member.CloseBrace);
            }
            return EnsureValue(content, blockPath, key, value);
        }

        private static void ValidateMembers(string content, BlockSpan parent)
        {
            var index = parent.OpenBrace + 1;
            while (index < parent.CloseBrace)
            {
                SkipTrivia(content, ref index, parent.CloseBrace);
                if (index >= parent.CloseBrace) break;
                string name; int start, length;
                if (!TryReadQuoted(content, ref index, parent.CloseBrace, out name, out start, out length))
                    throw new InvalidDataException("Steam 配置结构无效。");
                SkipTrivia(content, ref index, parent.CloseBrace);
                if (index < parent.CloseBrace && content[index] == '{')
                {
                    var end = FindMatchingBrace(content, index, parent.CloseBrace);
                    if (end < 0) throw new InvalidDataException("Steam 配置结构无效。");
                    ValidateMembers(content, new BlockSpan(index, end));
                    index = end + 1;
                }
                else
                {
                    string ignored;
                    if (!TryReadQuoted(content, ref index, parent.CloseBrace, out ignored, out start, out length))
                        throw new InvalidDataException("Steam 配置结构无效。");
                }
            }
        }

        internal static string[] ChildBlocks(string content, params string[] blockPath)
        {
            var block = ResolveBlock(content, blockPath);
            var names = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = block.OpenBrace + 1;
            while (index < block.CloseBrace)
            {
                SkipTrivia(content, ref index, block.CloseBrace);
                if (index >= block.CloseBrace) break;
                string name; int start, length;
                if (!TryReadQuoted(content, ref index, block.CloseBrace, out name, out start, out length) || !seen.Add(name))
                    throw new InvalidDataException("Steam 登录记录结构无效或有重复账号。");
                SkipTrivia(content, ref index, block.CloseBrace);
                if (index < block.CloseBrace && content[index] == '{')
                {
                    var end = FindMatchingBrace(content, index, block.CloseBrace);
                    if (end < 0) throw new InvalidDataException("Steam 登录记录结构无效。");
                    names.Add(name); index = end + 1;
                }
                else
                {
                    string ignored;
                    if (!TryReadQuoted(content, ref index, block.CloseBrace, out ignored, out start, out length))
                        throw new InvalidDataException("Steam 登录记录结构无效。");
                }
            }
            return names.ToArray();
        }

        private static BlockSpan ResolveBlock(string content, string[] path)
        {
            var block = new BlockSpan(-1, content.Length);
            foreach (var key in path)
            {
                MemberSpan member;
                if (FindDirectMember(content, block, key, out member) != FindMemberStatus.Found || !member.IsBlock)
                    throw new InvalidDataException("Steam 登录记录缺少有效节点。");
                block = new BlockSpan(member.OpenBrace, member.CloseBrace);
            }
            return block;
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
