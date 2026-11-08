namespace ItsycalForWindows.Providers;

/// <summary>
/// Yandex Calendar skeleton. The Yandex service is reached through CalDAV, but neither the
/// discovery nor the app-password transport exists in this slice.
/// </summary>
/// <remarks>
/// TODO(provider-next): implement CalDAV discovery and app-password authentication for Yandex,
/// storing the password only through <see cref="IProviderSecretStore"/>; reuse the generic CalDAV
/// transport once it exists. Until then no network request is made.
/// </remarks>
public sealed class YandexCalDavProvider : NotImplementedCalendarProvider
{
    /// <inheritdoc />
    public override ProviderKind Kind => ProviderKind.YandexCalDav;

    /// <inheritdoc />
    public override string DisplayName => "Yandex Calendar";
}
