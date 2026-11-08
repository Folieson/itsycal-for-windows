using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ItsycalForWindows.Models;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Refreshes provider accounts one range at a time with explicit per-account state, error
/// isolation, cancellation, secret delivery and optional caching.
/// </summary>
/// <remarks>
/// <para>
/// A failure in one provider never prevents the other accounts from syncing: every account is
/// attempted and reported separately in <see cref="ProviderSyncResult"/>. A caller cancellation
/// still propagates as <see cref="OperationCanceledException"/> so the host can abandon the range;
/// a provider-internal timeout is reported as a failed outcome instead.
/// </para>
/// <para>
/// The service resolves each account's secret from <see cref="IProviderSecretStore"/> and passes it
/// to the provider through <see cref="ProviderRequest"/>; providers never read the store
/// themselves. A per-account gate makes duplicate refreshes cheap and deterministic: when a sync
/// for the same account is already running, the second call returns
/// <see cref="ProviderSyncStatus.AlreadyRunning"/> instead of queueing another network request.
/// </para>
/// <para>
/// The service updates <see cref="IAccountStore.MarkSynced"/> only after a successful provider
/// fetch, never after a cached, skipped or failed one. An empty range is reported as
/// <see cref="ProviderSyncStatus.Skipped"/> and does not touch the last-sync timestamp. Account
/// metadata changes (enable/disable or a new remote address/token) invalidate the event cache and
/// the provider's own validators; <see cref="InvalidateAccount"/> does the same explicitly for a
/// disable/remove performed elsewhere.
/// </para>
/// </remarks>
public sealed class ProviderSyncService
{
    private readonly IAccountStore _accountStore;
    private readonly ProviderRegistry _registry;
    private readonly IProviderSecretStore? _secretStore;
    private readonly IProviderCache? _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProviderSyncState> _states = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AccountFingerprint> _fingerprints = new(StringComparer.Ordinal);

