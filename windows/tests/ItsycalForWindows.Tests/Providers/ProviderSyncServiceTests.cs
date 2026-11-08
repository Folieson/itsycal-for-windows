using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Models;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for <see cref="ProviderSyncService"/>: success, error isolation, secret delivery,
/// cancellation, duplicate sync, cache TTL/force refresh/invalidation, skipped accounts, unknown
/// kinds and last-sync persistence.
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
            GetEvents = (request, _) => request.Account.Id switch
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
        var service = CreateService(store, provider, cache: cache);

        var first = await service.SyncAccountAsync(account, Range);
        var second = await service.SyncAccountAsync(account, Range);

        Assert.Equal(ProviderSyncStatus.Succeeded, first.Status);
        Assert.Equal(ProviderSyncStatus.Cached, second.Status);
        Assert.Equal(1, provider.GetEventsCount);
        Assert.Single(second.Events);
        Assert.Equal(1, store.MarkSyncedCount);
    }

    [Fact]
    public async Task CacheEntry_ExpiresAfterTheTtl()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("ttl"));
        var provider = new FakeCalendarProvider { Events = [new ProviderEvent(Event("A"))] };
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero));
        var cache = new InMemoryProviderCache(TimeSpan.FromMinutes(5), clock);
        var service = CreateService(store, provider, cache: cache, timeProvider: clock);

        await service.SyncAccountAsync(account, Range);
        clock.Now = clock.Now.AddMinutes(4);
        Assert.Equal(ProviderSyncStatus.Cached, (await service.SyncAccountAsync(account, Range)).Status);

        clock.Now = clock.Now.AddMinutes(2);
        var afterExpiry = await service.SyncAccountAsync(account, Range);

        Assert.Equal(ProviderSyncStatus.Succeeded, afterExpiry.Status);
        Assert.Equal(2, provider.GetEventsCount);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task ForceRefresh_BypassesTheCacheAndProviderValidators()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("forced"));
        var provider = new FakeCalendarProvider { Events = [new ProviderEvent(Event("A"))] };
        var cache = new InMemoryProviderCache();
        var service = CreateService(store, provider, cache: cache);

        await service.SyncAccountAsync(account, Range);
        var forced = await service.SyncAccountAsync(account, Range, forceRefresh: true);

        Assert.Equal(ProviderSyncStatus.Succeeded, forced.Status);
        Assert.Equal(2, provider.GetEventsCount);
        Assert.True(provider.InvalidateCacheCount >= 1);
        Assert.Equal(account.Id, provider.LastInvalidatedAccountId);
    }

    [Fact]
    public async Task ChangedSecret_InvalidatesTheCacheAndTheFeedCache()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("changed"));
        var secrets = new InMemorySecretStore();
        secrets.Save(account.Id, new ProviderSecret { Resource = "https://example.com/one.ics" });
        var provider = new FakeCalendarProvider { Events = [new ProviderEvent(Event("A"))] };
        var cache = new InMemoryProviderCache();
        var service = CreateService(store, provider, secrets: secrets, cache: cache);

        await service.SyncAccountAsync(account, Range);
        Assert.Equal(ProviderSyncStatus.Cached, (await service.SyncAccountAsync(account, Range)).Status);

        secrets.Save(account.Id, new ProviderSecret { Resource = "https://example.com/two.ics" });
        var refreshed = await service.SyncAccountAsync(account, Range);

        Assert.Equal(ProviderSyncStatus.Succeeded, refreshed.Status);
        Assert.Equal(2, provider.GetEventsCount);
        Assert.True(provider.InvalidateCacheCount >= 1);
    }

    [Fact]
    public async Task DisabledAccount_IsSkippedAndInvalidatesCaches()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("disabled"));
        var provider = new FakeCalendarProvider { Events = [new ProviderEvent(Event("A"))] };
        var cache = new InMemoryProviderCache();
        var service = CreateService(store, provider, cache: cache);

        await service.SyncAccountAsync(account, Range);
        Assert.Equal(1, cache.Count);

        store.SetEnabled(account.Id, false);
        var outcome = await service.SyncAccountAsync(store.Find(account.Id)!, Range);

        Assert.Equal(ProviderSyncStatus.Skipped, outcome.Status);
        Assert.Equal(0, cache.Count);
        Assert.Equal(1, provider.InvalidateCacheCount);
        Assert.Equal(1, store.MarkSyncedCount);
        Assert.Equal(1, provider.ProbeCount);
    }

    [Fact]
    public async Task InvalidateAccount_ClearsEventAndProviderCaches()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("remove"));
        var provider = new FakeCalendarProvider { Events = [new ProviderEvent(Event("A"))] };
        var cache = new InMemoryProviderCache();
        var service = CreateService(store, provider, cache: cache);

        await service.SyncAccountAsync(account, Range);
        store.Remove(account.Id);
        service.InvalidateAccount(account.Id);

        Assert.Equal(0, cache.Count);
        Assert.True(provider.InvalidateCacheCount >= 1);
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
            GetEvents = async (_, _) =>
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
    public async Task SyncAccountAsync_DeliversTheResolvedSecretToTheProvider()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("secret"));
        var secrets = new InMemorySecretStore();
        var secret = new ProviderSecret { Resource = "https://example.com/private/feed.ics" };
        secrets.Save(account.Id, secret);
        var provider = new FakeCalendarProvider { Events = [new ProviderEvent(Event("A"))] };
        var service = CreateService(store, provider, secrets: secrets);

        var outcome = await service.SyncAccountAsync(account, Range);

        Assert.Equal(ProviderSyncStatus.Succeeded, outcome.Status);
        Assert.Equal(1, secrets.GetAsyncCount);
        Assert.Equal(account.Id, secrets.RequestedAccountIds[0]);
        Assert.Same(secret, provider.LastSecret);
        Assert.All(provider.Requests, request => Assert.Same(secret, request.Secret));
    }

    [Fact]
    public async Task SyncAccountAsync_WithoutStoredSecretPassesNull()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("no-secret"));
        var provider = new FakeCalendarProvider { Events = [new ProviderEvent(Event("A"))] };
        var service = CreateService(store, provider, secrets: new InMemorySecretStore());

        var outcome = await service.SyncAccountAsync(account, Range);

        Assert.Equal(ProviderSyncStatus.Succeeded, outcome.Status);
        Assert.Null(provider.LastSecret);
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
            GetEvents = static (_, _) => throw new InvalidOperationException("boom"),
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
            GetEvents = async (_, cancellationToken) =>
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
    public async Task SyncAccountAsync_EmptyRangeIsSkippedWithoutMarkSynced()
    {
        var store = new InMemoryAccountStore();
        var account = store.Add(Account("empty-range"));
        var provider = new FakeCalendarProvider();
        var service = CreateService(store, provider);

        var outcome = await service.SyncAccountAsync(account, ProviderEventRange.Days(From, From));

        Assert.Equal(ProviderSyncStatus.Skipped, outcome.Status);
        Assert.Empty(outcome.Events);
        Assert.Equal(0, store.MarkSyncedCount);
        Assert.Equal(0, provider.ProbeCount);
        Assert.Equal(0, provider.GetEventsCount);
        Assert.True(service.TryGetState(account.Id, out var state));
        Assert.True(state!.Status.IsReady);
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

    [Fact]
    public async Task RefreshAsync_ForceRefreshIsPropagated()
    {
        var store = new InMemoryAccountStore();
        store.Add(Account("forced-refresh"));
        var provider = new FakeCalendarProvider { Events = [new ProviderEvent(Event("A"))] };
        var cache = new InMemoryProviderCache();
        var service = CreateService(store, provider, cache: cache);

        await service.RefreshAsync(Range);
        var result = await service.RefreshAsync(Range, forceRefresh: true);

        Assert.Equal(ProviderSyncStatus.Succeeded, result.Outcomes.Single().Status);
        Assert.Equal(2, provider.GetEventsCount);
        Assert.True(provider.Requests[^1].ForceRefresh);
    }

    private static ProviderSyncService CreateService(
        IAccountStore store,
        FakeCalendarProvider provider,
        IProviderSecretStore? secrets = null,
        IProviderCache? cache = null,
        TimeProvider? timeProvider = null) =>
        new(
            store,
            new ProviderRegistry([new DelegateProviderFactory(provider.Kind, () => provider)]),
            secrets,
            cache,
            timeProvider ?? TimeProvider.System);

    private static ProviderAccount Account(string id, bool enabled = true) =>
        ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            id,
            remoteId: "https://example.com",
            id: id,
            enabled: enabled);

    private static CalendarEvent Event(string subject) =>
        new(subject, new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero));

    private static ProviderSyncOutcome Outcome(ProviderSyncResult result, string accountId) =>
        result.Outcomes.Single(outcome => outcome.Account.Id == accountId);
}
