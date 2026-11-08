namespace ItsycalForWindows.Providers;

/// <summary>Outcome classification for one account in a provider refresh.</summary>
public enum ProviderSyncStatus
{
    /// <summary>The provider returned a ready result and the events were refreshed.</summary>
    Succeeded,

    /// <summary>The events came from the local cache; no provider request was made.</summary>
    Cached,

    /// <summary>The account is disabled or the range is empty; nothing was fetched.</summary>
    Skipped,

    /// <summary>Another sync for the same account is already running; this call was not queued.</summary>
    AlreadyRunning,

    /// <summary>The provider needs configuration/credentials before it can be synced.</summary>
    NotConfigured,

    /// <summary>The provider is a deliberate skeleton in this build.</summary>
    NotImplemented,

    /// <summary>The provider rejected the credentials.</summary>
    Unauthorized,

    /// <summary>The provider failed (unreachable, timeout, malformed response or unexpected error).</summary>
    Failed,
}
