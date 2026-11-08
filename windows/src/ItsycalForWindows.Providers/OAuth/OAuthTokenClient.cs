using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ItsycalForWindows.Providers;

/// <summary>Explicit outcome category of one OAuth token-endpoint call.</summary>
public enum OAuthTokenStatus
{
    /// <summary>A token was returned and parsed.</summary>
    Success,

    /// <summary>The endpoint rejected the request for a reason other than the specific statuses below.</summary>
    BadRequest,

    /// <summary>The authorization code or refresh token is invalid/expired/revoked (<c>invalid_grant</c>).</summary>
    InvalidGrant,

    /// <summary>The client registration was rejected (HTTP 401/403 or <c>invalid_client</c>).</summary>
    Unauthorized,

    /// <summary>The endpoint is rate limiting requests (HTTP 429).</summary>
    RateLimited,

    /// <summary>The endpoint returned HTTP 5xx.</summary>
    ServerError,

    /// <summary>The endpoint could not be reached (DNS, TLS, proxy or socket failure).</summary>
    NetworkError,

    /// <summary>The request exceeded <see cref="OAuthOptions.TokenTimeout"/>.</summary>
    Timeout,

    /// <summary>The endpoint answered with malformed JSON or a response missing required fields.</summary>
    InvalidResponse,

    /// <summary>The refreshed token could not be persisted in the secret store.</summary>
    StorageError,
}

/// <summary>
/// Result of a token-endpoint call. <see cref="Message"/> is a fixed, user-safe string and
/// <see cref="ErrorCode"/> is a sanitized OAuth error code; neither ever carries a token, code,
/// verifier or response body.
/// </summary>
public sealed record OAuthTokenResult(
    OAuthTokenStatus Status,
    OAuthToken? Token = null,
    string? ErrorCode = null,
    string? Message = null)
{
    /// <summary>True only when a usable token was returned.</summary>
    public bool IsSuccess => Status == OAuthTokenStatus.Success && Token is not null;

    /// <summary>Creates a success result.</summary>
    public static OAuthTokenResult Success(OAuthToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new OAuthTokenResult(OAuthTokenStatus.Success, token);
    }

    /// <summary>Creates a failure result with a fixed safe message when none is supplied.</summary>
    public static OAuthTokenResult Failure(
        OAuthTokenStatus status,
        string? errorCode = null,
        string? message = null)
    {
        if (status == OAuthTokenStatus.Success)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A failure cannot use the success status.");
        }

        return new OAuthTokenResult(status, Token: null, errorCode, message ?? DefaultMessage(status));
    }

    /// <summary>Redacted representation; no token or code is printed.</summary>
    public override string ToString() =>
        $"OAuthTokenResult {{ Status = {Status}, ErrorCode = {ErrorCode ?? "(none)"}, " +
        $"Token = {(Token is null ? "(none)" : "(present, redacted)")} }}";

    private static string DefaultMessage(OAuthTokenStatus status) => status switch
    {
        OAuthTokenStatus.BadRequest => "The sign-in service rejected the request.",
        OAuthTokenStatus.InvalidGrant => "The sign-in grant is no longer valid.",
        OAuthTokenStatus.Unauthorized => "The sign-in service rejected the client registration.",
        OAuthTokenStatus.RateLimited => "The sign-in service is rate limiting requests.",
        OAuthTokenStatus.ServerError => "The sign-in service is temporarily unavailable.",
        OAuthTokenStatus.NetworkError => "The sign-in service could not be reached.",
        OAuthTokenStatus.Timeout => "The sign-in service timed out.",
        OAuthTokenStatus.InvalidResponse => "The sign-in service returned an invalid response.",
        OAuthTokenStatus.StorageError => "The refreshed token could not be stored securely.",
        _ => "The sign-in service returned an unexpected answer.",
    };
}

