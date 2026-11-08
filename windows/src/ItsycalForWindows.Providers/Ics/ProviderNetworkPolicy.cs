using System.Net;
using System.Net.Sockets;

namespace ItsycalForWindows.Providers;

/// <summary>Resolves a provider host to its addresses. Overridable so tests stay offline.</summary>
/// <param name="host">DNS host name from the validated URL.</param>
/// <param name="cancellationToken">Caller or budget cancellation token.</param>
public delegate Task<IReadOnlyList<IPAddress>> ProviderHostResolver(
    string host,
    CancellationToken cancellationToken);

/// <summary>Outcome of one network-policy check for a provider URL.</summary>
/// <param name="IsAllowed">True when the URL may be requested.</param>
/// <param name="State">Provider state to report when the URL is rejected.</param>
/// <param name="Message">Safe message; never contains the URL, host or credentials.</param>
public readonly record struct ProviderUrlValidation(bool IsAllowed, ProviderState State, string Message)
{
    /// <summary>An allowed URL.</summary>
    public static ProviderUrlValidation Allowed { get; } = new(true, ProviderState.Ready, string.Empty);

    /// <summary>A rejected URL with an explicit non-ready state.</summary>
    public static ProviderUrlValidation Reject(ProviderState state, string message) => new(false, state, message);
}

/// <summary>
/// Transport policy for provider URLs: HTTPS only, no embedded credentials, redirects validated per
/// hop, and private/loopback destinations allowed only with an explicit self-hosted opt-in.
/// </summary>
/// <remarks>
/// <para>
/// The policy follows ADR 0005 §4.5/§4.6: plain HTTP is rejected, URL user-info is rejected,
/// link-local, cloud-metadata and other non-routable ranges are always blocked (including an IPv4
/// link-local literal reached through an IPv6-mapped address), and loopback/private/LAN hosts need
/// <see cref="ProviderHttpOptions.AllowPrivateHosts"/>. When that opt-in is enabled the caller is
/// responsible for warning the user, because a user-chosen LAN service is outside the app's trust
/// boundary.
/// </para>
/// <para>
/// <see cref="ValidateSyntactic"/> performs no DNS lookup and is what a probe uses, so a probe can
/// stay offline. <see cref="ValidateAsync"/> resolves the host and is used for every redirect hop
/// of an actual fetch.
/// </para>
/// </remarks>
public static class ProviderNetworkPolicy
{
    private static readonly IPAddress[] ExplicitlyBlockedAddresses =
    [
        IPAddress.Parse("169.254.169.254"), // AWS/GCP/Azure instance metadata (IPv4 link-local)
        IPAddress.Parse("100.100.100.200"), // Alibaba Cloud metadata
        IPAddress.Parse("192.0.0.192"), // Oracle Cloud metadata
        IPAddress.Parse("fd00:ec2::254"), // AWS instance metadata (IPv6 ULA)
    ];

    private static readonly string[] BlockedMetadataHosts =
    [
        "metadata",
        "metadata.google.internal",
        "metadata.goog",
        "instance-data",
        "instance-data.ec2.internal",
    ];

