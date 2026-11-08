namespace ItsycalForWindows.Providers;

/// <summary>
/// Result of <see cref="ICalendarProvider.GetEventsAsync"/>: normalized events plus the explicit
/// status of the fetch.
/// </summary>
/// <remarks>
/// A provider must not throw for an expected failure (missing credentials, HTTP error, malformed
/// feed): it returns a non-ready status with an empty event list. Cancellation is the exception and
/// propagates as <see cref="OperationCanceledException"/>. <see cref="NotModified"/> is true when
/// the remote answered "not modified" and the events came from the provider's own cached text.
/// </remarks>
public sealed record ProviderEventResult
{
    /// <summary>Creates a result. Null event collections are treated as empty.</summary>
    public ProviderEventResult(
        ProviderStatus status,
        IReadOnlyList<ProviderEvent>? events = null,
        bool notModified = false)
    {
        ArgumentNullException.ThrowIfNull(status);

        Status = status;
        Events = events ?? [];
        NotModified = notModified;
    }

    /// <summary>Status of this fetch.</summary>
    public ProviderStatus Status { get; }

    /// <summary>Normalized events, empty for an error or an empty range.</summary>
    public IReadOnlyList<ProviderEvent> Events { get; }

    /// <summary>True when the remote returned HTTP 304; the events came from cached text.</summary>
    public bool NotModified { get; }

    /// <summary>Projects the events to the Core model consumed by the calendar surfaces.</summary>
    public IReadOnlyList<ItsycalForWindows.Models.CalendarEvent> ToCalendarEvents() =>
        Events.Select(static providerEvent => providerEvent.Event).ToArray();

    /// <summary>Creates an error/empty result from a status.</summary>
    public static ProviderEventResult Empty(ProviderStatus status) => new(status);

    /// <summary>Creates a ready result from normalized events.</summary>
    public static ProviderEventResult Ready(
        IReadOnlyList<ProviderEvent>? events,
        bool notModified = false,
        ProviderCapabilities capabilities = ProviderCapabilities.ReadOnlyEvents) =>
        new(ProviderStatus.Ready(capabilities), events, notModified);

    /// <summary>Safe representation: event counts only, never event content.</summary>
    public override string ToString() =>
        $"ProviderEventResult {{ Status = {Status}, EventCount = {Events.Count}, NotModified = {NotModified} }}";
}
