using ItsycalForWindows.Models;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Optional cache hook for <see cref="ProviderSyncService"/>. The sync service uses it before
/// probing a provider and fills it after a successful fetch; a caller that does not want caching
/// simply passes <c>null</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is intentionally a small interface rather than a full offline cache: the first provider
/// slice only needs a bounded, expiring in-memory cache, and the tests need to prove that a cache
/// hit skips the provider, that a forced refresh bypasses it and that disabling/removing/changing
/// an account invalidates it. Implementations must be thread-safe.
/// </para>
/// <para>
/// Event content must stay in memory only. Persistent/offline cache is a separate privacy decision
/// (encrypted cache) and is not part of this interface yet.
/// </para>
/// </remarks>
public interface IProviderCache
{
    /// <summary>Tries to read the events for one account and range.</summary>
    bool TryGet(string accountId, ProviderEventRange range, out IReadOnlyList<CalendarEvent> events);

    /// <summary>Stores the events for one account and range.</summary>
    void Save(string accountId, ProviderEventRange range, IReadOnlyList<CalendarEvent> events);

    /// <summary>Drops every cached range for one account.</summary>
    void Invalidate(string accountId);

    /// <summary>Drops every cached entry.</summary>
    void Clear();
}
