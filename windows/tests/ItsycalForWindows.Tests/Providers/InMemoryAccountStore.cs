using System;
using System.Collections.Generic;
using System.Linq;
using ItsycalForWindows.Providers;

namespace ItsycalForWindows.Tests;

/// <summary>In-memory account store for sync-service tests; records the last sync write.</summary>
internal sealed class InMemoryAccountStore : IAccountStore
{
    private readonly List<ProviderAccount> _accounts = [];

    public int MarkSyncedCount { get; private set; }

    public DateTimeOffset? LastMarkedSync { get; private set; }

    public IReadOnlyList<ProviderAccount> List() => _accounts.ToArray();

    public ProviderAccount? Find(string accountId) =>
        _accounts.FirstOrDefault(account => string.Equals(account.Id, accountId, StringComparison.Ordinal));

    public ProviderAccount Add(ProviderAccount account)
    {
        _accounts.Add(account);
        return account;
    }

    public bool Remove(string accountId) =>
        _accounts.RemoveAll(account => string.Equals(account.Id, accountId, StringComparison.Ordinal)) > 0;

    public bool SetEnabled(string accountId, bool enabled)
    {
        var index = _accounts.FindIndex(account => string.Equals(account.Id, accountId, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        _accounts[index] = _accounts[index] with { Enabled = enabled };
        return true;
    }

    public bool MarkSynced(string accountId, DateTimeOffset lastSyncAt)
    {
        var index = _accounts.FindIndex(account => string.Equals(account.Id, accountId, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }

        MarkSyncedCount++;
        LastMarkedSync = lastSyncAt;
        _accounts[index] = _accounts[index].WithLastSync(lastSyncAt);
        return true;
    }
}
