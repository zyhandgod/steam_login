using System;
using System.Web;

namespace SteamLoginLite.Services
{
    public enum PubgPlusRequestKind { None, Legacy, Basic, SurvivalMastery }

    public static class PubgPlusRequestMatcher
    {
        public static PubgPlusRequestKind Classify(string url, string gameId, out string accountId)
        {
            accountId = "";
            try
            {
                var uri = new Uri(url);
                if (!uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                    !uri.Host.Equals("apiv1.pubg.plus", StringComparison.OrdinalIgnoreCase)) return PubgPlusRequestKind.None;

                var query = HttpUtility.ParseQueryString(uri.Query);
                if (uri.AbsolutePath.EndsWith("/player/info", StringComparison.OrdinalIgnoreCase))
                    return string.Equals(query["player_id"], gameId, StringComparison.OrdinalIgnoreCase) ? PubgPlusRequestKind.Legacy : PubgPlusRequestKind.None;
                if (uri.AbsolutePath.EndsWith("/steam/player/basic", StringComparison.OrdinalIgnoreCase))
                    return string.Equals(query["player_id"], gameId, StringComparison.OrdinalIgnoreCase) ? PubgPlusRequestKind.Basic : PubgPlusRequestKind.None;
                if (uri.AbsolutePath.EndsWith("/steam/player/survival_mastery", StringComparison.OrdinalIgnoreCase))
                {
                    accountId = query["acc_id"] ?? "";
                    return accountId.Length > 0 ? PubgPlusRequestKind.SurvivalMastery : PubgPlusRequestKind.None;
                }
                return PubgPlusRequestKind.None;
            }
            catch { return PubgPlusRequestKind.None; }
        }
    }
}
