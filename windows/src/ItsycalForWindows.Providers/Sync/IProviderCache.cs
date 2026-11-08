using ItsycalForWindows.Models;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Optional cache hook for <see cref="ProviderSyncService"/>. The sync service uses it before
/// probing a provider and fills it after a successful fetch; a caller that does not want caching
/// simply passes <c>null</c>.
/// </summary>
/// <remarks>
/// This is intentionally a small interface rather than a full offline cache: the first provider
/// slice only needs a place to hang a future persistent cache, and the tests need to prove that a
/// cache hit skips the provider. Implementations must be thread-safe.
/// </remarks>
public interface IProviderCache
{
    /// <summary>Tries to read the events for one account and range.</summary>
    bool TryGet(string accountId, ProviderEventRange range, out IReadOnlyList<CalendarEvent> events);

    /// <summary>Stores the events for one account and range.</summary>
    void Save(string accountId, ProviderEventRange range, IReadOnlyList<CalendarEvent> events);

    /// <summary>Drops every cached range for one account.</summary>
    void Invalidate(string accountId);
}