    /// <summary>Creates the service. The secret store and cache are optional.</summary>
    public ProviderSyncService(
        IAccountStore accountStore,
        ProviderRegistry registry,
        IProviderSecretStore? secretStore = null,
        IProviderCache? cache = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(accountStore);
        ArgumentNullException.ThrowIfNull(registry);

        _accountStore = accountStore;
        _registry = registry;
        _secretStore = secretStore;
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
    /// Forgets every cached event and provider validator for <paramref name="accountId"/>. Call
    /// this after disabling or removing an account so a disabled provider cannot keep serving
    /// cached content.
    /// </summary>
    public void InvalidateAccount(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        _fingerprints.TryRemove(accountId, out _);
        _cache?.Invalidate(accountId);

        var account = _accountStore.Find(accountId);
        if (account is not null)
        {
            InvalidateProviderCache(account);
            return;
        }

        // The account may have been removed already; every provider is asked to drop the id.
        foreach (var kind in _registry.Kinds)
        {
            if (_registry.TryGet(kind, out var provider) && provider is IProviderCacheInvalidator invalidator)
            {
                invalidator.InvalidateCache(accountId);
            }
        }
    }

    /// <summary>
    /// Refreshes every account in the store. Disabled accounts are skipped; failures are isolated
    /// per account. Cancellation propagates after the current account finishes.
    /// </summary>
    public Task<ProviderSyncResult> RefreshAsync(
        ProviderEventRange range,
        CancellationToken cancellationToken = default) =>
        RefreshAsync(range, forceRefresh: false, cancellationToken);

    /// <summary>
    /// Refreshes every account in the store. When <paramref name="forceRefresh"/> is true the
    /// in-memory event cache and every provider validator cache are bypassed, so a manual
    /// "refresh now" is never defeated by a cache hit.
    /// </summary>
    public async Task<ProviderSyncResult> RefreshAsync(
        ProviderEventRange range,
        bool forceRefresh,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var accounts = _accountStore.List();
        var outcomes = new List<ProviderSyncOutcome>(accounts.Count);

        foreach (var account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            outcomes.Add(await SyncAccountAsync(account, range, forceRefresh, cancellationToken).ConfigureAwait(false));
        }

        return new ProviderSyncResult(outcomes);
    }

    /// <summary>
    /// Refreshes one account. Expected provider failures become a failure outcome; caller
    /// cancellation propagates. A concurrent call for the same account returns
    /// <see cref="ProviderSyncStatus.AlreadyRunning"/>.
    /// </summary>
    public Task<ProviderSyncOutcome> SyncAccountAsync(
        ProviderAccount account,
        ProviderEventRange range,
        CancellationToken cancellationToken = default) =>
        SyncAccountAsync(account, range, forceRefresh: false, cancellationToken);

    /// <summary>
    /// Refreshes one account. <paramref name="forceRefresh"/> bypasses both the event cache and
    /// the provider's own conditional-request cache.
    /// </summary>
    public async Task<ProviderSyncOutcome> SyncAccountAsync(
        ProviderAccount account,
        ProviderEventRange range,
        bool forceRefresh,
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
            return await SyncCoreAsync(account, range, forceRefresh, cancellationToken).ConfigureAwait(false);
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
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!account.Enabled)
        {
            InvalidateCaches(account);
            return RecordSkipped(account, ProviderStatus.NotConfigured("The account is disabled."));
        }

        if (range.IsEmpty)
        {
            return RecordSkipped(account, ProviderStatus.Ready(message: "The requested range is empty."));
        }

        ProviderSecret? secret = null;
        if (_secretStore is not null)
        {
            secret = await _secretStore.GetAsync(account.Id, cancellationToken).ConfigureAwait(false);
        }

        var fingerprint = Fingerprint(account, secret);
        if (_fingerprints.TryGetValue(account.Id, out var previous) && previous != fingerprint)
        {
            // The address, token, kind or enabled flag changed since the last attempt: cached
            // events and ETag validators for the old configuration must not be reused.
            InvalidateCaches(account);
        }

        _fingerprints[account.Id] = fingerprint;

        if (!forceRefresh && _cache is not null && _cache.TryGet(account.Id, range, out var cached))
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

        if (forceRefresh)
        {
            _cache?.Invalidate(account.Id);
            if (provider is IProviderCacheInvalidator invalidator)
            {
                invalidator.InvalidateCache(account.Id);
            }
        }

        var probe = await provider.ProbeAsync(ProviderRequest.Probe(account, secret), cancellationToken)
            .ConfigureAwait(false);
        if (probe is null)
        {
            return RecordFailure(account, ProviderStatus.Unavailable("The provider did not return a status."));
        }

        if (!probe.IsReady)
        {
            return RecordStatus(account, probe);
        }

        var result = await provider
            .GetEventsAsync(ProviderRequest.Fetch(account, range, secret, forceRefresh), cancellationToken)
            .ConfigureAwait(false);
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

    private void InvalidateCaches(ProviderAccount account)
    {
        _cache?.Invalidate(account.Id);
        InvalidateProviderCache(account);
    }

    private void InvalidateProviderCache(ProviderAccount account)
    {
        if (_registry.TryGet(account.Kind, out var provider) && provider is IProviderCacheInvalidator invalidator)
        {
            invalidator.InvalidateCache(account.Id);
        }
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

    private ProviderSyncOutcome RecordSkipped(ProviderAccount account, ProviderStatus status)
    {
        var attemptedAt = _timeProvider.GetUtcNow();
        var lastSuccess = LastSuccessFor(account);
        _states[account.Id] = new ProviderSyncState(
            account.Id,
            status,
            attemptedAt,
            lastSuccess,
            LastError: null);

        return new ProviderSyncOutcome(
            account,
            ProviderSyncStatus.Skipped,
            status,
            lastSuccessAt: lastSuccess);
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

    private static AccountFingerprint Fingerprint(ProviderAccount account, ProviderSecret? secret) =>
        new(account.Kind, account.Enabled, account.RemoteId, HashResource(secret?.Resource));

    private static string HashResource(string? resource) =>
        string.IsNullOrWhiteSpace(resource)
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resource)), 0, 8);

    private static ProviderSyncStatus MapStatus(ProviderState state) => state switch
    {
        ProviderState.Ready => ProviderSyncStatus.Succeeded,
        ProviderState.NotConfigured => ProviderSyncStatus.NotConfigured,
        ProviderState.NotImplemented => ProviderSyncStatus.NotImplemented,
        ProviderState.Unauthorized => ProviderSyncStatus.Unauthorized,
        _ => ProviderSyncStatus.Failed,
    };

    private readonly record struct AccountFingerprint(
        ProviderKind Kind,
        bool Enabled,
        string? RemoteId,
        string ResourceHash);
}
