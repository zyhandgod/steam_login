namespace SteamLoginLite.Models
{
    public sealed class PubgPlusResult
    {
        public string GameId { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string Status { get; set; } = "";
        public string RawStatus { get; set; } = "";
        public bool HasLevel { get; set; }
        public int Level { get; set; }
        public int Tier { get; set; }
    }
}
