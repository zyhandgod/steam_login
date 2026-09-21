using System;
using System.Text.Json;
using SteamLoginLite.Models;

namespace SteamLoginLite.Services
{
    public sealed class PubgPlusResponseParser
    {
        public PubgPlusResult Parse(string body, string expectedId)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (TryFind(root, "code", out var codeNode) && codeNode.ValueKind == JsonValueKind.Number && codeNode.TryGetInt32(out var code) && code != 0) return null;
                if (!TryFind(root, "player", out var player) || player.ValueKind != JsonValueKind.Object) return null;

                var name = GetString(player, "name");
                var shard = GetString(player, "shardId");
                if (!string.Equals(name, expectedId, StringComparison.OrdinalIgnoreCase)) return null;
                if (shard.Length > 0 && !string.Equals(shard, "steam", StringComparison.OrdinalIgnoreCase)) return null;

                var raw = GetString(player, "ban");
                if (raw.Length == 0 || !player.TryGetProperty("level", out var levelNode) || !levelNode.TryGetInt32(out var level) || level < 0) return null;

                return new PubgPlusResult
                {
                    GameId = expectedId,
                    RawStatus = raw,
                    Status = BanStatusNormalizer.Normalize(raw),
                    Level = level
                };
            }
            catch { return null; }
        }

        private static string GetString(JsonElement source, string key)
        {
            if (!source.TryGetProperty(key, out var value)) return "";
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
        }

        private static bool TryFind(JsonElement node, string key, out JsonElement found)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in node.EnumerateObject())
                    if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)) { found = property.Value; return true; }
                foreach (var property in node.EnumerateObject())
                    if (TryFind(property.Value, key, out found)) return true;
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in node.EnumerateArray())
                    if (TryFind(item, key, out found)) return true;
            }
            found = default;
            return false;
        }
    }
}
