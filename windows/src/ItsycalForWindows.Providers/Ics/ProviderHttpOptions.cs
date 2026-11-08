namespace ItsycalForWindows.Providers;

/// <summary>
/// Network and size limits for one ICS subscription fetch.
/// </summary>
/// <remarks>
/// <para>
/// The defaults match the local <see cref="ItsycalForWindows.Services.IcsEventSource"/> cap of
/// 2 MiB and add the ADR 0005 transport policy: HTTPS only, at most three redirects, one total
/// timeout budget for the whole attempt (all hops plus the body read), and private/loopback hosts
/// rejected unless the caller explicitly opts into self-hosted feeds. A caller may lower the
/// limits (tests do), but <see cref="Validate"/> refuses values outside the policy so a bad
/// configuration cannot disable them.
/// </para>
/// <para>
/// <see cref="HostResolver"/> exists so tests can exercise the private/link-local/metadata policy
/// without real DNS. Production leaves it <c>null</c> and uses the OS resolver.
/// </para>
/// </remarks>
public sealed record ProviderHttpOptions
{
    /// <summary>Default total attempt timeout (redirects plus body read).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Default maximum feed size (2 MiB, the same cap as the local ICS folder).</summary>
    public const int DefaultMaxResponseBytes = 2 * 1024 * 1024;

    /// <summary>Default maximum number of followed redirects (ADR 0005 policy: at most three).</summary>
    public const int DefaultMaxRedirects = 3;

    /// <summary>Hard upper bound accepted by <see cref="Validate"/>.</summary>
    public const int MaxAllowedRedirects = 3;

    /// <summary>
    /// Total attempt timeout, including every redirect hop and the response-body read.
    /// </summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>Maximum response body size in bytes.</summary>
    public int MaxResponseBytes { get; init; } = DefaultMaxResponseBytes;

    /// <summary>Maximum number of HTTP redirects to follow (0 disallows redirects, at most 3).</summary>
    public int MaxRedirects { get; init; } = DefaultMaxRedirects;

    /// <summary>
    /// Explicit opt-in for self-hosted/private ICS feeds. When false (the default), loopback,
    /// RFC 1918, CGNAT and IPv6 unique-local destinations are rejected. Link-local and cloud
    /// metadata ranges are rejected even when this is true, and the scheme must stay HTTPS.
    /// </summary>
    public bool AllowPrivateHosts { get; init; }

    /// <summary>
    /// Optional DNS resolver used by the network policy. <c>null</c> uses the OS resolver;
    /// tests inject a deterministic resolver so they stay offline.
    /// </summary>
    public ProviderHostResolver? HostResolver { get; init; }

    /// <summary>User agent sent with subscription requests.</summary>
    public string UserAgent { get; init; } =
        "ItsycalForWindows/1.0 (+https://github.com/Folieson/itsycal-for-windows)";

    /// <summary>Validates the options and returns <c>this</c> for terse construction.</summary>
    public ProviderHttpOptions Validate()
    {
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(
                nameof(Timeout),
                Timeout,
                "The timeout must be greater than zero and at most five minutes.");
        }

        if (MaxResponseBytes < 1024 || MaxResponseBytes > 50 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxResponseBytes),
                MaxResponseBytes,
                "The response cap must be between 1 KiB and 50 MiB.");
        }

        if (MaxRedirects is < 0 or > MaxAllowedRedirects)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxRedirects),
                MaxRedirects,
                "At most three redirects are allowed.");
        }

        if (string.IsNullOrWhiteSpace(UserAgent))
        {
            throw new ArgumentException("A user agent is required.", nameof(UserAgent));
        }

        return this;
    }
}
