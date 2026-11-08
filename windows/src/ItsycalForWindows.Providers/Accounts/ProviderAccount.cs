using System.Text.Json.Serialization;

namespace ItsycalForWindows.Providers;

/// <summary>
/// One configured calendar account. This is the non-secret half of a provider configuration:
/// credentials and tokens live in <see cref="IProviderSecretStore"/> and are never stored here.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Id"/> is the stable key used by the account store, the secret store, the sync state
/// and the cache. It must survive display-name and address edits; generate it once with
/// <see cref="NewId"/> and never reuse it. <see cref="RemoteId"/> is the provider-specific address
/// (calendar id, subscription URL or local <c>.ics</c> path), not a credential.
/// </para>
/// <para>
/// The type intentionally has no token, password or authorization-header property. A record's
/// generated <c>ToString</c> and the accounts JSON can therefore never expose secret material; the
/// explicit <see cref="ToString"/> below is a second guard for logs.
/// </para>
/// </remarks>
public sealed record ProviderAccount
{
    /// <summary>Stable account id; conventionally a lower-case GUID without dashes.</summary>
    [JsonRequired]
    public required string Id { get; init; }

    /// <summary>Provider product that owns the account.</summary>
    [JsonRequired]
    public required ProviderKind Kind { get; init; }

    /// <summary>User-visible account name, for example <c>Work</c> or <c>Personal</c>.</summary>
    [JsonRequired]
    public required string DisplayName { get; init; }

    /// <summary>
    /// Optional sign-in name or calendar owner. It is display metadata only and never a password.
    /// </summary>
    public string? AccountName { get; init; }

    /// <summary>
    /// Provider-specific remote identifier: a calendar id, a CalDAV collection URL, an ICS
    /// subscription URL or a local <c>.ics</c> path. Must not contain credentials; URLs with
    /// embedded user-info are rejected by the ICS provider.
    /// </summary>
    public string? RemoteId { get; init; }

    /// <summary>False when the user switched the account off; disabled accounts are not synced.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Creation instant in UTC. Set by <see cref="DpapiAccountStore.Add"/> when omitted.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last successful sync instant in UTC, or <c>null</c> when never synced.</summary>
    public DateTimeOffset? LastSyncAt { get; init; }

    /// <summary>Creates a new stable account id (32 hex characters, no dashes).</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Creates a validated account with a fresh id and creation instant. Use this from settings
    /// and tests instead of hand-writing the record so the stable key is never forgotten.
    /// </summary>
    public static ProviderAccount Create(
        ProviderKind kind,
        string displayName,
        string? accountName = null,
        string? remoteId = null,
        bool enabled = true,
        string? id = null,
        DateTimeOffset? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new ProviderAccount
        {
            Id = string.IsNullOrWhiteSpace(id) ? NewId() : id.Trim(),
            Kind = kind,
            DisplayName = displayName.Trim(),
            AccountName = string.IsNullOrWhiteSpace(accountName) ? null : accountName.Trim(),
            RemoteId = string.IsNullOrWhiteSpace(remoteId) ? null : remoteId.Trim(),
            Enabled = enabled,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };
    }

    /// <summary>Returns a copy with a new <see cref="LastSyncAt"/> value.</summary>
    public ProviderAccount WithLastSync(DateTimeOffset lastSyncAt) =>
        this with { LastSyncAt = lastSyncAt.ToUniversalTime() };

    /// <summary>
    /// Safe log representation. It names only the non-secret fields; there are no secret fields to
    /// redact, which is the point of keeping credentials in <see cref="ProviderSecret"/>. An
    /// http(s) <see cref="RemoteId"/> is reduced to scheme/host/path so a feed token in the query
    /// string cannot end up in a log line.
    /// </summary>
    public override string ToString() =>
        $"ProviderAccount {{ Id = {Id}, Kind = {Kind}, DisplayName = {DisplayName}, " +
        $"AccountName = {AccountName ?? "(none)"}, RemoteId = {SafeRemoteId}, " +
        $"Enabled = {Enabled}, CreatedAt = {CreatedAt:O}, LastSyncAt = {LastSyncAt?.ToString("O") ?? "(never)"} }}";

    private string SafeRemoteId
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RemoteId))
            {
                return "(none)";
            }

            return Uri.TryCreate(RemoteId, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}"
                : RemoteId;
        }
    }
}
