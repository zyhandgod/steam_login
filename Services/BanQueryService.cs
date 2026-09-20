using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SteamLoginLite.Services
{
    public sealed class BanQueryResult
    {
        public bool Success { get; set; }
        public bool NotFound { get; set; }
        public string Status { get; set; } = "查询失败";
        public string RawStatus { get; set; } = "";
        public int? Level { get; set; }
        public string Provider { get; set; } = "";
        public string Error { get; set; } = "";
    }

    public sealed class BanQueryService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

        public BanQueryService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("SteamLoginLite/1.0");
        }

        public async Task<BanQueryResult> QueryAsync(string playerId, CancellationToken token)
        {
            playerId = (playerId ?? "").Trim();
            if (playerId.Length == 0) return Fail("查询ID为空");
            var errors = new List<string>();
            foreach (var provider in new[]
            {
                new { Name = "PUBG Ban Checker", Url = "https://pubgbanchecker.com/api/check-ban-clan?platform=steam&player=" },
                new { Name = "PUBG Meta", Url = "https://api.pubgmeta.com/api/account/steam/" }
            })
            {
                try
                {
                    using (var response = await _http.GetAsync(provider.Url + Uri.EscapeDataString(playerId), token))
                    {
                        if (response.StatusCode == HttpStatusCode.NotFound)
                            return new BanQueryResult { NotFound = true, Status = "玩家不存在", Provider = provider.Name };
                        if (!response.IsSuccessStatusCode)
                        {
                            errors.Add(provider.Name + ": HTTP " + (int)response.StatusCode);
                            continue;
                        }
                        var body = await response.Content.ReadAsStringAsync();
                        var parsed = Parse(body, provider.Name);
                        if (parsed.Success || parsed.NotFound) return parsed;
                        errors.Add(provider.Name + ": " + parsed.Error);
                    }
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { errors.Add(provider.Name + ": 请求超时"); }
                catch (Exception ex) { errors.Add(provider.Name + ": " + ex.Message); }
            }
            return Fail(string.Join("；", errors));
        }

        private BanQueryResult Parse(string body, string provider)
        {
            try
            {
                var root = _json.DeserializeObject(body);
                var raw = FindString(root, "banType", "banStatus", "status", "ban");
                var level = FindInt(root, "level", "gameLevel");
                var message = FindString(root, "message", "msg", "error");
                if (ContainsNotFound(raw) || ContainsNotFound(message))
                    return new BanQueryResult { NotFound = true, Status = "玩家不存在", Provider = provider };
                if (string.IsNullOrWhiteSpace(raw)) return Fail("返回数据缺少封禁状态", provider);
                return new BanQueryResult
                {
                    Success = true, RawStatus = raw, Status = BanStatusNormalizer.Normalize(raw), Level = level, Provider = provider
                };
            }
            catch (Exception ex) { return Fail("无法解析服务端返回：" + ex.Message, provider); }
        }

        private static bool ContainsNotFound(string value)
        {
            value = (value ?? "").ToLowerInvariant();
            return value.Contains("not found") || value.Contains("不存在") || value.Contains("no player");
        }

        private static string FindString(object node, params string[] keys)
        {
            object found;
            if (!TryFind(node, keys, out found) || found == null) return "";
            return Convert.ToString(found, CultureInfo.InvariantCulture) ?? "";
        }

        private static int? FindInt(object node, params string[] keys)
        {
            object found;
            if (!TryFind(node, keys, out found) || found == null) return null;
            int value;
            return int.TryParse(Convert.ToString(found, CultureInfo.InvariantCulture), out value) ? value : (int?)null;
        }

        private static bool TryFind(object node, string[] keys, out object found)
        {
            foreach (var key in keys)
                if (TryFindSingle(node, key, out found)) return true;
            found = null;
            return false;
        }

        private static bool TryFindSingle(object node, string key, out object found)
        {
            var dict = node as IDictionary<string, object>;
            if (dict != null)
            {
                foreach (var pair in dict)
                    if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) { found = pair.Value; return true; }
                foreach (var pair in dict)
                    if (TryFindSingle(pair.Value, key, out found)) return true;
            }
            var list = node as IEnumerable;
            if (list != null && !(node is string))
                foreach (var item in list)
                    if (TryFindSingle(item, key, out found)) return true;
            found = null;
            return false;
        }

        private static BanQueryResult Fail(string error, string provider = "") =>
            new BanQueryResult { Status = "查询失败", Error = error, Provider = provider };

        public void Dispose() => _http.Dispose();
    }
}
