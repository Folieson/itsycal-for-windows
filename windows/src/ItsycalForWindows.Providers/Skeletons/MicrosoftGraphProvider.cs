namespace ItsycalForWindows.Providers;

/// <summary>
/// Microsoft Graph skeleton for Outlook.com, Microsoft 365 and Exchange Online.
/// </summary>
/// <remarks>
/// TODO(provider-next): add delegated OAuth with the <c>Calendars.Read</c> scope, store the refresh
/// token through <see cref="IProviderSecretStore"/> and query <c>/me/calendarView</c> for the
/// visible range. On-prem Exchange Web Services remains a later, separate transport. Until then no
/// network request is made.
/// </remarks>
public sealed class MicrosoftGraphProvider : NotImplementedCalendarProvider
{
    /// <inheritdoc />
    public override ProviderKind Kind => ProviderKind.MicrosoftGraph;

    /// <inheritdoc />
    public override string DisplayName => "Microsoft 365 / Outlook";
}
