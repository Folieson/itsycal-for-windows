namespace ItsycalForWindows.Providers;

/// <summary>
/// Explicit capability/health answer from <see cref="ICalendarProvider.ProbeAsync"/>.
/// </summary>
/// <remarks>
/// <see cref="Message"/> is user-facing and safe to show or log: it must never contain a token,
/// password, authorization header or full feed URL. An empty event list with a
/// <see cref="ProviderState.Ready"/> status means "successfully fetched, no events in range"; it is
/// not an error.
/// </remarks>
public sealed record ProviderStatus
{
    /// <summary>Creates a status value.</summary>
    public ProviderStatus(
        ProviderState state,
        ProviderCapabilities capabilities = ProviderCapabilities.None,
        string? message = null)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown provider state.");
        }

        State = state;
        Capabilities = capabilities;
        Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
    }

    /// <summary>Explicit state of the provider.</summary>
    public ProviderState State { get; }

    /// <summary>Features this provider can offer in the current build.</summary>
    public ProviderCapabilities Capabilities { get; }

    /// <summary>Optional safe, user-facing explanation; never a secret or full remote URL.</summary>
    public string? Message { get; }

    /// <summary>True only for <see cref="ProviderState.Ready"/>.</summary>
    public bool IsReady => State == ProviderState.Ready;

    /// <summary>True when the provider is implemented and configured enough to fetch.</summary>
    public bool IsConfigured => State is not (ProviderState.NotConfigured or ProviderState.NotImplemented);

    /// <summary>Creates a ready status with the read-only capability by default.</summary>
    public static ProviderStatus Ready(
        ProviderCapabilities capabilities = ProviderCapabilities.ReadOnlyEvents,
        string? message = null) =>
        new(ProviderState.Ready, capabilities, message);

    /// <summary>Creates an explicit "the account needs setup" status.</summary>
    public static ProviderStatus NotConfigured(string? message = null) =>
        new(ProviderState.NotConfigured, ProviderCapabilities.None, message);

    /// <summary>Creates an explicit "not implemented in this build" status.</summary>
    public static ProviderStatus NotImplemented(string? message = null) =>
        new(ProviderState.NotImplemented, ProviderCapabilities.None, message);

    /// <summary>Creates an "authorization failed" status.</summary>
    public static ProviderStatus Unauthorized(string? message = null) =>
        new(ProviderState.Unauthorized, ProviderCapabilities.None, message);

    /// <summary>Creates an "unreachable/timed out" status.</summary>
    public static ProviderStatus Unavailable(string? message = null) =>
        new(ProviderState.Unavailable, ProviderCapabilities.None, message);

    /// <summary>Creates an "invalid response/feed" status.</summary>
    public static ProviderStatus InvalidResponse(string? message = null) =>
        new(ProviderState.InvalidResponse, ProviderCapabilities.None, message);

    /// <summary>Safe representation; carries no secret material.</summary>
    public override string ToString() =>
        $"ProviderStatus {{ State = {State}, Capabilities = {Capabilities}, Message = {Message ?? "(none)"} }}";
}
