namespace ItsycalForWindows.Providers;

/// <summary>
/// Google Calendar OAuth and read-only API settings. No client id or secret is embedded in the
/// build: <see cref="ClientId"/> is supplied by the PO-owned Google Cloud desktop-app registration.
/// </summary>
/// <remarks>
/// <para>
/// The only requested scope is
/// <c>https://www.googleapis.com/auth/calendar.readonly</c> (ADR 0005 В§4.3); no Gmail, Drive,
/// contacts or userinfo scope is requested. The authorization request adds
/// <c>access_type=offline</c> so a refresh token is issued and <c>prompt=consent</c> so a repeat
/// consent returns one deterministically.
/// </para>
/// <para>
/// <see cref="CalendarId"/> is the single configured calendar for this slice; it defaults to
/// <c>primary</c> and the account's display-safe <see cref="ProviderAccount.RemoteId"/> overrides
/// it when set. Calendar selection UX is PRV-306 and is deliberately not implemented here.
/// </para>
/// </remarks>
public sealed record GoogleOAuthOptions
{
    /// <summary>The only scope this provider requests in the read-only slice.</summary>
    public const string CalendarReadonlyScope = "https://www.googleapis.com/auth/calendar.readonly";

    /// <summary>Default calendar id; Google's alias for the account's main calendar.</summary>
    public const string DefaultCalendarId = "primary";

    /// <summary>Google authorization endpoint (desktop-app loopback flow).</summary>
    public static readonly Uri AuthorizationEndpoint =
        new("https://accounts.google.com/o/oauth2/v2/auth");

    /// <summary>Google token endpoint for code exchange and refresh.</summary>
    public static readonly Uri TokenEndpoint = new("https://oauth2.googleapis.com/token");

    /// <summary>Default response cap for one Google Calendar events page (2 MiB).</summary>
    public const int DefaultMaxResponseBytes = 2 * 1024 * 1024;

    /// <summary>Default maximum number of event pages fetched for one range.</summary>
    public const int DefaultMaxPages = 10;

    /// <summary>Default maximum number of events returned for one range.</summary>
    public const int DefaultMaxEvents = 10_000;

    /// <summary>Maximum results Google allows per events page; sent on every request.</summary>
    public const int MaxResultsPerPage = 2500;

    /// <summary>Configured OAuth client id for the desktop app; empty means "not configured yet".</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Loopback redirect URI; defaults to the ephemeral <c>127.0.0.1</c> callback.</summary>
    public Uri RedirectUri { get; init; } = new(OAuthOptions.DefaultLoopbackRedirectUri);

    /// <summary>Single calendar id read by this account; defaults to <c>primary</c>.</summary>
    public string CalendarId { get; init; } = DefaultCalendarId;

    /// <summary>Requested scopes; defaults to <see cref="CalendarReadonlyScope"/> only.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [CalendarReadonlyScope];

    /// <summary>Time allowed for the browser step.</summary>
    public TimeSpan AuthorizationTimeout { get; init; } = OAuthOptions.DefaultAuthorizationTimeout;

    /// <summary>Timeout for one token-endpoint request.</summary>
    public TimeSpan TokenTimeout { get; init; } = OAuthOptions.DefaultTokenTimeout;

    /// <summary>Total timeout for one events page, including the body read.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum size of one events page response body.</summary>
    public int MaxResponseBytes { get; init; } = DefaultMaxResponseBytes;

    /// <summary>Maximum number of pages fetched for one range before the fetch is rejected.</summary>
    public int MaxPages { get; init; } = DefaultMaxPages;

    /// <summary>Maximum number of events returned for one range before the fetch is rejected.</summary>
    public int MaxEvents { get; init; } = DefaultMaxEvents;

    /// <summary>True when a client id is present. The provider reports not-configured otherwise.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

    /// <summary>
    /// Builds the generic OAuth options used by <see cref="LoopbackOAuthFlow"/> and the token
    /// client. Throws when the client id is missing because an interactive flow cannot start.
    /// </summary>
    public OAuthOptions ToOAuthOptions()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                "A Google OAuth client id is required before the sign-in flow can start.");
        }

        var options = BuildOAuthOptions();
        options.Validate();
        return options;
    }

    /// <summary>Validates the Google-specific and shared OAuth settings and returns <c>this</c>.</summary>
    public GoogleOAuthOptions Validate()
    {
        // A missing client id is a valid "not configured yet" state, so it is not an error here;
        // ToOAuthOptions/Validate(true) enforces it before a real authorization attempt.
        BuildOAuthOptions().Validate(requireClientId: IsConfigured);

        if (string.IsNullOrWhiteSpace(CalendarId) || CalendarId.Any(char.IsControl))
        {
            throw new ArgumentException("A calendar id is required.");
        }

        if (CalendarId.Length > 1024)
        {
            throw new ArgumentException("The calendar id is too long.");
        }

        if (RequestTimeout <= TimeSpan.Zero || RequestTimeout > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(paramName: null, message: "The request timeout must be greater than zero and at most five minutes.");
        }

        if (MaxResponseBytes is < 1024 or > 50 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(paramName: null, message: "The response cap must be between 1 KiB and 50 MiB.");
        }

        if (MaxPages is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(paramName: null, message: "The page cap must be between 1 and 100.");
        }

        if (MaxEvents is < 1 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(paramName: null, message: "The event cap must be between 1 and 100000.");
        }

        return this;
    }

    private OAuthOptions BuildOAuthOptions() => new()
    {
        ClientId = ClientId?.Trim() ?? string.Empty,
        AuthorizationEndpoint = AuthorizationEndpoint,
        TokenEndpoint = TokenEndpoint,
        RedirectUri = RedirectUri ?? new Uri(OAuthOptions.DefaultLoopbackRedirectUri),
        Scopes = Scopes ?? [],
        AuthorizationTimeout = AuthorizationTimeout,
        TokenTimeout = TokenTimeout,
        AdditionalAuthorizationParameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        },
    };
}


