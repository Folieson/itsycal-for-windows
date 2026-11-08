using ItsycalForWindows.Models;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Bounded in-memory cache used as the default hook. It is process-local and deliberately not
/// persistent: offline-first persistence is a follow-up, but the sync service contract is already
/// cache-shaped.
/// </summary>
public sealed class InMemoryProviderCache : IProviderCache
{
    private const int MaxEntries = 128;

    private readonly object _gate = new();
    private readonly Dictionary<CacheKey, IReadOnlyList<CalendarEvent>> _entries = [];

    /// <inheritdoc />
    public bool TryGet(string accountId, ProviderEventRange range, out IReadOnlyList<CalendarEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        lock (_gate)
        {
            if (_entries.TryGetValue(new CacheKey(accountId, range), out var cached))
            {
                events = cached;
                return true;
            }
        }

        events = [];
        return false;
    }

    /// <inheritdoc />
    public void Save(string accountId, ProviderEventRange range, IReadOnlyList<CalendarEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(events);

        lock (_gate)
        {
            if (_entries.Count >= MaxEntries)
            {
                _entries.Clear();
            }

            _entries[new CacheKey(accountId, range)] = events.ToArray();
        }
    }

    /// <inheritdoc />
    public void Invalidate(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        lock (_gate)
        {
            foreach (var key in _entries.Keys.Where(key => key.AccountId == accountId).ToArray())
            {
                _entries.Remove(key);
            }
        }
    }

    private readonly record struct CacheKey(string AccountId, DateOnly FromInclusive, DateOnly ToExclusive)
    {
        public CacheKey(string accountId, ProviderEventRange range)
            : this(accountId, range.FromInclusive, range.ToExclusive)
        {
        }
    }
}
