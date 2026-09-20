namespace SteamLoginLite.Models
{
    public sealed class PubgPlusResult
    {
        public string GameId { get; set; } = "";
        public string Status { get; set; } = "";
        public string RawStatus { get; set; } = "";
        public int Level { get; set; }
    }
}
