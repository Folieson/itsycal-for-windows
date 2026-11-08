using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Providers;

namespace ItsycalForWindows.Tests;

/// <summary>Configurable provider fake used by registry and sync-service tests.</summary>
internal sealed class FakeCalendarProvider : ICalendarProvider, IProviderCacheInvalidator
{
    private int _probeCount;
    private int _getEventsCount;
    private int _invalidateCacheCount;

    public ProviderKind Kind { get; init; } = ProviderKind.IcsSubscription;

    public string DisplayName => "Fake provider";

    public Func<ProviderRequest, CancellationToken, Task<ProviderStatus>>? Probe { get; init; }

    public Func<ProviderRequest, CancellationToken, Task<ProviderEventResult>>? GetEvents { get; init; }

    public IReadOnlyList<ProviderEvent> Events { get; init; } = [];

    public int ProbeCount => Volatile.Read(ref _probeCount);

    public int GetEventsCount => Volatile.Read(ref _getEventsCount);

    public int InvalidateCacheCount => Volatile.Read(ref _invalidateCacheCount);

    public string? LastInvalidatedAccountId { get; private set; }

    public ProviderSecret? LastSecret { get; private set; }

    public List<ProviderRequest> Requests { get; } = [];

    public List<ProviderEventRange> RequestedRanges { get; } = [];

    public Task<ProviderStatus> ProbeAsync(
        ProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _probeCount);
        LastSecret = request.Secret;
        Requests.Add(request);
        return Probe is null
            ? Task.FromResult(ProviderStatus.Ready())
            : Probe(request, cancellationToken);
    }

    public Task<ProviderEventResult> GetEventsAsync(
        ProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _getEventsCount);
        LastSecret = request.Secret;
        Requests.Add(request);
        RequestedRanges.Add(request.Range);

        return GetEvents is null
            ? Task.FromResult(ProviderEventResult.Ready(Events))
            : GetEvents(request, cancellationToken);
    }

    public void InvalidateCache(string accountId)
    {
        Interlocked.Increment(ref _invalidateCacheCount);
        LastInvalidatedAccountId = accountId;
    }
}
