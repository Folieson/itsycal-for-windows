namespace ItsycalForWindows.Providers;

/// <summary>
/// Persistent, non-secret account registry used by the provider sync service and the settings
/// surface.
/// </summary>
/// <remarks>
/// Implementations must be safe for the small concurrent use of the settings page and a refresh,
/// including a second surface process: the shipped <see cref="DpapiAccountStore"/> serializes every
/// operation with an in-process lock and a cross-process named mutex. A missing, corrupt or
/// truncated store reads as an empty list instead of throwing; a write failure is reported to the
/// caller. The shipped store also moves an unreadable file aside before overwriting it, so a
/// corrupt store cannot silently destroy every account.
/// </remarks>
public interface IAccountStore
{
    /// <summary>Returns all accounts, ordered deterministically (creation time, then id).</summary>
    IReadOnlyList<ProviderAccount> List();

    /// <summary>Finds one account by its stable id, or <c>null</c> when it does not exist.</summary>
    ProviderAccount? Find(string accountId);

    /// <summary>
    /// Adds a new account. The store assigns <see cref="ProviderAccount.CreatedAt"/> when it is
    /// <c>default</c> and normalizes display metadata. Throws <see cref="ArgumentException"/> for
    /// an invalid id/kind or a remote id that would persist credentials/tokenized feed addresses,
    /// and <see cref="InvalidOperationException"/> for a duplicate id.
    /// </summary>
    ProviderAccount Add(ProviderAccount account);

    /// <summary>
    /// Removes an account and its secrets, secret first; false when the account does not exist.
    /// </summary>
    bool Remove(string accountId);

    /// <summary>Enables or disables an account; false when the account does not exist.</summary>
    bool SetEnabled(string accountId, bool enabled);

    /// <summary>
    /// Records a successful sync instant; false when the account does not exist. Called by
    /// <see cref="ProviderSyncService"/> after a provider returned events, never for a skipped,
    /// cached or failed attempt.
    /// </summary>
    bool MarkSynced(string accountId, DateTimeOffset lastSyncAt);
}
