namespace ItsycalForWindows.Providers;

/// <summary>
/// Immutable registry that resolves an <see cref="ICalendarProvider"/> by <see cref="ProviderKind"/>.
/// </summary>
/// <remarks>
/// The registry is deliberately small: it validates that every factory creates its declared kind,
/// rejects duplicate kinds and lets the sync service report an explicit failure for an account
/// whose kind is not registered in this build. It is not a service locator with mutable global
/// state; the caller owns the instance and its lifetime.
/// </remarks>
public sealed class ProviderRegistry
{
    private readonly IReadOnlyDictionary<ProviderKind, ICalendarProvider> _providers;

    /// <summary>Creates a registry from <paramref name="factories"/>; null entries are ignored.</summary>
    /// <exception cref="ArgumentException">A kind is duplicated or invalid.</exception>
    /// <exception cref="InvalidOperationException">A factory returned null or the wrong kind.</exception>
    public ProviderRegistry(IEnumerable<IProviderFactory>? factories)
    {
        var providers = new Dictionary<ProviderKind, ICalendarProvider>();
        if (factories is not null)
        {
            foreach (var factory in factories)
            {
                if (factory is null)
                {
                    continue;
                }

                if (!Enum.IsDefined(factory.Kind))
                {
                    throw new ArgumentException($"Unknown provider kind '{factory.Kind}'.", nameof(factories));
                }

                var provider = factory.Create()
                    ?? throw new InvalidOperationException($"The factory for {factory.Kind} returned null.");

                if (provider.Kind != factory.Kind)
                {
                    throw new InvalidOperationException(
                        $"The factory for {factory.Kind} returned a provider for {provider.Kind}.");
                }

                if (!providers.TryAdd(factory.Kind, provider))
                {
                    throw new ArgumentException(
                        $"A provider for {factory.Kind} is already registered.",
                        nameof(factories));
                }
            }
        }

        _providers = providers;
    }

    /// <summary>Registered kinds in enum order.</summary>
    public IReadOnlyCollection<ProviderKind> Kinds =>
        _providers.Keys.OrderBy(static kind => (int)kind).ToArray();

    /// <summary>True when a provider is registered for <paramref name="kind"/>.</summary>
    public bool Contains(ProviderKind kind) => _providers.ContainsKey(kind);

    /// <summary>Tries to resolve a provider by kind.</summary>
    public bool TryGet(ProviderKind kind, out ICalendarProvider? provider) =>
        _providers.TryGetValue(kind, out provider);

    /// <summary>Resolves a provider by kind or throws when it is not registered.</summary>
    public ICalendarProvider Get(ProviderKind kind) =>
        _providers.TryGetValue(kind, out var provider)
            ? provider
            : throw new KeyNotFoundException($"No provider is registered for {kind}.");
}
