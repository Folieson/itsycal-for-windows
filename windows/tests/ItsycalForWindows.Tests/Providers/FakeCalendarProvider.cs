using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Providers;

namespace ItsycalForWindows.Tests;

/// <summary>Configurable provider fake used by registry and sync-service tests.</summary>
internal sealed class FakeCalendarProvider : ICalendarProvider
{
    private int _probeCount;
    private int _getEventsCount;

    public ProviderKind Kind { get; init; } = ProviderKind.IcsSubscription;

    public string DisplayName => "Fake provider";

    public Func<ProviderAccount, CancellationToken, Task<ProviderStatus>>? Probe { get; init; }

    public Func<ProviderAccount, ProviderEventRange, CancellationToken, Task<ProviderEventResult>>? GetEvents { get; init; }

    public IReadOnlyList<ProviderEvent> Events { get; init; } = [];

    public int ProbeCount => Volatile.Read(ref _probeCount);

    public int GetEventsCount => Volatile.Read(ref _getEventsCount);

    public List<ProviderEventRange> RequestedRanges { get; } = [];

    public Task<ProviderStatus> ProbeAsync(ProviderAccount account, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _probeCount);
        return Probe is null
            ? Task.FromResult(ProviderStatus.Ready())
            : Probe(account, cancellationToken);
    }

    public Task<ProviderEventResult> GetEventsAsync(
        ProviderAccount account,
        ProviderEventRange range,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _getEventsCount);
        RequestedRanges.Add(range);

        return GetEvents is null
            ? Task.FromResult(ProviderEventResult.Ready(Events))
            : GetEvents(account, range, cancellationToken);
    }
}
