namespace ItsycalForWindows.Providers;

/// <summary>
/// One provider call: the account, the resolved secret, the requested range and the refresh mode.
/// </summary>
/// <remarks>
/// <para>
/// The request object is the secret-delivery boundary. <see cref="ProviderSyncService"/> resolves
/// the account's <see cref="ProviderSecret"/> from <see cref="IProviderSecretStore"/> and passes
/// it here; a provider never reads the store itself and must never echo the secret, the full
/// remote address or the range contents into a log, status message or <c>ToString</c>.
/// </para>
/// <para>
/// A probe carries no range and must not perform network I/O. A fetch always carries a range;
/// <see cref="ForceRefresh"/> asks the provider to ignore cached validators (for example the ICS
/// ETag/If-Modified-Since pair) for this call.
/// </para>
/// </remarks>
public sealed record ProviderRequest
{
    private ProviderRequest()
    {
    }

    /// <summary>The account being probed or fetched.</summary>
    public required ProviderAccount Account { get; init; }

    /// <summary>
    /// Resolved secret for the account, or <c>null</c> when the store has none. Secret material;
    /// never log or serialize it.
    /// </summary>
    public ProviderSecret? Secret { get; init; }

    /// <summary>Range to fetch; empty/ignored for a probe.</summary>
    public ProviderEventRange Range { get; init; }

    /// <summary>True when the provider must bypass its own conditional-request cache.</summary>
    public bool ForceRefresh { get; init; }

    /// <summary>Creates a probe request. No network I/O may happen for this request.</summary>
    public static ProviderRequest Probe(ProviderAccount account, ProviderSecret? secret = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new ProviderRequest { Account = account, Secret = secret };
    }

    /// <summary>Creates a fetch request for one range.</summary>
    public static ProviderRequest Fetch(
        ProviderAccount account,
        ProviderEventRange range,
        ProviderSecret? secret = null,
        bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new ProviderRequest
        {
            Account = account,
            Range = range,
            Secret = secret,
            ForceRefresh = forceRefresh,
        };
    }

    /// <summary>Safe representation: the secret is shown only as present or absent.</summary>
    public override string ToString() =>
        $"ProviderRequest {{ Account = {Account}, Range = {Range}, ForceRefresh = {ForceRefresh}, " +
        $"Secret = {(Secret is null ? "(none)" : "(present, redacted)")} }}";
}
