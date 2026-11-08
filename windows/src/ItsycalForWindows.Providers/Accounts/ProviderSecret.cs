namespace ItsycalForWindows.Providers;

/// <summary>
/// Secret material for one account: OAuth access/refresh tokens or a CalDAV app password.
/// </summary>
/// <remarks>
/// <para>
/// Secrets are stored separately from <see cref="ProviderAccount"/> and are encrypted with DPAPI
/// (current user) plus application entropy. They must never be logged, serialized into the
/// accounts file, put into a URL, or included in an error message. The custom <see cref="ToString"/>
/// redacts every secret value so an accidental interpolation or debugger display cannot leak a
/// token.
/// </para>
/// <para>
/// A provider-specific flow may use only one of the properties (for example
/// <see cref="AccessToken"/> + <see cref="RefreshToken"/> for OAuth and <see cref="Password"/> for
/// CalDAV). Empty secrets are not persisted by <see cref="DpapiAccountStore"/>.
/// </para>
/// </remarks>
public sealed record ProviderSecret
{
    /// <summary>OAuth access token; short-lived and refreshed lazily by the provider.</summary>
    public string? AccessToken { get; init; }

    /// <summary>OAuth refresh token used to obtain a new access token.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>Optional access-token expiry in UTC.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>CalDAV password or app password.</summary>
    public string? Password { get; init; }

    /// <summary>True when this value carries no secret at all and can be removed.</summary>
    public bool IsEmpty =>
        string.IsNullOrEmpty(AccessToken) &&
        string.IsNullOrEmpty(RefreshToken) &&
        string.IsNullOrEmpty(Password) &&
        ExpiresAt is null;

    /// <summary>True when an OAuth access or refresh token is present.</summary>
    public bool HasOAuthToken =>
        !string.IsNullOrEmpty(AccessToken) || !string.IsNullOrEmpty(RefreshToken);

    /// <summary>Redacted representation; it never contains a token or password value.</summary>
    public override string ToString() =>
        $"ProviderSecret {{ AccessToken = {Redact(AccessToken)}, RefreshToken = {Redact(RefreshToken)}, " +
        $"Password = {Redact(Password)}, ExpiresAt = {ExpiresAt?.ToString("O") ?? "(none)"} }}";

    private static string Redact(string? value) => string.IsNullOrEmpty(value) ? "(none)" : "(redacted)";
}
