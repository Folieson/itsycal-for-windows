using System.Collections.Concurrent;
using ItsycalForWindows.Models;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Refreshes provider accounts one range at a time with explicit per-account state, error
/// isolation, cancellation and optional caching.
/// </summary>
/// <remarks>
/// <para>
/// A failure in one provider never prevents the other accounts from syncing: every account is
/// attempted and reported separately in <see cref="ProviderSyncResult"/>. A caller cancellation
/// still propagates as <see cref="OperationCanceledException"/> so the host can abandon the range;
/// a provider-internal timeout is reported as a failed outcome instead.
/// </para>
/// <para>
/// A per-account gate makes duplicate refreshes cheap and deterministic: when a sync for the same
/// account is already running, the second call returns
/// <see cref="ProviderSyncStatus.AlreadyRunning"/> instead of queueing another network request.
/// The service updates <see cref="IAccountStore.MarkSynced"/> only after a successful provider
/// fetch, never after a cached or failed one.
/// </para>
/// </remarks>
public sealed class ProviderSyncService
{
    private readonly IAccountStore _accountStore;
    private readonly ProviderRegistry _registry;
    private readonly IProviderCache? _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProviderSyncState> _states = new(StringComparer.Ordinal);

    /// <summary>Creates the service. <paramref name="cache"/> is optional.</summary>
    public ProviderSyncService(
        IAccountStore accountStore,
        ProviderRegistry registry,
        IProviderCache? cache = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(accountStore);
        ArgumentNullException.ThrowIfNull(registry);

        _accountStore = accountStore;
        _registry = registry;
        _cache = cache;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Snapshot of the last known state for every account the service has considered.</summary>
    public IReadOnlyList<ProviderSyncState> States =>
        _states.Values.OrderBy(static state => state.AccountId, StringComparer.Ordinal).ToArray();

    /// <summary>Tries to read the last known state of one account.</summary>
    public bool TryGetState(string accountId, out ProviderSyncState? state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return _states.TryGetValue(accountId, out state);
    }

    /// <summary>
    /// Refreshes every account in the store. Disabled accounts are skipped; failures are isolated
    /// per account. Cancellation propagates after the current account finishes.
    /// </summary>
    public async Task<ProviderSyncResult> RefreshAsync(
        ProviderEventRange range,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var accounts = _accountStore.List();
        var outcomes = new List<ProviderSyncOutcome>(accounts.Count);

        foreach (var account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcomes.Add(await SyncAccountAsync(account, range, cancellationToken).ConfigureAwait(false));
        }

        return new ProviderSyncResult(outcomes);
    }

    /// <summary>
    /// Refreshes one account. Expected provider failures become a failure outcome; caller
    /// cancellation propagates. A concurrent call for the same account returns
    /// <see cref="ProviderSyncStatus.AlreadyRunning"/>.
    /// </summary>
    public async Task<ProviderSyncOutcome> SyncAccountAsync(
        ProviderAccount account,
        ProviderEventRange range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        cancellationToken.ThrowIfCancellationRequested();

        var gate = _gates.GetOrAdd(account.Id, static _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new ProviderSyncOutcome(
                account,
                ProviderSyncStatus.AlreadyRunning,
                ProviderStatus.Unavailable("A sync is already running for this account."),
                lastSuccessAt: LastSuccessFor(account));
        }

        try
        {
            return await SyncCoreAsync(account, range, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return RecordFailure(account, ProviderStatus.Unavailable("The provider request was cancelled."));
        }
#pragma warning disable CA1031 // A provider must never take the whole refresh down.
        catch (Exception)
        {
            return RecordFailure(account, ProviderStatus.Unavailable("The provider failed unexpectedly."));
        }
#pragma warning restore CA1031
        finally
        {
            gate.Release();
        }
    }

    private async Task<ProviderSyncOutcome> SyncCoreAsync(
        ProviderAccount account,
        ProviderEventRange range,
        CancellationToken cancellationToken)
    {
        if (!account.Enabled)
        {
            return new ProviderSyncOutcome(
                account,
                ProviderSyncStatus.Skipped,
                ProviderStatus.NotConfigured("The account is disabled."),
                lastSuccessAt: LastSuccessFor(account));
        }

        if (range.IsEmpty)
        {
            return RecordSuccess(
                account,
                ProviderStatus.Ready(),
                [],
                _timeProvider.GetUtcNow());
        }

        if (_cache is not null && _cache.TryGet(account.Id, range, out var cached))
        {
            var cachedAt = _timeProvider.GetUtcNow();
            var lastSuccess = LastSuccessFor(account);
            var cachedStatus = ProviderStatus.Ready();
            _states[account.Id] = new ProviderSyncState(
                account.Id,
                cachedStatus,
                cachedAt,
                lastSuccess,
                LastError: null);

            return new ProviderSyncOutcome(
                account,
                ProviderSyncStatus.Cached,
                cachedStatus,
                cached,
                lastSuccess);
        }

        if (!_registry.TryGet(account.Kind, out var provider) || provider is null)
        {
            return RecordFailure(
                account,
                ProviderStatus.Unavailable($"No provider is registered for {account.Kind}."));
        }

        var probe = await provider.ProbeAsync(account, cancellationToken).ConfigureAwait(false);
        if (probe is null)
        {
            return RecordFailure(account, ProviderStatus.Unavailable("The provider did not return a status."));
        }

        if (!probe.IsReady)
        {
            return RecordStatus(account, probe);
        }

        var result = await provider.GetEventsAsync(account, range, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return RecordFailure(account, ProviderStatus.Unavailable("The provider did not return a result."));
        }

        if (!result.Status.IsReady)
        {
            return RecordStatus(account, result.Status, result.ToCalendarEvents());
        }

        var events = result.ToCalendarEvents();
        _cache?.Save(account.Id, range, events);

        var syncedAt = _timeProvider.GetUtcNow();
        _accountStore.MarkSynced(account.Id, syncedAt);
        var updatedAccount = account.WithLastSync(syncedAt);

        return RecordSuccess(updatedAccount, result.Status, events, syncedAt);
    }

    private ProviderSyncOutcome RecordSuccess(
        ProviderAccount account,
        ProviderStatus status,
        IReadOnlyList<CalendarEvent> events,
        DateTimeOffset syncedAt)
    {
        _states[account.Id] = new ProviderSyncState(
            account.Id,
            status,
            syncedAt,
            syncedAt,
            LastError: null);

        return new ProviderSyncOutcome(
            account,
            ProviderSyncStatus.Succeeded,
            status,
            events,
            syncedAt);
    }

    private ProviderSyncOutcome RecordStatus(
        ProviderAccount account,
        ProviderStatus status,
        IReadOnlyList<CalendarEvent>? events = null)
    {
        var attemptedAt = _timeProvider.GetUtcNow();
        var lastSuccess = LastSuccessFor(account);
        var lastError = status.IsConfigured && !status.IsReady ? status.Message : null;
        _states[account.Id] = new ProviderSyncState(
            account.Id,
            status,
            attemptedAt,
            lastSuccess,
            lastError);

        return new ProviderSyncOutcome(
            account,
            MapStatus(status.State),
            status,
            events,
            lastSuccess,
            lastError);
    }

    private ProviderSyncOutcome RecordFailure(ProviderAccount account, ProviderStatus status)
    {
        return RecordStatus(account, status);
    }

    private DateTimeOffset? LastSuccessFor(ProviderAccount account) =>
        _states.TryGetValue(account.Id, out var state) ? state.LastSuccessAt : account.LastSyncAt;

    private static ProviderSyncStatus MapStatus(ProviderState state) => state switch
    {
        ProviderState.Ready => ProviderSyncStatus.Succeeded,
        ProviderState.NotConfigured => ProviderSyncStatus.NotConfigured,
        ProviderState.NotImplemented => ProviderSyncStatus.NotImplemented,
        ProviderState.Unauthorized => ProviderSyncStatus.Unauthorized,
        _ => ProviderSyncStatus.Failed,
    };
}
