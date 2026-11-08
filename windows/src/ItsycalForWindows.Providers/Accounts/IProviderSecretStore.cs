namespace ItsycalForWindows.Providers;

/// <summary>
/// Persistent secret store for provider credentials (OAuth tokens, CalDAV passwords and
/// token-bearing ICS feed addresses).
/// </summary>
/// <remarks>
/// <para>
/// The production implementation encrypts each account record separately with DPAPI
/// (<c>DataProtectionScope.CurrentUser</c> and per-account entropy) in
/// <c>%LOCALAPPDATA%\ItsycalForWindows\accounts\secrets.dat</c>. Callers must treat the returned
/// <see cref="ProviderSecret"/> as sensitive: do not log it, serialize it into telemetry, put it
/// in a URL that is shown, or include it in a user-visible error. An unreadable/corrupt secret
/// record reads as "no secret" rather than throwing.
/// </para>
/// <para>
/// <see cref="ProviderSyncService"/> resolves the secret before probing or fetching and passes it
/// to the provider through <see cref="ProviderRequest"/>; providers do not read the store
/// themselves. A missing secret is represented by <c>null</c> and the provider answers
/// <see cref="ProviderState.NotConfigured"/> rather than guessing an address.
/// </para>
/// </remarks>
public interface IProviderSecretStore
{
    /// <summary>Loads the secret for <paramref name="accountId"/>; false when none is stored.</summary>
    bool TryGet(string accountId, out ProviderSecret? secret);

    /// <summary>
    /// Asynchronously loads the secret for <paramref name="accountId"/>, or <c>null</c> when none
    /// is stored. Implementations must treat cancellation as caller cancellation.
    /// </summary>
    Task<ProviderSecret?> GetAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores or replaces the secret for <paramref name="accountId"/>. An empty secret removes the
    /// entry. Throws <see cref="ArgumentException"/> for an empty account id.
    /// </summary>
    bool Save(string accountId, ProviderSecret secret);

    /// <summary>Removes the secret; false when no secret was stored.</summary>
    bool Remove(string accountId);
}
