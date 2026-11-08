using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Providers;

namespace ItsycalForWindows.Tests;

/// <summary>In-memory secret store for sync-service and store tests; records lookups.</summary>
internal sealed class InMemorySecretStore : IProviderSecretStore
{
    private readonly Dictionary<string, ProviderSecret> _secrets = new(StringComparer.Ordinal);

    public int GetAsyncCount { get; private set; }

    public List<string> RequestedAccountIds { get; } = [];

    public bool TryGet(string accountId, out ProviderSecret? secret) =>
        _secrets.TryGetValue(accountId, out secret);

    public Task<ProviderSecret?> GetAsync(string accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GetAsyncCount++;
        RequestedAccountIds.Add(accountId);
        return Task.FromResult(_secrets.TryGetValue(accountId, out var secret) ? secret : null);
    }

    public bool Save(string accountId, ProviderSecret secret)
    {
        if (secret.IsEmpty)
        {
            return _secrets.Remove(accountId);
        }

        _secrets[accountId] = secret;
        return true;
    }

    public bool Remove(string accountId) => _secrets.Remove(accountId);
}

/// <summary>Deterministic clock for cache-TTL and store-timestamp tests.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    public ManualTimeProvider(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}
