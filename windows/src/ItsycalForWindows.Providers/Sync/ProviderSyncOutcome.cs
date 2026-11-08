using ItsycalForWindows.Models;

namespace ItsycalForWindows.Providers;

/// <summary>Per-account result of a provider refresh.</summary>
public sealed record ProviderSyncOutcome
{
    /// <summary>Creates an outcome. Null event collections are treated as empty.</summary>
    public ProviderSyncOutcome(
        ProviderAccount account,
        ProviderSyncStatus status,
        ProviderStatus providerStatus,
        IReadOnlyList<CalendarEvent>? events = null,
        DateTimeOffset? lastSuccessAt = null,
        string? error = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(providerStatus);

        Account = account;
        Status = status;
        ProviderStatus = providerStatus;
        Events = events ?? [];
        LastSuccessAt = lastSuccessAt;
        Error = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
    }

    /// <summary>The account that was refreshed.</summary>
    public ProviderAccount Account { get; }

    /// <summary>Outcome classification.</summary>
    public ProviderSyncStatus Status { get; }

    /// <summary>Explicit provider status behind the outcome.</summary>
    public ProviderStatus ProviderStatus { get; }

    /// <summary>Events for the requested range (empty for failures/skips).</summary>
    public IReadOnlyList<CalendarEvent> Events { get; }

    /// <summary>Last successful sync instant known to the service, if any.</summary>
    public DateTimeOffset? LastSuccessAt { get; }

    /// <summary>Safe user-facing error text; never a token, password or full URL.</summary>
    public string? Error { get; }

    /// <summary>True when the outcome carries fresh or cached events.</summary>
    public bool IsSuccess => Status is ProviderSyncStatus.Succeeded or ProviderSyncStatus.Cached;

    /// <summary>Safe representation: event counts only, never event content or the resolved secret.</summary>
    public override string ToString() =>
        $"ProviderSyncOutcome {{ Account = {Account}, Status = {Status}, EventCount = {Events.Count}, " +
        $"LastSuccessAt = {LastSuccessAt?.ToString("O") ?? "(never)"}, Error = {Error ?? "(none)"} }}";
}