    /// <summary>True when the scheme is the HTTPS-only policy.</summary>
    public static bool IsSchemeAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.Scheme == Uri.UriSchemeHttps;
    }

    /// <summary>
    /// True for addresses that are never allowed, even with the private-host opt-in: link-local
    /// (IPv4/IPv6), cloud metadata, unspecified, broadcast, multicast and other non-routable
    /// values.
    /// </summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (ExplicitlyBlockedAddresses.Any(blocked => blocked.Equals(address)))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            return address.Equals(IPAddress.Any) ||
                   address.Equals(IPAddress.Broadcast) ||
                   bytes[0] >= 224 || // multicast and reserved
                   (bytes[0] == 169 && bytes[1] == 254); // link-local
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.Equals(IPAddress.IPv6Any) ||
                   address.IsIPv6LinkLocal ||
                   address.IsIPv6Multicast;
        }

        return true;
    }

    /// <summary>
    /// True for loopback, RFC 1918, CGNAT, IPv6 unique-local and other non-public destinations.
    /// These are allowed only with <see cref="ProviderHttpOptions.AllowPrivateHosts"/>.
    /// </summary>
    public static bool IsPrivateOrLoopbackAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 127) ||
                   (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.Equals(IPAddress.IPv6Loopback) || address.IsIPv6UniqueLocal;
        }

        return false;
    }

    /// <summary>
    /// Checks scheme, credentials and literal addresses without a DNS lookup. A host name that is
    /// not an IP literal is provisionally allowed; <see cref="ValidateAsync"/> resolves it before
    /// the request.
    /// </summary>
    public static ProviderUrlValidation ValidateSyntactic(Uri uri, ProviderHttpOptions options)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(options);

        if (!IsSchemeAllowed(uri))
        {
            return ProviderUrlValidation.Reject(
                ProviderState.InvalidResponse,
                "Only HTTPS calendar subscriptions are supported.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return ProviderUrlValidation.Reject(
                ProviderState.InvalidResponse,
                "The subscription address must not contain embedded credentials.");
        }

        var host = uri.DnsSafeHost.TrimEnd('.');
        if (string.IsNullOrEmpty(host))
        {
            return ProviderUrlValidation.Reject(
                ProviderState.InvalidResponse,
                "The subscription address has no host.");
        }

        if (BlockedMetadataHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            return ProviderUrlValidation.Reject(
                ProviderState.InvalidResponse,
                "The subscription address targets a cloud metadata service.");
        }

        if (IPAddress.TryParse(host, out var literal))
        {
            return ValidateAddress(literal, options);
        }

        if (IsLocalHostName(host) && !options.AllowPrivateHosts)
        {
            return ProviderUrlValidation.Reject(
                ProviderState.InvalidResponse,
                "The subscription address is a loopback host. Self-hosted feeds must be enabled explicitly.");
        }

        return ProviderUrlValidation.Allowed;
    }

    /// <summary>
    /// Full check used before every request and redirect hop: syntactic policy plus DNS resolution
    /// against the blocked/private ranges. Never includes the URL or host in the returned message.
    /// </summary>
    public static async Task<ProviderUrlValidation> ValidateAsync(
        Uri uri,
        ProviderHttpOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(options);

        var syntactic = ValidateSyntactic(uri, options);
        if (!syntactic.IsAllowed)
        {
            return syntactic;
        }

        var host = uri.DnsSafeHost.TrimEnd('.');
        if (IPAddress.TryParse(host, out _) || IsLocalHostName(host))
        {
            return syntactic;
        }

        IReadOnlyList<IPAddress> addresses;
        try
        {
            addresses = options.HostResolver is { } resolver
                ? await resolver(host, cancellationToken).ConfigureAwait(false)
                : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A DNS failure is an explicit unavailable state, not a crash.
        catch (Exception)
        {
            return ProviderUrlValidation.Reject(
                ProviderState.Unavailable,
                "The subscription host could not be resolved.");
        }
#pragma warning restore CA1031

        if (addresses is null || addresses.Count == 0)
        {
            return ProviderUrlValidation.Reject(
                ProviderState.Unavailable,
                "The subscription host could not be resolved.");
        }

        foreach (var address in addresses)
        {
            var addressValidation = ValidateAddress(address, options);
            if (!addressValidation.IsAllowed)
            {
                return addressValidation;
            }
        }

        return ProviderUrlValidation.Allowed;
    }

    private static ProviderUrlValidation ValidateAddress(IPAddress address, ProviderHttpOptions options)
    {
        if (IsBlockedAddress(address))
        {
            return ProviderUrlValidation.Reject(
                ProviderState.InvalidResponse,
                "The subscription address is in a blocked network range.");
        }

        if (IsPrivateOrLoopbackAddress(address) && !options.AllowPrivateHosts)
        {
            return ProviderUrlValidation.Reject(
                ProviderState.InvalidResponse,
                "The subscription address is a private or loopback address. " +
                "Self-hosted feeds must be enabled explicitly.");
        }

        return ProviderUrlValidation.Allowed;
    }

    private static bool IsLocalHostName(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
}
