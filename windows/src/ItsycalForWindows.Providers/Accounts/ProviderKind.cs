namespace ItsycalForWindows.Providers;

/// <summary>
/// The remote calendar product a <see cref="ProviderAccount"/> belongs to.
/// </summary>
/// <remarks>
/// The values are persisted in the accounts JSON file, so existing names and numeric values must
/// not change. New providers append a new value; unknown values from a future build are rejected
/// when an account is added instead of being guessed.
/// </remarks>
public enum ProviderKind
{
    /// <summary>Google Calendar (Gmail accounts) through the Google Calendar API.</summary>
    Google = 0,

    /// <summary>Microsoft Graph: Outlook.com, Microsoft 365 and Exchange Online.</summary>
    MicrosoftGraph = 1,

    /// <summary>Yandex Calendar through its CalDAV endpoint.</summary>
    YandexCalDav = 2,

    /// <summary>Any standards-compliant CalDAV server (iCloud, Fastmail, Nextcloud, ...).</summary>
    GenericCalDav = 3,

    /// <summary>A read-only ICS feed over HTTP(S) or a local <c>.ics</c> file.</summary>
    IcsSubscription = 4,
}
