using ItsycalForWindows.Models;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Bounded in-memory cache used as the default hook. It is process-local, expires after a short
/// TTL and deliberately not persistent: event content must not reach the disk until an encrypted
/// offline cache is designed and approved as a separate privacy decision.
/// </summary>
/// <remarks>
/// Entries expire after <see cref="DefaultTtl"/> (five minutes, matching ADR 0005 §5.4) unless a
/// caller supplies another value. Expired entries are removed on access, and the cache caps its
/// entry count so a long session cannot grow without bound. All members are thread-safe.
/// </remarks>
public sealed class InMemoryProviderCache : IProviderCache
{
    /// <summary>Default entry lifetime.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private const int MaxEntries = 128;

    private readonly object _gate = new();
    private readonly Dictionary<CacheKey, CacheEntry> _entries = [];
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _ttl;

    /// <summary>Creates a cache with the default five-minute TTL.</summary>
    public InMemoryProviderCache()
        : this(DefaultTtl, TimeProvider.System)
    {
    }

    /// <summary>Creates a cache with an explicit TTL and clock, primarily for tests.</summary>
    public InMemoryProviderCache(TimeSpan ttl, TimeProvider timeProvider)
    {
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "The cache TTL must be positive.");
        }

        _ttl = ttl;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Number of live entries; exposed for tests and diagnostics.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                RemoveExpiredUnlocked(_timeProvider.GetUtcNow());
                return _entries.Count;
            }
        }
    }

    /// <inheritdoc />
    public bool TryGet(string accountId, ProviderEventRange range, out IReadOnlyList<CalendarEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        lock (_gate)
        {
            var now = _timeProvider.GetUtcNow();
            RemoveExpiredUnlocked(now);

            if (_entries.TryGetValue(new CacheKey(accountId, range), out var entry))
            {
                events = entry.Events;
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
            var now = _timeProvider.GetUtcNow();
            RemoveExpiredUnlocked(now);

            if (_entries.Count >= MaxEntries)
            {
                _entries.Clear();
            }

            _entries[new CacheKey(accountId, range)] = new CacheEntry(
                events.ToArray(),
                now.Add(_ttl));
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

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private void RemoveExpiredUnlocked(DateTimeOffset now)
    {
        foreach (var key in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
        {
            _entries.Remove(key);
        }
    }

    private readonly record struct CacheEntry(IReadOnlyList<CalendarEvent> Events, DateTimeOffset ExpiresAt);

    private readonly record struct CacheKey(string AccountId, DateOnly FromInclusive, DateOnly ToExclusive)
    {
        public CacheKey(string accountId, ProviderEventRange range)
            : this(accountId, range.FromInclusive, range.ToExclusive)
        {
        }
    }
}
