using System;
using System.Linq;
using System.Threading.Tasks;
using ItsycalForWindows.Models;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for the thin <see cref="ProviderEventSource"/> adapter: provider events become Core
/// calendar events, failures are isolated and the source honors the empty-range contract.
/// </summary>
public sealed class ProviderEventSourceTests
{
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 12, 1);

    [Fact]
    public async Task GetEventsAsync_AdaptsProviderEventsToCoreEvents()
    {
        var store = new InMemoryAccountStore();
        store.Add(Account("feed"));
        var start = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var provider = new FakeCalendarProvider
        {
            Events = [new ProviderEvent(new CalendarEvent("Provider event", start, start.AddHours(1)))],
        };
        var service = new ProviderSyncService(
            store,
            new ProviderRegistry([new DelegateProviderFactory(provider.Kind, () => provider)]));
        var source = new ProviderEventSource(service);

        var events = await source.GetEventsAsync(From, To);

        Assert.Single(events);
        Assert.Equal("Provider event", events[0].Subject);
        Assert.Equal(1, provider.GetEventsCount);
    }

    [Fact]
    public async Task GetEventsAsync_ProviderFailureReturnsEmptyInsteadOfThrowing()
    {
        var store = new InMemoryAccountStore();
        store.Add(Account("broken"));
        var provider = new FakeCalendarProvider
        {
            GetEvents = static (_, _) => throw new InvalidOperationException("boom"),
        };
        var service = new ProviderSyncService(
            store,
            new ProviderRegistry([new DelegateProviderFactory(provider.Kind, () => provider)]));
        var source = new ProviderEventSource(service);

        var events = await source.GetEventsAsync(From, To);

        Assert.Empty(events);
    }

    [Fact]
    public async Task GetEventsAsync_EmptyRangeSkipsTheProvider()
    {
        var store = new InMemoryAccountStore();
        store.Add(Account("empty"));
        var provider = new FakeCalendarProvider();
        var service = new ProviderSyncService(
            store,
            new ProviderRegistry([new DelegateProviderFactory(provider.Kind, () => provider)]));
        var source = new ProviderEventSource(service);

        var events = await source.GetEventsAsync(From, From);

        Assert.Empty(events);
        Assert.Equal(0, provider.ProbeCount);
        Assert.Equal(0, provider.GetEventsCount);
    }

    [Fact]
    public async Task ForceRefreshSource_BypassesTheCache()
    {
        var store = new InMemoryAccountStore();
        store.Add(Account("forced"));
        var provider = new FakeCalendarProvider
        {
            Events = [new ProviderEvent(new CalendarEvent(
                "Event",
                new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero)))],
        };
        var service = new ProviderSyncService(
            store,
            new ProviderRegistry([new DelegateProviderFactory(provider.Kind, () => provider)]),
            cache: new InMemoryProviderCache());
        var source = new ProviderEventSource(service, forceRefresh: true);

        await source.GetEventsAsync(From, To);
        await source.GetEventsAsync(From, To);

        Assert.Equal(2, provider.GetEventsCount);
    }

    private static ProviderAccount Account(string id) =>
        ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            id,
            remoteId: "https://example.com",
            id: id);
}
