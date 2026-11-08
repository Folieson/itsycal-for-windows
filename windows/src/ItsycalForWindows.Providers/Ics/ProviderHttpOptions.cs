namespace ItsycalForWindows.Providers;

/// <summary>
/// Network and size limits for one ICS subscription fetch.
/// </summary>
/// <remarks>
/// The defaults match the local <see cref="ItsycalForWindows.Services.IcsEventSource"/> cap of
/// 2 MiB and add a strict redirect and timeout policy. A caller may lower them (tests do), but
/// <see cref="Validate"/> refuses nonsensical values so a bad configuration cannot disable the
/// limits entirely.
/// </remarks>
public sealed record ProviderHttpOptions
{
    /// <summary>Default per-request timeout.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Default maximum feed size (2 MiB, the same cap as the local ICS folder).</summary>
    public const int DefaultMaxResponseBytes = 2 * 1024 * 1024;

    /// <summary>Default maximum number of followed redirects.</summary>
    public const int DefaultMaxRedirects = 5;

    /// <summary>Total attempt timeout, including response-body reads.</summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>Maximum response body size in bytes.</summary>
    public int MaxResponseBytes { get; init; } = DefaultMaxResponseBytes;

    /// <summary>Maximum number of HTTP redirects to follow (0 disallows redirects).</summary>
    public int MaxRedirects { get; init; } = DefaultMaxRedirects;

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

        if (MaxRedirects is < 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRedirects), MaxRedirects, "At most 20 redirects are allowed.");
        }

        if (string.IsNullOrWhiteSpace(UserAgent))
        {
            throw new ArgumentException("A user agent is required.", nameof(UserAgent));
        }

        return this;
    }
}
