namespace ItsycalForWindows.Providers;

/// <summary>
/// Creates the provider implementation for one <see cref="ProviderKind"/>.
/// </summary>
/// <remarks>
/// Factories exist so the registry can be composed without a service locator and without
/// constructing providers that need a live <see cref="HttpClient"/> in tests. <see cref="Create"/>
/// is called once per registry; the returned instance must be thread-safe because one provider can
/// serve several accounts.
/// </remarks>
public interface IProviderFactory
{
    /// <summary>The kind this factory serves.</summary>
    ProviderKind Kind { get; }

    /// <summary>Creates the provider instance. Must not return null or a provider of another kind.</summary>
    ICalendarProvider Create();
}

/// <summary>Simple factory over a delegate, used by <see cref="ProviderCatalog"/> and tests.</summary>
public sealed class DelegateProviderFactory : IProviderFactory
{
    private readonly Func<ICalendarProvider> _create;

    /// <summary>Creates a factory for <paramref name="kind"/>.</summary>
    public DelegateProviderFactory(ProviderKind kind, Func<ICalendarProvider> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _create = create;
        Kind = kind;
    }

    /// <inheritdoc />
    public ProviderKind Kind { get; }

    /// <inheritdoc />
    public ICalendarProvider Create() => _create();
}
