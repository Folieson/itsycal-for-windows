namespace ItsycalForWindows.Providers;

/// <summary>
/// Implemented by a provider that keeps its own in-memory feed/validator cache (for example the
/// ICS ETag/If-Modified-Since text cache).
/// </summary>
/// <remarks>
/// <see cref="ProviderSyncService"/> calls <see cref="InvalidateCache"/> when an account is
/// disabled/removed, when its address changes, and before a forced refresh. Providers must drop
/// every cached body and validator for the account; they must never persist that content to disk.
/// </remarks>
public interface IProviderCacheInvalidator
{
    /// <summary>Drops every cached feed/validator value for <paramref name="accountId"/>.</summary>
    void InvalidateCache(string accountId);
}
