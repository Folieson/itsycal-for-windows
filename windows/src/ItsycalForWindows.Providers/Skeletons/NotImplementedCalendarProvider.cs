namespace ItsycalForWindows.Providers;

/// <summary>
/// Base class for the deliberately unimplemented OAuth/CalDAV providers in this slice.
/// </summary>
/// <remarks>
/// The skeletons exist so that a configured account of any supported kind resolves to a provider
/// with an explicit <see cref="ProviderState.NotConfigured"/> or
/// <see cref="ProviderState.NotImplemented"/> answer instead of returning fabricated events or
/// throwing. <see cref="GetEventsAsync"/> never performs I/O and always returns an empty,
/// non-ready result. The real OAuth (PKCE, loopback redirect, DPAPI token refresh) and CalDAV
/// transports are the next slice; see spec 0006 and ADR 0002.
/// </remarks>
public abstract class NotImplementedCalendarProvider : ICalendarProvider
{
    /// <inheritdoc />
    public abstract ProviderKind Kind { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <summary>Short setup hint shown by the future settings surface.</summary>
    protected virtual string SetupHint => $"{DisplayName} is not configured yet.";

    /// <summary>Message used once an address exists but the transport is still absent.</summary>
    protected virtual string NotImplementedMessage =>
        $"{DisplayName} sign-in and event fetching are not implemented in this build.";

    /// <inheritdoc />
    public Task<ProviderStatus> ProbeAsync(
        ProviderAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        cancellationToken.ThrowIfCancellationRequested();

        var configured = !string.IsNullOrWhiteSpace(account.RemoteId) ||
            !string.IsNullOrWhiteSpace(account.AccountName);

        return Task.FromResult(configured
            ? ProviderStatus.NotImplemented(NotImplementedMessage)
            : ProviderStatus.NotConfigured(SetupHint));
    }

    /// <inheritdoc />
    public Task<ProviderEventResult> GetEventsAsync(
        ProviderAccount account,
        ProviderEventRange range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(ProviderEventResult.Empty(ProviderStatus.NotImplemented(NotImplementedMessage)));
    }
}