/// <summary>
/// OAuth 2.0 token endpoint client for the authorization-code exchange and the refresh grant.
/// </summary>
/// <remarks>
/// <para>
/// The client is a public client: the form carries <c>client_id</c> and either the one-time
/// authorization <c>code</c> plus <c>code_verifier</c>, or the <c>refresh_token</c>; it never sends
/// a client secret or an <c>Authorization</c> header. The endpoint must be HTTPS (validated by
/// <see cref="OAuthOptions.Validate"/>), automatic redirects are rejected, and the response body is
/// capped before parsing.
/// </para>
/// <para>
/// HTTP 400/401/403/429/5xx, network failures, timeouts and malformed bodies map to explicit
/// <see cref="OAuthTokenStatus"/> values instead of exceptions; caller cancellation still
/// propagates as <see cref="OperationCanceledException"/>. There is no logging path, and no token,
/// code, verifier or response body appears in an exception, result message or
/// <see cref="object.ToString"/>.
/// </para>
/// </remarks>
public sealed class OAuthTokenClient
{
    private const string UserAgent = "ItsycalForWindows/1.0 (+https://github.com/Folieson/itsycal-for-windows)";

    private readonly HttpClient _httpClient;
    private readonly OAuthOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a token client over <paramref name="httpClient"/>; the caller owns the client.</summary>
    public OAuthTokenClient(HttpClient httpClient, OAuthOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Validate();
        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Exchanges a one-time authorization code for tokens. <paramref name="redirectUri"/> must be
    /// the exact redirect sent to the authorization endpoint.
    /// </summary>
    public Task<OAuthTokenResult> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (!OAuthPkce.IsValidCodeVerifier(codeVerifier))
        {
            throw new ArgumentException("The PKCE code verifier is not valid.", nameof(codeVerifier));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);
        OAuthOptions.ValidateLoopbackRedirectUri(new Uri(redirectUri, UriKind.Absolute));

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = _options.ClientId.Trim(),
            ["code_verifier"] = codeVerifier,
        };

