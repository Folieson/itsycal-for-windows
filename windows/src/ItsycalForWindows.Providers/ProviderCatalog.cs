namespace ItsycalForWindows.Providers;

/// <summary>
/// Default provider composition for the shipping build: the Google Calendar provider, the three
/// CalDAV/Graph skeletons plus the working read-only ICS subscription provider.
/// </summary>
/// <remarks>
/// The skeletons are registered on purpose. An account of a kind that is not registered would look
/// like a programming error; returning an explicit "not implemented" status from a registered
/// skeleton keeps the settings surface honest and lets the OAuth work land later without changing
/// the sync service. Google is the first real OAuth provider; it is created without OAuth options
/// (and reports <see cref="ProviderState.NotConfigured"/>) until the composition root supplies a
/// PO-owned client id and an <see cref="IOAuthTokenStore"/>. The handler is owned by the caller;
/// providers own only the <see cref="System.Net.Http.HttpClient"/> they wrap around it.
/// </remarks>
public static class ProviderCatalog
{
    /// <summary>Creates the default factories. <paramref name="handler"/> is owned by the caller.</summary>
    /// <param name="handler">Shared HTTP handler; the caller owns and disposes it.</param>
    /// <param name="httpOptions">Limits for the ICS provider.</param>
    /// <param name="googleOptions">Google OAuth configuration; <c>null</c> leaves Google unconfigured.</param>
    /// <param name="googleTokenStore">Token persistence for Google; optional for an unconfigured build.</param>
    public static IReadOnlyList<IProviderFactory> CreateFactories(
        HttpMessageHandler handler,
        ProviderHttpOptions? httpOptions = null,
        GoogleOAuthOptions? googleOptions = null,
        IOAuthTokenStore? googleTokenStore = null)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var options = httpOptions ?? new ProviderHttpOptions();
        return
        [
            new DelegateProviderFactory(
                ProviderKind.Google,
                googleOptions is null
                    ? static () => new GoogleCalendarProvider()
                    : () => new GoogleCalendarProvider(handler, googleOptions, googleTokenStore)),
            new DelegateProviderFactory(ProviderKind.MicrosoftGraph, static () => new MicrosoftGraphProvider()),
            new DelegateProviderFactory(ProviderKind.YandexCalDav, static () => new YandexCalDavProvider()),
            new DelegateProviderFactory(ProviderKind.GenericCalDav, static () => new GenericCalDavProvider()),
            new DelegateProviderFactory(
                ProviderKind.IcsSubscription,
                () => new IcsSubscriptionProvider(handler, options)),
        ];
    }

    /// <summary>Creates a registry with every provider this build ships.</summary>
    public static ProviderRegistry CreateRegistry(
        HttpMessageHandler handler,
        ProviderHttpOptions? httpOptions = null,
        GoogleOAuthOptions? googleOptions = null,
        IOAuthTokenStore? googleTokenStore = null) =>
        new(CreateFactories(handler, httpOptions, googleOptions, googleTokenStore));
}
