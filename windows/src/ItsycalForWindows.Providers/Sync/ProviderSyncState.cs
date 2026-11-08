namespace ItsycalForWindows.Providers;

/// <summary>Last known sync state of one account, exposed for the settings/status surface.</summary>
public sealed record ProviderSyncState(
    string AccountId,
    ProviderStatus Status,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    string? LastError)
{
    /// <summary>True when the last attempt produced fresh or cached events.</summary>
    public bool IsSuccess => Status.IsReady;
}
