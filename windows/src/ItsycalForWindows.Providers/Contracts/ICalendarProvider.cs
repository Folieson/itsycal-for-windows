namespace ItsycalForWindows.Providers;

/// <summary>
/// Read-only calendar provider boundary. Every remote or subscription source implements this
/// contract; the Core <see cref="ItsycalForWindows.Services.IEventSource"/> composite remains the
/// UI-facing abstraction and receives the normalized Core events through
/// <see cref="ProviderSyncService"/> (or the thin
/// <see cref="ProviderEventSource"/> adapter).
/// </summary>
/// <remarks>
/// <para>
/// Implementations must be safe to call from a background thread, must not do network I/O in
/// <see cref="ProbeAsync"/> (a probe only reports configuration/capabilities), and must not throw
/// for expected failures: they return <see cref="ProviderStatus"/> /
/// <see cref="ProviderEventResult"/> with an explicit non-ready state. Cancellation is honored and
/// propagates as <see cref="OperationCanceledException"/>; a provider must never convert caller
/// cancellation into a failure status.
/// </para>
/// <para>
/// Secrets arrive through <see cref="ProviderRequest.Secret"/>; a provider must never log event
/// content, tokens, passwords or full remote URLs, and must not write credentials to disk.
/// </para>
/// </remarks>
public interface ICalendarProvider
{
    /// <summary>The provider kind this implementation serves.</summary>
    ProviderKind Kind { get; }

    /// <summary>User-facing product name, for example <c>Google Calendar</c>.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Reports whether the account is configured, which capabilities exist and which explicit state
    /// applies. This method must not perform a network request in the current slice.
    /// </summary>
    Task<ProviderStatus> ProbeAsync(ProviderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches normalized events overlapping <see cref="ProviderRequest.Range"/>. A successful
    /// fetch with no events returns a ready status and an empty list; an expected failure returns
    /// the matching non-ready status instead of throwing.
    /// </summary>
    Task<ProviderEventResult> GetEventsAsync(
        ProviderRequest request,
        CancellationToken cancellationToken = default);
}
