using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;
using SteamLoginLite.Models;

namespace SteamLoginLite.Services
{
    public sealed class PubgPlusResponseParser
    {
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public PubgPlusResult Parse(string body, string expectedId)
        {
            try
            {
                var root = _json.DeserializeObject(body) as IDictionary<string, object>;
                if (root == null) return null;

                object codeValue;
                int code;
                if (root.TryGetValue("code", out codeValue) && int.TryParse(Convert.ToString(codeValue, CultureInfo.InvariantCulture), out code) && code != 0)
                    return null;

                object playerValue;
                if (!TryFind(root, "player", out playerValue)) return null;
                var player = playerValue as IDictionary<string, object>;
                if (player == null) return null;

                var name = GetString(player, "name");
                var shard = GetString(player, "shardId");
                if (!string.Equals(name, expectedId, StringComparison.OrdinalIgnoreCase)) return null;
                if (shard.Length > 0 && !string.Equals(shard, "steam", StringComparison.OrdinalIgnoreCase)) return null;

                var raw = GetString(player, "ban");
                int level;
                if (raw.Length == 0 || !TryGetInt(player, "level", out level) || level < 0) return null;
                int tier;
                if (!TryGetInt(player, "tier", out tier) || tier < 1 || tier > 5) tier = 0;

                return new PubgPlusResult
                {
                    GameId = expectedId,
                    RawStatus = raw,
                    Status = BanStatusNormalizer.Normalize(raw),
                    Level = level,
                    Tier = tier
                };
            }
            catch { return null; }
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";
        }

        private static bool TryGetInt(IDictionary<string, object> source, string key, out int value)
        {
            value = 0;
            object raw;
            return source.TryGetValue(key, out raw) && raw != null && int.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out value);
        }

        private static bool TryFind(object node, string key, out object found)
        {
            var dictionary = node as IDictionary<string, object>;
            if (dictionary != null)
            {
                foreach (var pair in dictionary)
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) { found = pair.Value; return true; }
                foreach (var pair in dictionary)
                    if (TryFind(pair.Value, key, out found)) return true;
            }

            var list = node as IEnumerable;
            if (list != null && !(node is string))
                foreach (var item in list)
                    if (TryFind(item, key, out found)) return true;

            found = null;
            return false;
        }
    }
}
