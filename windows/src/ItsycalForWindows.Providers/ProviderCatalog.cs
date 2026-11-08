namespace ItsycalForWindows.Providers;

/// <summary>
/// Default provider composition for the shipping build: the four skeleton providers plus the
/// working read-only ICS subscription provider.
/// </summary>
/// <remarks>
/// The skeleton providers are registered on purpose. An account of a kind that is not registered
/// would look like a programming error; returning an explicit "not implemented" status from a
/// registered skeleton keeps the settings surface honest and lets the OAuth work land later without
/// changing the sync service.
/// </remarks>
public static class ProviderCatalog
{
    /// <summary>Creates the default factories. <paramref name="httpClient"/> is owned by the caller.</summary>
    public static IReadOnlyList<IProviderFactory> CreateFactories(
        HttpClient httpClient,
        ProviderHttpOptions? httpOptions = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        var options = httpOptions ?? new ProviderHttpOptions();
        return
        [
            new DelegateProviderFactory(ProviderKind.Google, static () => new GoogleCalendarProvider()),
            new DelegateProviderFactory(ProviderKind.MicrosoftGraph, static () => new MicrosoftGraphProvider()),
            new DelegateProviderFactory(ProviderKind.YandexCalDav, static () => new YandexCalDavProvider()),
            new DelegateProviderFactory(ProviderKind.GenericCalDav, static () => new GenericCalDavProvider()),
            new DelegateProviderFactory(
                ProviderKind.IcsSubscription,
                () => new IcsSubscriptionProvider(httpClient, options)),
        ];
    }

    /// <summary>Creates a registry with every provider this build ships.</summary>
    public static ProviderRegistry CreateRegistry(
        HttpClient httpClient,
        ProviderHttpOptions? httpOptions = null) =>
        new(CreateFactories(httpClient, httpOptions));
}
