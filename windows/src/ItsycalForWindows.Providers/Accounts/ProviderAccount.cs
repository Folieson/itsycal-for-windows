using System.Text.Json.Serialization;

namespace ItsycalForWindows.Providers;

/// <summary>
/// One configured calendar account. This is the non-secret half of a provider configuration:
/// credentials, tokens and token-bearing feed addresses live in
/// <see cref="IProviderSecretStore"/> and are never stored here.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Id"/> is the stable key used by the account store, the secret store, the sync state
/// and the cache. It must survive display-name and address edits; generate it once with
/// <see cref="NewId"/> and never reuse it. <see cref="RemoteId"/> is display-safe provider
/// metadata (a calendar id or, for an ICS subscription, only <c>scheme://host</c>): the full feed
/// address, which can embed a token, is kept in <see cref="ProviderSecret.Resource"/>.
/// </para>
/// <para>
/// The type intentionally has no token, password or feed-address property. A record's generated
/// <c>ToString</c> and the accounts JSON can therefore never expose secret material; the explicit
/// <see cref="ToString"/> below is a second guard for logs and omits the display name, sign-in
/// name and all URL paths.
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
    /// It is not written by <see cref="ToString"/>.
    /// </summary>
    public string? AccountName { get; init; }

    /// <summary>
    /// Display-safe provider-specific identifier: a calendar id, a CalDAV collection URL or, for
    /// an ICS subscription, only <c>scheme://host[:port]</c> (or <see cref="IcsFeedAddress.LocalFileRemoteId"/>
    /// for a local file). The complete ICS feed address, including any path token, is stored in
    /// <see cref="ProviderSecret.Resource"/> and must never be persisted here.
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

        if (kind == ProviderKind.IcsSubscription &&
            !string.IsNullOrWhiteSpace(remoteId) &&
            !IcsFeedAddress.IsSafeRemoteId(remoteId))
        {
            throw new ArgumentException(
                "An ICS account remote id must be the display-safe scheme and host only. " +
                "Use CreateIcsSubscription to keep the full feed address as a provider secret.",
                nameof(remoteId));
        }

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

    /// <summary>
    /// Creates an ICS subscription account whose <see cref="RemoteId"/> is the display-safe origin
    /// and whose full feed address is returned separately as a secret ready for
    /// <see cref="IProviderSecretStore.Save"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The address is not an absolute HTTPS URL or local file.</exception>
    public static (ProviderAccount Account, ProviderSecret Secret) CreateIcsSubscription(
        string displayName,
        string feedAddress,
        string? id = null,
        bool enabled = true,
        DateTimeOffset? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feedAddress);

        var account = Create(
            ProviderKind.IcsSubscription,
            displayName,
            remoteId: IcsFeedAddress.ToRemoteId(feedAddress),
            enabled: enabled,
            id: id,
            createdAt: createdAt);

        return (account, IcsFeedAddress.CreateSecret(feedAddress));
    }

    /// <summary>Returns a copy with a new <see cref="LastSyncAt"/> value.</summary>
    public ProviderAccount WithLastSync(DateTimeOffset lastSyncAt) =>
        this with { LastSyncAt = lastSyncAt.ToUniversalTime() };

    /// <summary>
    /// Safe log representation. It names only the stable id, kind, enabled flag and display-safe
    /// host; the display name, sign-in name and every URL path/query are omitted so a log line
    /// cannot carry an email address or a feed token.
    /// </summary>
    public override string ToString() =>
        $"ProviderAccount {{ Id = {Id}, Kind = {Kind}, Enabled = {Enabled}, Host = {SafeHost} }}";

    private string SafeHost
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RemoteId))
            {
                return "(none)";
            }

            if (string.Equals(RemoteId.Trim(), IcsFeedAddress.LocalFileRemoteId, StringComparison.OrdinalIgnoreCase))
            {
                return "(local file)";
            }

            return Uri.TryCreate(RemoteId, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? uri.Host
                : "(not a URL)";
        }
    }
}
