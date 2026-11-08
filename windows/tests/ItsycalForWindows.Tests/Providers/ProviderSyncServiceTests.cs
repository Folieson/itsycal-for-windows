using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Models;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for <see cref="ProviderSyncService"/>: success, error isolation, cancellation, duplicate
/// sync, cache hits, disabled accounts, unknown kinds and last-sync persistence.
/// </summary>
public sealed class ProviderSyncServiceTests
{
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 12, 1);
    private static readonly ProviderEventRange Range = ProviderEventRange.Days(From, To);

    [Fact]
    public async Task RefreshAsync_SyncsEveryAccountAndIsolatesFailures()
    {
        var store = new InMemoryAccountStore();
        store.Add(Account("ok"));
        store.Add(Account("boom"));
        store.Add(Account("later"));

        var provider = new FakeCalendarProvider
        {
            GetEvents = (account, _, _) => account.Id switch
            {
                "ok" => Task.FromResult(ProviderEventResult.Ready([new ProviderEvent(Event("A"))])),
                "boom" => throw new InvalidOperationException("provider exploded"),
                _ => Task.FromResult(ProviderEventResult.Empty(ProviderStatus.NotImplemented("not yet"))),
            },
        };

        var service = CreateService(store, provider);
        var result = await service.RefreshAsync(Range);

        Assert.Equal(3, result.Outcomes.Count);
        Assert.Equal(ProviderSyncStatus.Succeeded, Outcome(result, "ok").Status);
        Assert.Equal(ProviderSyncStatus.Failed, Outcome(result, "boom").Status);
        Assert.Equal(ProviderSyncStatus.NotImplemented, Outcome(result, "later").Status);
        Assert.Single(result.Events);
        Assert.Equal("A", result.Events[0].Subject);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
    }

    [Fact]
    public async Task SyncAccountAsync_UsesCacheAndDoesNotCallProviderAgain()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("cached"));
        var provider = new FakeCalendarProvider
        {
            Events = [new ProviderEvent(Event("Cached"))],
        };
        var cache = new InMemoryProviderCache();
        var service = CreateService(store, provider, cache);

        var first = await service.SyncAccountAsync(account, Range);
        var second = await service.SyncAccountAsync(account, Range);

        Assert.Equal(ProviderSyncStatus.Succeeded, first.Status);
        Assert.Equal(ProviderSyncStatus.Cached, second.Status);
        Assert.Equal(1, provider.GetEventsCount);
        Assert.Single(second.Events);
        Assert.Equal(1, store.MarkSyncedCount);
    }

    [Fact]
    public async Task SyncAccountAsync_DuplicateCallReturnsAlreadyRunning()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("busy"));

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeCalendarProvider
        {
            GetEvents = async (_, _, _) =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return ProviderEventResult.Ready([]);
            },
        };

        var service = CreateService(store, provider);
        var first = service.SyncAccountAsync(account, Range);
        await started.Task.ConfigureAwait(false);

        var second = await service.SyncAccountAsync(account, Range).ConfigureAwait(false);
        Assert.Equal(ProviderSyncStatus.AlreadyRunning, second.Status);

        release.SetResult();
        var firstResult = await first.ConfigureAwait(false);
        Assert.Equal(ProviderSyncStatus.Succeeded, firstResult.Status);
        Assert.Equal(1, provider.GetEventsCount);
    }

    [Fact]
    public async Task SyncAccountAsync_ProbeNotConfiguredDoesNotFetch()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("setup"));
        var provider = new FakeCalendarProvider
        {
            Probe = static (_, _) => Task.FromResult(ProviderStatus.NotConfigured("add credentials")),
        };
        var service = CreateService(store, provider);

        var outcome = await service.SyncAccountAsync(account, Range);

        Assert.Equal(ProviderSyncStatus.NotConfigured, outcome.Status);
        Assert.Equal(0, provider.GetEventsCount);
        Assert.Equal(0, store.MarkSyncedCount);
        Assert.Contains("credentials", outcome.ProviderStatus.Message!, StringComparison.OrdinalIgnoreCase);
        Assert.Null(outcome.Error);
    }

    [Fact]
    public async Task SyncAccountAsync_DisabledAccountIsSkipped()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("disabled", enabled: false));
        var provider = new FakeCalendarProvider();
        var service = CreateService(store, provider);

        var outcome = await service.SyncAccountAsync(account, Range);

        Assert.Equal(ProviderSyncStatus.Skipped, outcome.Status);
        Assert.Equal(0, provider.ProbeCount);
        Assert.Equal(0, provider.GetEventsCount);
    }

    [Fact]
    public async Task SyncAccountAsync_UnknownKindFailsWithoutThrowing()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("unknown"));
        var service = new ProviderSyncService(store, new ProviderRegistry([]));

        var outcome = await service.SyncAccountAsync(account, Range);

        Assert.Equal(ProviderSyncStatus.Failed, outcome.Status);
        Assert.Contains("registered", outcome.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyncAccountAsync_UpdatesLastSyncOnlyAfterSuccess()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("persisted"));
        var provider = new FakeCalendarProvider { Events = [new ProviderEvent(Event("A"))] };
        var service = CreateService(store, provider);

        var outcome = await service.SyncAccountAsync(account, Range);

        Assert.Equal(ProviderSyncStatus.Succeeded, outcome.Status);
        Assert.Equal(1, store.MarkSyncedCount);
        Assert.NotNull(store.LastMarkedSync);
        Assert.Equal(store.LastMarkedSync, outcome.LastSuccessAt);
        Assert.True(service.TryGetState(account.Id, out var state));
        Assert.True(state!.IsSuccess);
        Assert.NotNull(state.LastSuccessAt);
    }

    [Fact]
    public async Task SyncAccountAsync_FailureDoesNotUpdateLastSync()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("failing"));
        var provider = new FakeCalendarProvider
        {
            GetEvents = static (_, _, _) => throw new InvalidOperationException("boom"),
        };
        var service = CreateService(store, provider);

        var outcome = await service.SyncAccountAsync(account, Range);

        Assert.Equal(ProviderSyncStatus.Failed, outcome.Status);
        Assert.Equal(0, store.MarkSyncedCount);
        Assert.NotNull(outcome.Error);
        Assert.DoesNotContain("boom", outcome.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncAccountAsync_PreCanceledTokenThrows()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("cancel"));
        var provider = new FakeCalendarProvider();
        var service = CreateService(store, provider);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SyncAccountAsync(account, Range, cancellation.Token));
        Assert.Equal(0, provider.ProbeCount);
    }

    [Fact]
    public async Task SyncAccountAsync_CancellationDuringProviderPropagates()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("slow"));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeCalendarProvider
        {
            GetEvents = async (_, _, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                return ProviderEventResult.Ready([]);
            },
        };
        var service = CreateService(store, provider);

        using var cancellation = new CancellationTokenSource();
        var task = service.SyncAccountAsync(account, Range, cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task SyncAccountAsync_EmptyRangeSkipsTheProvider()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("empty-range"));
        var provider = new FakeCalendarProvider();
        var service = CreateService(store, provider);

        var outcome = await service.SyncAccountAsync(account, ProviderEventRange.Days(From, From));

        Assert.Equal(ProviderSyncStatus.Succeeded, outcome.Status);
        Assert.Empty(outcome.Events);
        Assert.Equal(0, provider.ProbeCount);
        Assert.Equal(0, provider.GetEventsCount);
    }

    [Fact]
    public async Task RefreshAsync_PreCanceledTokenThrows()
    {
        var store = new InMemoryAccountStore();
        store.Add(Account("a"));
        var service = CreateService(store, new FakeCalendarProvider());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RefreshAsync(Range, cancellation.Token));
    }

    private static ProviderSyncService CreateService(
        IAccountStore store,
        FakeCalendarProvider provider,
        IProviderCache? cache = null) =>
        new(
            store,
            new ProviderRegistry([new DelegateProviderFactory(provider.Kind, () => provider)]),
            cache,
            TimeProvider.System);

    private static ProviderAccount Account(string id, bool enabled = true) =>
        ProviderAccount.Create(ProviderKind.IcsSubscription, id, id: id, enabled: enabled);

    private static CalendarEvent Event(string subject) =>
        new(subject, new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero));

    private static ProviderSyncOutcome Outcome(ProviderSyncResult result, string accountId) =>
        result.Outcomes.Single(outcome => outcome.Account.Id == accountId);
}
