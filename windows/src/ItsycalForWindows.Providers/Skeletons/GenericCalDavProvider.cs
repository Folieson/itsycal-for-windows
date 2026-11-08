namespace ItsycalForWindows.Providers;

/// <summary>
/// Generic CalDAV skeleton for iCloud, Fastmail, Nextcloud and similar servers.
/// </summary>
/// <remarks>
/// TODO(provider-next): implement principal/calendar discovery, PROPFIND/REPORT range queries and
/// app-password authentication, storing the password only through
/// <see cref="IProviderSecretStore"/>. Until then no network request is made.
/// </remarks>
public sealed class GenericCalDavProvider : NotImplementedCalendarProvider
{
    /// <inheritdoc />
    public override ProviderKind Kind => ProviderKind.GenericCalDav;

    /// <inheritdoc />
    public override string DisplayName => "CalDAV";
}
