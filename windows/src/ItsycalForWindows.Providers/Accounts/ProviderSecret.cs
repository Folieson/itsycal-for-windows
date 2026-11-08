namespace ItsycalForWindows.Providers;

/// <summary>
/// Secret material for one account: OAuth access/refresh tokens, a CalDAV app password or a
/// token-bearing ICS subscription address.
/// </summary>
/// <remarks>
/// <para>
/// Secrets are stored separately from <see cref="ProviderAccount"/> and are encrypted with DPAPI
/// (current user) plus per-account application entropy. They must never be logged, serialized into
/// the accounts file, put into a URL that is shown or logged, or included in an error message. The
/// custom <see cref="ToString"/> redacts every secret value so an accidental interpolation or
/// debugger display cannot leak a token.
/// </para>
/// <para>
/// A provider-specific flow may use only one of the properties (for example
/// <see cref="AccessToken"/> + <see cref="RefreshToken"/> for OAuth, <see cref="Password"/> for
/// CalDAV and <see cref="Resource"/> for an ICS subscription). Empty secrets are not persisted by
/// <see cref="DpapiAccountStore"/>.
/// </para>
/// <para>
/// <see cref="Resource"/> is the full user-entered resource address that cannot be stored as
/// account metadata: for an ICS subscription it is the complete feed URL (including a
/// token-bearing path or query) or a local <c>.ics</c> path. A published feed link frequently
/// embeds an opaque credential, so the address is treated as a secret.
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

    /// <summary>
    /// Full secret resource address: an ICS feed URL/token or a local <c>.ics</c> path. Never
    /// stored in <see cref="ProviderAccount.RemoteId"/>.
    /// </summary>
    public string? Resource { get; init; }

    /// <summary>True when this value carries no secret at all and can be removed.</summary>
    public bool IsEmpty =>
        string.IsNullOrEmpty(AccessToken) &&
        string.IsNullOrEmpty(RefreshToken) &&
        string.IsNullOrEmpty(Password) &&
        string.IsNullOrEmpty(Resource) &&
        ExpiresAt is null;

    /// <summary>True when an OAuth access or refresh token is present.</summary>
    public bool HasOAuthToken =>
        !string.IsNullOrEmpty(AccessToken) || !string.IsNullOrEmpty(RefreshToken);

    /// <summary>True when a full resource address (for example an ICS feed URL) is present.</summary>
    public bool HasResource => !string.IsNullOrEmpty(Resource);

    /// <summary>Redacted representation; it never contains a token, password or resource address.</summary>
    public override string ToString() =>
        $"ProviderSecret {{ AccessToken = {Redact(AccessToken)}, RefreshToken = {Redact(RefreshToken)}, " +
        $"Password = {Redact(Password)}, Resource = {Redact(Resource)}, " +
        $"ExpiresAt = {ExpiresAt?.ToString("O") ?? "(none)"} }}";

    private static string Redact(string? value) => string.IsNullOrEmpty(value) ? "(none)" : "(redacted)";
}
