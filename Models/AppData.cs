using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SteamLoginLite.Models
{
    public sealed class AppData
    {
        public int Version { get; set; } = 1;
        public List<AccountRecord> Accounts { get; set; } = new List<AccountRecord>();
        public AppSettings Settings { get; set; } = new AppSettings();
        public string CurrentAccountId { get; set; } = "";
    }

    public sealed class AccountRecord
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Username { get; set; } = "";
        public string EncryptedPassword { get; set; } = "";
        public string Email { get; set; } = "";
        public string EncryptedEmailPassword { get; set; } = "";
        public string GameId { get; set; } = "";
        public int? Level { get; set; }
        public string Status { get; set; } = "未查询";
        public string RawStatus { get; set; } = "";
        public string Note { get; set; } = "";
        public long LastLoginAt { get; set; }
        public long LastQueryAt { get; set; }

        [JsonIgnore]
        public bool UiSelected { get; set; }

        [JsonIgnore]
        public bool UiIsCurrent { get; set; }

        public string EffectiveGameId => string.IsNullOrWhiteSpace(GameId) ? Username : GameId;
        public string LevelText => Level.HasValue ? Level.Value.ToString() : "—";
        public string LastLoginText => FormatTime(LastLoginAt);
        public string LastQueryText => FormatTime(LastQueryAt);
        private static string FormatTime(long value)
        {
            if (value <= 0) return "—";
            try { return DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("yyyy-MM-dd HH:mm"); }
            catch { return "—"; }
        }
    }

    public sealed class AppSettings
    {
        public string SteamPath { get; set; } = "";
        public int StartupDelaySeconds { get; set; } = 8;
        public bool CloseRecommendations { get; set; } = true;
        public bool CloseFriendsList { get; set; } = true;
        public bool OpenLibrary { get; set; } = true;
        public int QueryIntervalMilliseconds { get; set; } = 1200;
    }
}
