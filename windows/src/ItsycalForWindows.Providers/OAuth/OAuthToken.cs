namespace ItsycalForWindows.Providers;

/// <summary>
/// One OAuth token set: access token, optional refresh token, absolute expiry and granted scope.
/// </summary>
/// <remarks>
/// This is the in-memory representation; at rest the same values live in a
/// <see cref="ProviderSecret"/> protected by <see cref="IProviderSecretStore"/> (DPAPI). The
/// tokens are credentials: never log them, serialize them into account metadata or print them via
/// <see cref="ToString"/> (which redacts every value). <see cref="ExpiresAt"/> is stored as an
/// absolute UTC instant so a restart can decide whether a refresh is needed without trusting a
/// local clock delta.
/// </remarks>
public sealed record OAuthToken
{
    /// <summary>Bearer access token. May be empty when only a refresh token is known.</summary>
    public required string AccessToken { get; init; }

    /// <summary>Refresh token, when the provider issued one.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Token type reported by the provider, normally <c>Bearer</c>.</summary>
    public string? TokenType { get; init; }

    /// <summary>Space-delimited granted scope, exactly as returned by the token endpoint.</summary>
    public string? Scope { get; init; }

    /// <summary>Absolute UTC expiry of <see cref="AccessToken"/>, or <c>null</c> when unknown.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// True when the access token is unknown-length or expired at <paramref name="now"/> plus the
    /// optional safety <paramref name="skew"/>. A token without an expiry is treated as usable.
    /// </summary>
    public bool IsExpired(DateTimeOffset now, TimeSpan? skew = null) =>
        ExpiresAt is { } expiresAt && now + (skew ?? TimeSpan.Zero) >= expiresAt;

    /// <summary>True when an access token value is present and not expired within the skew.</summary>
    public bool HasUsableAccessToken(DateTimeOffset now, TimeSpan? skew = null) =>
        !string.IsNullOrEmpty(AccessToken) && !IsExpired(now, skew);

    /// <summary>
    /// Creates the in-memory token from a stored secret, or <c>null</c> when the secret has no
    /// OAuth token at all. A refresh-only secret is returned with an empty access token.
    /// </summary>
    public static OAuthToken? FromSecret(ProviderSecret? secret)
    {
        if (secret is null ||
            (string.IsNullOrEmpty(secret.AccessToken) && string.IsNullOrEmpty(secret.RefreshToken)))
        {
            return null;
        }

        return new OAuthToken
        {
            AccessToken = secret.AccessToken ?? string.Empty,
            RefreshToken = secret.RefreshToken,
            ExpiresAt = secret.ExpiresAt,
            Scope = secret.Scope,
        };
    }

    /// <summary>Redacted representation; no token value is ever printed.</summary>
    public override string ToString() =>
        $"OAuthToken {{ AccessToken = {Presence(AccessToken)}, RefreshToken = {Presence(RefreshToken)}, " +
        $"ExpiresAt = {ExpiresAt?.ToString("O") ?? "(none)"}, Scope = {Presence(Scope)} }}";

    private static string Presence(string? value) => string.IsNullOrEmpty(value) ? "(none)" : "(redacted)";
}
