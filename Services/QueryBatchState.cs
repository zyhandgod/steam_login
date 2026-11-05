using System;
using System.Collections.Generic;
using SteamLoginLite.Models;

namespace SteamLoginLite.Services
{
    public sealed class QueryAccount
    {
        public string Id { get; }
        public string GameId { get; }
        public QueryAccount(string id, string gameId) { Id = id; GameId = gameId; }
    }

    // Pure task state: every retry/navigation changes generation, including identical nicknames.
    public sealed class QueryBatchState
    {
        public IReadOnlyList<QueryAccount> Accounts { get; }
        public int Index { get; private set; }
        public int Generation { get; private set; }
        public int Saved { get; private set; }
        public int Skipped { get; private set; }
        public bool Finished => Index >= Accounts.Count;
        public QueryAccount Current => Finished ? null : Accounts[Index];
        private PubgPlusResult _basic;
        private readonly Dictionary<string, PubgPlusResult> _mastery = new Dictionary<string, PubgPlusResult>(StringComparer.OrdinalIgnoreCase);
        private bool _accepted;
        public QueryBatchState(IReadOnlyList<QueryAccount> accounts) { Accounts = new List<QueryAccount>(accounts).AsReadOnly(); }
        public int Restart() { _basic = null; _mastery.Clear(); _accepted = false; return ++Generation; }
        public bool IsCurrent(int generation) => !Finished && generation == Generation;
        public PubgPlusResult AddBasic(int generation, PubgPlusResult result)
        {
            if (!IsCurrent(generation) || _accepted || result == null || string.IsNullOrEmpty(result.AccountId) || !string.Equals(result.GameId, Current.GameId, StringComparison.OrdinalIgnoreCase)) return null;
            _basic = result;
            return Combined(generation);
        }
        public PubgPlusResult AddMastery(int generation, PubgPlusResult result)
        {
            if (!IsCurrent(generation) || _accepted || result == null || !result.HasLevel || result.Level < 0 || string.IsNullOrEmpty(result.AccountId)) return null;
            _mastery[result.AccountId] = result;
            return Combined(generation);
        }
        private PubgPlusResult Combined(int generation)
        {
            PubgPlusResult mastery;
            if (_basic == null || !_mastery.TryGetValue(_basic.AccountId, out mastery)) return null;
            return Accept(generation, new PubgPlusResult { GameId = Current.GameId, AccountId = _basic.AccountId, RawStatus = _basic.RawStatus,
                Status = _basic.Status, HasLevel = true, Level = mastery.Level, Tier = mastery.Tier });
        }
        public PubgPlusResult Fallback(int generation)
        {
            if (!IsCurrent(generation) || _accepted || _basic == null) return null;
            return Accept(generation, new PubgPlusResult { GameId = Current.GameId, AccountId = _basic.AccountId,
                Status = _basic.Status, RawStatus = _basic.RawStatus, HasLevel = false });
        }
        public PubgPlusResult Accept(int generation, PubgPlusResult result)
        {
            if (!IsCurrent(generation) || _accepted || result == null || !string.Equals(result.GameId, Current.GameId, StringComparison.OrdinalIgnoreCase)) return null;
            _accepted = true; return result;
        }
        public void SaveFailed(int generation) { if (IsCurrent(generation)) _accepted = false; }
        public void Advance(bool saved) { if (Finished) return; if (saved) Saved++; else Skipped++; Index++; Restart(); }
        public void Cancel() { Index = Accounts.Count; Restart(); }
    }
}
