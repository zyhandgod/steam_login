using System;
using System.Collections.Generic;
using System.Linq;
using SteamLoginLite.Models;

namespace SteamLoginLite.Services
{
    public static class SteamAccountSync
    {
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
                var account = data.Accounts.FirstOrDefault(a => a.Username.Equals(remembered.Username, StringComparison.OrdinalIgnoreCase));
                if (account == null) { account = new AccountRecord { Username = remembered.Username }; data.Accounts.Add(account); }
                account.IsLocalAccount = true;
                account.CanUseSavedLogin = remembered.CanUseSavedLogin;
            }
            var current = data.Accounts.FirstOrDefault(a => a.IsLocalAccount && a.Username.Equals(activeUsername, StringComparison.OrdinalIgnoreCase));
            if (current != null) data.CurrentAccountId = current.Id;
        }
    }
}
