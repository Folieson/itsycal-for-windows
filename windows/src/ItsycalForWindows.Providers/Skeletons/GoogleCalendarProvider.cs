namespace ItsycalForWindows.Providers;

/// <summary>
/// Google Calendar skeleton. OAuth 2.0 with PKCE, the loopback redirect and the token refresh are
/// deliberately not implemented yet; this class only reports the explicit state.
/// </summary>
/// <remarks>
/// TODO(provider-next): add the installed-app OAuth flow, store the refresh token through
/// <see cref="IProviderSecretStore"/> and call the Google Calendar events endpoint for the visible
/// range. Until then no network request is made.
/// </remarks>
public sealed class GoogleCalendarProvider : NotImplementedCalendarProvider
{
    /// <inheritdoc />
    public override ProviderKind Kind => ProviderKind.Google;

    /// <inheritdoc />
    public override string DisplayName => "Google Calendar";
}
