namespace ItsycalForWindows.Providers;

/// <summary>
/// Persistent secret store for provider credentials (OAuth tokens and CalDAV passwords).
/// </summary>
/// <remarks>
/// The production implementation encrypts the whole secrets document with DPAPI
/// (<c>DataProtectionScope.CurrentUser</c>) under
/// <c>%LOCALAPPDATA%\ItsycalForWindows\accounts</c>. Callers must treat the returned
/// <see cref="ProviderSecret"/> as sensitive: do not log it, serialize it into telemetry, put it
/// in a URL, or include it in a user-visible error. An unreadable/corrupt secret file reads as
/// "no secret" rather than throwing.
/// </remarks>
public interface IProviderSecretStore
{
    /// <summary>Loads the secret for <paramref name="accountId"/>; false when none is stored.</summary>
    bool TryGet(string accountId, out ProviderSecret? secret);

    /// <summary>
    /// Stores or replaces the secret for <paramref name="accountId"/>. An empty secret removes the
    /// entry. Throws <see cref="ArgumentException"/> for an empty account id.
    /// </summary>
    bool Save(string accountId, ProviderSecret secret);

    /// <summary>Removes the secret; false when no secret was stored.</summary>
    bool Remove(string accountId);
}
