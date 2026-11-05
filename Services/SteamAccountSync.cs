using System;
using System.Collections.Generic;
using System.Linq;
using SteamLoginLite.Models;

namespace SteamLoginLite.Services
{
    public static class SteamAccountSync
    {
        // Keep only a name tombstone: removed passwords/notes/results are not retained.
        public static void Remove(AppData data, IEnumerable<string> ids)
        {
            var selected = new HashSet<string>(ids);
            if (data.IgnoredLocalAccounts == null) data.IgnoredLocalAccounts = new List<string>();
            foreach (var account in data.Accounts.Where(a => selected.Contains(a.Id)))
                if (!string.IsNullOrWhiteSpace(account.Username) && !data.IgnoredLocalAccounts.Any(name => string.Equals(name, account.Username.Trim(), StringComparison.OrdinalIgnoreCase)))
                    data.IgnoredLocalAccounts.Add(account.Username.Trim());
            data.Accounts.RemoveAll(a => selected.Contains(a.Id));
            if (selected.Contains(data.CurrentAccountId)) data.CurrentAccountId = "";
        }

        public static void RestoreName(AppData data, string username)
        {
            if (data.IgnoredLocalAccounts == null) data.IgnoredLocalAccounts = new List<string>();
            data.IgnoredLocalAccounts.RemoveAll(name => string.Equals(name, username?.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static bool FillMissingData(AccountRecord existing, AccountRecord incoming)
        {
            if (!existing.IsLocalAccount) return false;
            var changed = false;
            if (string.IsNullOrEmpty(existing.EncryptedPassword) && !string.IsNullOrEmpty(incoming.EncryptedPassword)) { existing.EncryptedPassword = incoming.EncryptedPassword; changed = true; }
            if (string.IsNullOrEmpty(existing.EncryptedEmailPassword) && !string.IsNullOrEmpty(incoming.EncryptedEmailPassword)) { existing.EncryptedEmailPassword = incoming.EncryptedEmailPassword; changed = true; }
            if (string.IsNullOrWhiteSpace(existing.Email) && !string.IsNullOrWhiteSpace(incoming.Email)) { existing.Email = incoming.Email; changed = true; }
            if (string.IsNullOrWhiteSpace(existing.GameId) && !string.IsNullOrWhiteSpace(incoming.GameId)) { existing.GameId = incoming.GameId; changed = true; }
            return changed;
        }

        public static void Merge(AppData data, List<SteamLocalAccount> local, string activeUsername)
        {
            data.CurrentAccountId = "";
            foreach (var account in data.Accounts) { account.IsLocalAccount = false; account.CanUseSavedLogin = false; }
            foreach (var remembered in local)
            {
                var account = data.Accounts.FirstOrDefault(a => string.Equals(a.Username, remembered.Username, StringComparison.OrdinalIgnoreCase));
                if (account == null && data.IgnoredLocalAccounts != null && data.IgnoredLocalAccounts.Any(name => string.Equals(name, remembered.Username, StringComparison.OrdinalIgnoreCase))) continue;
                if (account == null) { account = new AccountRecord { Username = remembered.Username }; data.Accounts.Add(account); }
                account.IsLocalAccount = true;
                account.CanUseSavedLogin = remembered.CanUseSavedLogin;
            }
            var current = data.Accounts.FirstOrDefault(a => a.IsLocalAccount && a.Username.Equals(activeUsername, StringComparison.OrdinalIgnoreCase));
            if (current != null) data.CurrentAccountId = current.Id;
        }
    }
}
