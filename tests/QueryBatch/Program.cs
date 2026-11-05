using System;
using System.Collections.Generic;
using SteamLoginLite.Models;
using SteamLoginLite.Services;

static class QueryBatchTest
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static PubgPlusResult Basic(string nickname = "same", string id = "account.a") => new PubgPlusResult { GameId = nickname, AccountId = id, Status = "正常", RawStatus = "Innocent" };
    static PubgPlusResult Mastery(string id = "account.a") => new PubgPlusResult { AccountId = id, HasLevel = true, Level = 77, Tier = 2 };
    static void Main()
    {
        var inputs = new List<QueryAccount> { new QueryAccount("first", "same"), new QueryAccount("second", "same"), new QueryAccount("third", "different") };
        var batch = new QueryBatchState(inputs); inputs.Clear();
        Check(batch.Accounts.Count == 3, "Task snapshot immutable and duplicate nickname tasks retained");
        var generation = batch.Restart();
        Check(batch.Current.Id == "first" && batch.AddBasic(generation, Basic("wrong")) == null, "Wrong nickname rejected");
        Check(batch.AddBasic(generation, Basic(id: "")) == null, "Missing account ID rejected");
        Check(batch.AddMastery(generation, null) == null && batch.AddMastery(generation, new PubgPlusResult { AccountId = "account.a" }) == null, "Malformed mastery ignored");
        Check(batch.AddMastery(generation, Mastery("account.other")) == null && batch.AddBasic(generation, Basic()) == null, "Wrong-account mastery cannot bind");
        var result = batch.AddMastery(generation, Mastery());
        Check(result != null && result.Level == 77 && result.Tier == 2 && result.Status == "正常" && result.HasLevel, "Basic then mastery combines trusted account");
        Check(batch.Fallback(generation) == null && batch.AddBasic(generation, Basic()) == null, "Accepted result emitted once");
        batch.SaveFailed(generation);
        Check(batch.Fallback(generation) != null, "Failed save can retry without advancing");
        batch.Advance(true); var second = batch.Generation;
        Check(batch.Current.Id == "second" && batch.Saved == 1, "Same nickname advances by account ID");
        Check(batch.AddBasic(generation, Basic()) == null && batch.AddMastery(generation, Mastery()) == null && batch.Fallback(generation) == null, "Previous same-nickname callbacks invalidated");
        Check(batch.AddMastery(second, Mastery()) == null, "Mastery before basic waits");
        Check(batch.AddBasic(second, Basic()) != null, "Mastery before basic combines");
        batch.Restart(); var retry = batch.Generation;
        Check(batch.Fallback(second) == null && batch.Fallback(retry) == null, "Retry clears cached results and callbacks");
        Check(batch.AddBasic(retry, Basic()) == null, "New retry requires fresh mastery");
        result = batch.Fallback(retry);
        Check(result != null && !result.HasLevel && result.Status == "正常", "Basic-only fallback preserves absence of level");
        batch.Advance(false); var third = batch.Generation;
        Check(batch.Skipped == 1 && batch.Current.Id == "third", "Skip advances independently");
        Check(batch.Accept(third, new PubgPlusResult { GameId = "wrong" }) == null, "Legacy mismatch rejected");
        Check(batch.Accept(third, new PubgPlusResult { GameId = "different", HasLevel = true, Level = 5 }) != null, "Legacy result accepted for exact task");
        batch.Advance(true); Check(batch.Finished && batch.Current == null && batch.Saved == 2 && batch.Skipped == 1, "Batch finishes with accurate counts");
        batch.Cancel(); Check(!batch.IsCurrent(third), "Cancel revokes callbacks");
        var empty = new QueryBatchState(new QueryAccount[0]); Check(empty.Finished && empty.Current == null, "Empty batch safe");
        empty.Advance(true); Check(empty.Saved == 0, "Finished batch cannot advance");
        Console.WriteLine("PASS: immutable duplicate-nickname tasks, trusted merges in either order, wrong IDs, malformed data, one result, failed save, retry/skip/cancel, stale generations, basic fallback and counts.");
    }
}