        return SendAsync(form, cancellationToken);
    }

    /// <summary>Uses a refresh token to obtain a new access token. No client secret is sent.</summary>
    public Task<OAuthTokenResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _options.ClientId.Trim(),
        };

        return SendAsync(form, cancellationToken);
    }

    private async Task<OAuthTokenResult> SendAsync(
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.TokenTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(UserAgent);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return OAuthTokenResult.Failure(OAuthTokenStatus.Timeout);
        }
        catch (HttpRequestException)
        {
            return OAuthTokenResult.Failure(OAuthTokenStatus.NetworkError);
        }
        catch (IOException)
        {
            return OAuthTokenResult.Failure(OAuthTokenStatus.NetworkError);
        }

        using (response)
        {
            if (response.RequestMessage?.RequestUri is { } actual &&
                !string.Equals(
                    actual.AbsoluteUri,
                    _options.TokenEndpoint.AbsoluteUri,
                    StringComparison.Ordinal))
            {
                return OAuthTokenResult.Failure(
                    OAuthTokenStatus.InvalidResponse,
                    message: "The sign-in service returned an unexpected redirect.");
            }

            var statusCode = (int)response.StatusCode;
            if (statusCode is >= 300 and < 400)
            {
                return OAuthTokenResult.Failure(
                    OAuthTokenStatus.InvalidResponse,
                    message: "The sign-in service returned an unexpected redirect.");
            }

            byte[]? body;
            try
            {
                if (response.Content.Headers.ContentLength is { } contentLength &&
                    contentLength > _options.MaxTokenResponseBytes)
                {
                    return OAuthTokenResult.Failure(OAuthTokenStatus.InvalidResponse);
                }

                body = await ReadAtMostAsync(response.Content, _options.MaxTokenResponseBytes, budget.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return OAuthTokenResult.Failure(OAuthTokenStatus.Timeout);
            }
            catch (HttpRequestException)
            {
                return OAuthTokenResult.Failure(OAuthTokenStatus.NetworkError);
            }
            catch (IOException)
            {
                return OAuthTokenResult.Failure(OAuthTokenStatus.NetworkError);
            }

            if (body is null)
            {
                return OAuthTokenResult.Failure(OAuthTokenStatus.InvalidResponse);
            }

            if (!response.IsSuccessStatusCode)
            {
                return MapFailureStatus(statusCode, TryReadErrorCode(body));
            }

            return ParseTokenResponse(body);
        }
    }

    private static OAuthTokenResult MapFailureStatus(int statusCode, string? errorCode) => statusCode switch
    {
        400 when string.Equals(errorCode, "invalid_grant", StringComparison.Ordinal) =>
            OAuthTokenResult.Failure(OAuthTokenStatus.InvalidGrant, errorCode),
        400 when string.Equals(errorCode, "invalid_client", StringComparison.Ordinal) ||
                 string.Equals(errorCode, "unauthorized_client", StringComparison.Ordinal) =>
            OAuthTokenResult.Failure(OAuthTokenStatus.Unauthorized, errorCode),
        400 => OAuthTokenResult.Failure(OAuthTokenStatus.BadRequest, errorCode),
        401 or 403 => OAuthTokenResult.Failure(OAuthTokenStatus.Unauthorized, errorCode),
        (int)HttpStatusCode.TooManyRequests =>
            OAuthTokenResult.Failure(OAuthTokenStatus.RateLimited, errorCode),
        >= 500 and <= 599 => OAuthTokenResult.Failure(OAuthTokenStatus.ServerError, errorCode),
        _ => OAuthTokenResult.Failure(OAuthTokenStatus.BadRequest, errorCode),
    };

    private OAuthTokenResult ParseTokenResponse(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return OAuthTokenResult.Failure(OAuthTokenStatus.InvalidResponse);
            }

            var root = document.RootElement;
            if (!TryReadString(root, "access_token", out var accessToken) ||
                string.IsNullOrWhiteSpace(accessToken))
            {
                return OAuthTokenResult.Failure(OAuthTokenStatus.InvalidResponse);
            }

            TryReadString(root, "refresh_token", out var refreshToken);
            TryReadString(root, "token_type", out var tokenType);
            TryReadString(root, "scope", out var scope);

            DateTimeOffset? expiresAt = null;
            if (root.TryGetProperty("expires_in", out var expiresIn) &&
                expiresIn.ValueKind == JsonValueKind.Number &&
                expiresIn.TryGetInt64(out var seconds))
            {
                if (seconds is < 0 or > 31_536_000)
                {
                    return OAuthTokenResult.Failure(OAuthTokenStatus.InvalidResponse);
                }

                expiresAt = _timeProvider.GetUtcNow().AddSeconds(seconds);
            }

            return OAuthTokenResult.Success(new OAuthToken
            {
                AccessToken = accessToken.Trim(),
                RefreshToken = string.IsNullOrWhiteSpace(refreshToken) ? null : refreshToken.Trim(),
                TokenType = string.IsNullOrWhiteSpace(tokenType) ? null : tokenType.Trim(),
                Scope = string.IsNullOrWhiteSpace(scope) ? null : scope.Trim(),
                ExpiresAt = expiresAt,
            });
        }
        catch (JsonException)
        {
            return OAuthTokenResult.Failure(OAuthTokenStatus.InvalidResponse);
        }
    }

    private static string? TryReadErrorCode(byte[] body)
    {
        if (body.Length == 0)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                TryReadString(document.RootElement, "error", out var error))
            {
                return SanitizeErrorCode(error);
            }
        }
        catch (JsonException)
        {
            // An unparsable error body is still mapped by HTTP status; the body is never surfaced.
        }

        return null;
    }

    private static bool TryReadString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            property.GetString() is { } text)
        {
            value = text;
            return true;
        }

        return false;
    }

    private static string? SanitizeErrorCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 64)
        {
            return null;
        }

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.'))
            {
                return null;
            }
        }

        return trimmed;
    }

    private static async Task<byte[]?> ReadAtMostAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (memory.Length + read > maxBytes)
            {
                return null;
            }

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }
}
