namespace SteamLoginLite.Services
{
    public static class BanStatusNormalizer
    {
        public static string Normalize(string value)
        {
            value = (value ?? "").Trim();
            var key = value.ToLowerInvariant().Replace("_", "").Replace(" ", "");
            if (key == "正常" || key == "未封禁" || key == "innocent" || key == "normal" || key == "notbanned" || key == "none" || key == "0") return "正常";
            if (key.Contains("临时封禁") || key.Contains("temporary") || key.Contains("temporarily") || key.Contains("tempban")) return "临时封禁";
            if (key.Contains("永久封禁") || key.Contains("permanent") || key.Contains("permanently") || key.Contains("permban")) return "永久封禁";
            return "未知状态：" + value;
        }
    }
}
