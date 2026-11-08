namespace ItsycalForWindows.Providers;

/// <summary>
/// <see cref="IOAuthTokenStore"/> over <see cref="IProviderSecretStore"/>: access token, refresh
/// token, absolute expiry and granted scope are stored in the account's DPAPI-protected record.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is written to <see cref="ProviderAccount"/>; the account JSON never carries a token.
/// Saving merges with the existing record so a rotated access token keeps the old refresh token
/// and scope when the token endpoint omits them, and any unrelated fields (for example a resource
/// address for another provider) are preserved.
/// </para>
/// <para>
/// The store has no logging path and no <see cref="object.ToString"/> that could reveal a token.
/// </para>
/// </remarks>
public sealed class OAuthTokenStore : IOAuthTokenStore
{
    private readonly IProviderSecretStore _secretStore;

    /// <summary>Creates the store over an encrypted provider secret store.</summary>
    public OAuthTokenStore(IProviderSecretStore secretStore)
    {
        ArgumentNullException.ThrowIfNull(secretStore);
        _secretStore = secretStore;
    }

    /// <inheritdoc />
    public async Task<OAuthToken?> GetAsync(
        string accountId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        cancellationToken.ThrowIfCancellationRequested();

        var secret = await _secretStore.GetAsync(accountId, cancellationToken).ConfigureAwait(false);
        return OAuthToken.FromSecret(secret);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        string accountId,
        OAuthToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            throw new ArgumentException("An OAuth access token is required to save the token set.", nameof(token));
        }

        var existing = await _secretStore.GetAsync(accountId, cancellationToken).ConfigureAwait(false);

        var merged = new ProviderSecret
        {
            AccessToken = token.AccessToken,
            RefreshToken = string.IsNullOrEmpty(token.RefreshToken)
                ? existing?.RefreshToken
                : token.RefreshToken,
            ExpiresAt = token.ExpiresAt,
            Scope = string.IsNullOrEmpty(token.Scope) ? existing?.Scope : token.Scope,
            Password = existing?.Password,
            Resource = existing?.Resource,
        };

        if (!_secretStore.Save(accountId, merged))
        {
            throw new InvalidOperationException("The OAuth token could not be stored securely.");
        }
    }

    /// <inheritdoc />
    public Task RemoveAsync(string accountId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        cancellationToken.ThrowIfCancellationRequested();

        _secretStore.Remove(accountId);
        return Task.CompletedTask;
    }
}
