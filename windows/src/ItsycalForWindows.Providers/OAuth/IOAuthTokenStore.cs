namespace ItsycalForWindows.Providers;

/// <summary>
/// Persists OAuth tokens for one account through the encrypted provider secret store.
/// </summary>
/// <remarks>
/// Implementations keep the token out of account metadata: only the opaque account id is used as
/// the key, and the production implementation is <see cref="OAuthTokenStore"/> over
/// <see cref="IProviderSecretStore"/> (per-account DPAPI). Sign-out/removal calls
/// <see cref="RemoveAsync"/> so no token survives the account.
/// </remarks>
public interface IOAuthTokenStore
{
    /// <summary>Loads the token for <paramref name="accountId"/>, or <c>null</c> when none is stored.</summary>
    Task<OAuthToken?> GetAsync(string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores or replaces the token. A missing refresh token or scope in <paramref name="token"/>
    /// keeps the previously stored value, so a refresh response that omits the refresh token does
    /// not destroy it.
    /// </summary>
    Task SaveAsync(string accountId, OAuthToken token, CancellationToken cancellationToken = default);

    /// <summary>Deletes every stored OAuth value for the account (sign-out/remove).</summary>
    Task RemoveAsync(string accountId, CancellationToken cancellationToken = default);
}
