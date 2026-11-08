using ItsycalForWindows.Models;
using ItsycalForWindows.Services;

namespace ItsycalForWindows.Providers;

/// <summary>Aggregate result of syncing every enabled account.</summary>
public sealed record ProviderSyncResult
{
    /// <summary>Creates a result. Null outcome lists are treated as empty.</summary>
    public ProviderSyncResult(IReadOnlyList<ProviderSyncOutcome>? outcomes)
    {
        Outcomes = outcomes ?? [];
    }

    /// <summary>One outcome per account considered, in account-store order.</summary>
    public IReadOnlyList<ProviderSyncOutcome> Outcomes { get; }

    /// <summary>
    /// All events merged across accounts with the same de-duplication the calendar UI uses
    /// (subject case-insensitive, start, end), sorted by start.
    /// </summary>
    public IReadOnlyList<CalendarEvent> Events =>
        CompositeEventSource.Merge(Outcomes.Select(static outcome => outcome.Events));

    /// <summary>Number of accounts with fresh or cached events.</summary>
    public int SucceededCount => Outcomes.Count(static outcome => outcome.IsSuccess);

    /// <summary>Number of accounts that failed for a reason other than being disabled.</summary>
    public int FailedCount => Outcomes.Count(static outcome =>
        outcome.Status is ProviderSyncStatus.Failed or ProviderSyncStatus.Unauthorized);

    /// <summary>Number of disabled accounts skipped by the refresh.</summary>
    public int SkippedCount => Outcomes.Count(static outcome => outcome.Status == ProviderSyncStatus.Skipped);

    /// <summary>Safe representation: counts only, never event content.</summary>
    public override string ToString() =>
        $"ProviderSyncResult {{ Outcomes = {Outcomes.Count}, Succeeded = {SucceededCount}, " +
        $"Failed = {FailedCount}, Skipped = {SkippedCount}, EventCount = {Events.Count} }}";
}
