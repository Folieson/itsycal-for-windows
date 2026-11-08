namespace ItsycalForWindows.Providers;

/// <summary>
/// Features a provider can offer. The first shipping slice is read-only, so every provider
/// advertises at most <see cref="ReadOnlyEvents"/> plus an optional sync hint.
/// </summary>
[Flags]
public enum ProviderCapabilities
{
    /// <summary>No capability; a skeleton or an unconfigured account.</summary>
    None = 0,

    /// <summary>Can read events for a date range; no writes are offered.</summary>
    ReadOnlyEvents = 1 << 0,

    /// <summary>Supports conditional fetches (ETag/If-Modified-Since or a sync cursor).</summary>
    IncrementalSync = 1 << 1,

    /// <summary>Could notify about remote changes (not implemented in this slice).</summary>
    ChangeNotifications = 1 << 2,
}
