using System.Collections.ObjectModel;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Generic OAuth 2.0 authorization-code + PKCE settings shared by every external provider.
/// </summary>
/// <remarks>
/// <para>
/// The flow is the public-client native-app model (RFC 8252): authorization code with PKCE S256,
/// a loopback redirect on <c>127.0.0.1</c> and no client secret. A client id is configuration, not
/// a credential; the app never ships a client secret. Endpoints are validated as absolute HTTPS
/// URLs with no embedded user-info so a configuration mistake cannot downgrade the token exchange.
/// </para>
/// <para>
/// <see cref="RedirectUri"/> defaults to an ephemeral loopback port
/// (<c>http://127.0.0.1:0/oauth2/callback</c>); the flow binds the socket first and replaces port
/// 0 with the actual port before building the authorization URL. A fixed non-zero port is also
/// accepted for providers that require an exact registered redirect.
/// </para>
/// </remarks>
public sealed record OAuthOptions
{
    /// <summary>Loopback redirect template used when no fixed port is configured.</summary>
    public const string DefaultLoopbackRedirectUri = "http://127.0.0.1:0/oauth2/callback";

    /// <summary>Default timeout for the interactive authorization step (ADR 0005: at most 5 minutes).</summary>
    public static readonly TimeSpan DefaultAuthorizationTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Default timeout for one token-endpoint request.</summary>
    public static readonly TimeSpan DefaultTokenTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Default response cap for a token-endpoint response.</summary>
    public const int DefaultMaxTokenResponseBytes = 256 * 1024;

    private static readonly IReadOnlyDictionary<string, string> EmptyParameters =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal));

    private static readonly string[] ReservedAuthorizationParameters =
    [
        "response_type",
        "client_id",
        "redirect_uri",
        "scope",
        "state",
        "code_challenge",
        "code_challenge_method",
    ];

    /// <summary>OAuth client id. Required and configurable; never a client secret.</summary>
    public required string ClientId { get; init; }

    /// <summary>Authorization endpoint; must be an absolute HTTPS URL.</summary>
    public required Uri AuthorizationEndpoint { get; init; }

    /// <summary>Token endpoint; must be an absolute HTTPS URL.</summary>
    public required Uri TokenEndpoint { get; init; }

    /// <summary>Loopback redirect URI; defaults to <see cref="DefaultLoopbackRedirectUri"/>.</summary>
    public Uri RedirectUri { get; init; } = new(DefaultLoopbackRedirectUri);

    /// <summary>Requested scopes; at least one is required by <see cref="Validate"/>.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>Total time allowed for the user to finish the browser step.</summary>
    public TimeSpan AuthorizationTimeout { get; init; } = DefaultAuthorizationTimeout;

    /// <summary>Timeout for one token-endpoint request (code exchange or refresh).</summary>
    public TimeSpan TokenTimeout { get; init; } = DefaultTokenTimeout;

    /// <summary>Maximum token-endpoint response body size.</summary>
    public int MaxTokenResponseBytes { get; init; } = DefaultMaxTokenResponseBytes;

    /// <summary>
    /// Extra authorization-query parameters (for example Google's <c>access_type=offline</c> and
    /// <c>prompt=consent</c>). Reserved OAuth parameters cannot be overridden.
    /// </summary>
    public IReadOnlyDictionary<string, string> AdditionalAuthorizationParameters { get; init; } =
        EmptyParameters;

    /// <summary>
    /// Validates the complete configuration and returns <c>this</c> for terse construction. A
    /// provider that is not configured yet may set <paramref name="requireClientId"/> to false.
    /// </summary>
    /// <exception cref="ArgumentException">An endpoint, redirect, scope or extra parameter is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A timeout or size limit is outside its bounds.</exception>
    public OAuthOptions Validate(bool requireClientId = true)
    {
        if (requireClientId && string.IsNullOrWhiteSpace(ClientId))
        {
            throw new ArgumentException("An OAuth client id is required.");
        }

        if (!IsHttpsEndpoint(AuthorizationEndpoint))
        {
            throw new ArgumentException(
                "The authorization endpoint must be an absolute HTTPS URL without user-info or a fragment.");
        }

        if (!IsHttpsEndpoint(TokenEndpoint))
        {
            throw new ArgumentException(
                "The token endpoint must be an absolute HTTPS URL without user-info or a fragment.");
        }

        ValidateLoopbackRedirectUri(RedirectUri);

        if (Scopes is null || Scopes.Count == 0)
        {
            throw new ArgumentException("At least one OAuth scope is required.");
        }

        if (Scopes.Count > 20)
        {
            throw new ArgumentException("Too many OAuth scopes.");
        }

        foreach (var scope in Scopes)
        {
            if (string.IsNullOrWhiteSpace(scope) || scope.Any(char.IsWhiteSpace) || scope.Any(char.IsControl))
            {
                throw new ArgumentException(
                    "An OAuth scope must be a non-empty value without whitespace.");
            }
        }

        if (AuthorizationTimeout <= TimeSpan.Zero || AuthorizationTimeout > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(paramName: null, message: "The authorization timeout must be greater than zero and at most ten minutes.");
        }

        if (TokenTimeout <= TimeSpan.Zero || TokenTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(paramName: null, message: "The token timeout must be greater than zero and at most two minutes.");
        }

        if (MaxTokenResponseBytes is < 1024 or > 4 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(paramName: null, message: "The token response cap must be between 1 KiB and 4 MiB.");
        }

        var extras = AdditionalAuthorizationParameters ?? EmptyParameters;
        if (extras.Count > 20)
        {
            throw new ArgumentException("Too many extra authorization parameters.");
        }

        foreach (var pair in extras)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Any(char.IsControl))
            {
                throw new ArgumentException("An extra authorization parameter needs a non-empty name.");
            }

            if (ReservedAuthorizationParameters.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A reserved OAuth parameter must not be overridden.");
            }

            if (string.IsNullOrEmpty(pair.Value) || pair.Value.Any(char.IsControl))
            {
                throw new ArgumentException("An extra authorization parameter needs a non-empty value.");
            }
        }

        return this;
    }

    /// <summary>
    /// Validates and returns a loopback redirect URI: absolute <c>http</c>, host exactly
    /// <c>127.0.0.1</c>, a callback path below the root, no query/fragment/user-info and a port in
    /// <c>0..65535</c>. Port 0 means "let the OS choose an ephemeral port".
    /// </summary>
    public static Uri ValidateLoopbackRedirectUri(Uri? redirectUri)
    {
        if (redirectUri is null ||
            !redirectUri.IsAbsoluteUri ||
            redirectUri.Scheme != Uri.UriSchemeHttp ||
            !string.Equals(redirectUri.Host, "127.0.0.1", StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(redirectUri.UserInfo) ||
            !string.IsNullOrEmpty(redirectUri.Query) ||
            !string.IsNullOrEmpty(redirectUri.Fragment) ||
            redirectUri.AbsolutePath.Length <= 1 ||
            redirectUri.Port is < 0 or > 65535)
        {
            throw new ArgumentException(
                "The redirect URI must be http://127.0.0.1[:port]/callback with a non-root path.",
                nameof(redirectUri));
        }

        return redirectUri;
    }

    private static bool IsHttpsEndpoint(Uri? endpoint) =>
        endpoint is not null &&
        endpoint.IsAbsoluteUri &&
        endpoint.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(endpoint.UserInfo) &&
        string.IsNullOrEmpty(endpoint.Fragment) &&
        !string.IsNullOrWhiteSpace(endpoint.Host);
}


