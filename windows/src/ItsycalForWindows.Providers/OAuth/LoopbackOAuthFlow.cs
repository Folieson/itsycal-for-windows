using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ItsycalForWindows.Providers;

/// <summary>Explicit outcome of one interactive loopback OAuth attempt.</summary>
public enum OAuthFlowStatus
{
    /// <summary>The browser callback carried a code and the token exchange succeeded.</summary>
    Success,

    /// <summary>The user (or the provider) denied the authorization request.</summary>
    AccessDenied,

    /// <summary>The provider returned an authorization error other than a denial.</summary>
    ProviderError,

    /// <summary>The callback state did not match, was missing or had already been used.</summary>
    StateMismatch,

    /// <summary>No callback arrived within <see cref="OAuthOptions.AuthorizationTimeout"/>.</summary>
    Timeout,

    /// <summary>The callback was malformed or came to the wrong path/host.</summary>
    InvalidCallback,

    /// <summary>The loopback listener could not bind or failed while accepting the callback.</summary>
    ListenerFailed,

    /// <summary>The default browser could not be opened.</summary>
    BrowserLaunchFailed,

    /// <summary>The authorization code was received but the token endpoint rejected or failed it.</summary>
    TokenExchangeFailed,
}

/// <summary>
/// Result of <see cref="LoopbackOAuthFlow.AuthorizeAsync"/>. The token is present only on success;
/// the message is a fixed, user-safe string and never contains a code, verifier or token.
/// </summary>
public sealed record OAuthFlowResult(
    OAuthFlowStatus Status,
    OAuthToken? Token = null,
    OAuthTokenStatus? TokenStatus = null,
    string? ErrorCode = null,
    string? Message = null)
{
    /// <summary>True only when the flow completed and a token was obtained.</summary>
    public bool IsSuccess => Status == OAuthFlowStatus.Success && Token is not null;

    /// <summary>Redacted representation; no code, verifier or token is printed.</summary>
    public override string ToString() =>
        $"OAuthFlowResult {{ Status = {Status}, TokenStatus = {TokenStatus?.ToString() ?? "(none)"}, " +
        $"ErrorCode = {ErrorCode ?? "(none)"}, Token = {(Token is null ? "(none)" : "(present, redacted)")} }}";
}

/// <summary>
/// Authorization-code + PKCE flow over an ephemeral loopback listener on <c>127.0.0.1</c>.
/// </summary>
/// <remarks>
/// <para>
/// The listener binds to <see cref="IPAddress.Loopback"/> and port 0 (or the configured fixed port)
/// before the system browser is opened, so no other process can race the callback port after the
/// authorization URL exists. Only one callback is accepted; it must be a <c>GET</c> to the exact
/// redirect path with a loopback <c>Host</c> header, a matching single-use state and either a
/// denial or an authorization code. The code is exchanged exactly once with the PKCE verifier and
/// no client secret.
/// </para>
/// <para>
/// The authorization URL carries <c>response_type=code</c>, <c>client_id</c>, <c>redirect_uri</c>,
/// <c>scope</c>, <c>state</c>, <c>code_challenge</c> and
/// <c>code_challenge_method=S256</c>; provider-specific extras (Google's
/// <c>access_type=offline</c> and <c>prompt=consent</c>) come from
/// <see cref="OAuthOptions.AdditionalAuthorizationParameters"/>. There is no client secret.
/// </para>
/// <para>
/// The flow times out after <see cref="OAuthOptions.AuthorizationTimeout"/> (default five minutes)
/// and closes the listener on success, failure, timeout or cancellation. Caller cancellation
/// propagates as <see cref="OperationCanceledException"/>. The flow has no logging path: the
/// authorization URL, callback query, code, verifier, state and tokens are never logged, and all
/// status messages are fixed safe strings.
/// </para>
/// </remarks>
public sealed class LoopbackOAuthFlow
{
    private const int MaxRequestHeadBytes = 16 * 1024;

    private readonly OAuthOptions _options;
    private readonly OAuthTokenClient _tokenClient;
    private readonly IOAuthBrowserLauncher _browserLauncher;

    /// <summary>Creates the flow. The token client must target the same provider as the options.</summary>
    public LoopbackOAuthFlow(
        OAuthOptions options,
        OAuthTokenClient tokenClient,
        IOAuthBrowserLauncher? browserLauncher = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenClient);

        _options = options.Validate();
        _tokenClient = tokenClient;
        _browserLauncher = browserLauncher ?? new SystemBrowserLauncher();
    }

    /// <summary>
    /// Runs one interactive authorization. Returns an explicit result for denial, state mismatch,
    /// timeout, listener/browser failure or token-exchange failure; caller cancellation propagates.
    /// </summary>
    public async Task<OAuthFlowResult> AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pkce = OAuthPkce.Create();
        TcpListener? listener = null;

        try
        {
            listener = new TcpListener(IPAddress.Loopback, _options.RedirectUri.Port);
            try
            {
                listener.Start();
            }
            catch (SocketException)
            {
                return new OAuthFlowResult(
                    OAuthFlowStatus.ListenerFailed,
                    Message: "The sign-in callback listener could not start. Check local firewall policy and try again.");
            }

            var actualPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirectUri = BuildRedirectUri(actualPort);
            var authorizationUri = BuildAuthorizationUri(redirectUri, pkce);

            try
            {
                await _browserLauncher.LaunchAsync(authorizationUri, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // A browser/launcher failure is an explicit, safe result.
            catch (Exception)
            {
                return new OAuthFlowResult(
                    OAuthFlowStatus.BrowserLaunchFailed,
                    Message: "The system browser could not be opened.");
            }
#pragma warning restore CA1031

            var callback = await WaitForCallbackAsync(listener, redirectUri, pkce, cancellationToken)
                .ConfigureAwait(false);

            if (!callback.IsSuccess)
            {
                return new OAuthFlowResult(
                    callback.Status,
                    ErrorCode: callback.ErrorCode,
                    Message: callback.Message);
            }

            var tokenResult = await _tokenClient
                .ExchangeCodeAsync(callback.Code!, pkce.CodeVerifier, redirectUri.AbsoluteUri, cancellationToken)
                .ConfigureAwait(false);

            return tokenResult.IsSuccess
                ? new OAuthFlowResult(
                    OAuthFlowStatus.Success,
                    tokenResult.Token,
                    tokenResult.Status,
                    tokenResult.ErrorCode)
                : new OAuthFlowResult(
                    OAuthFlowStatus.TokenExchangeFailed,
                    TokenStatus: tokenResult.Status,
                    ErrorCode: tokenResult.ErrorCode,
                    Message: tokenResult.Message);
        }
        finally
        {
            listener?.Stop();
        }
    }

    private async Task<CallbackResult> WaitForCallbackAsync(
        TcpListener listener,
        Uri redirectUri,
        OAuthPkce pkce,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.AuthorizationTimeout);

        try
        {
            using var client = await listener.AcceptTcpClientAsync(budget.Token).ConfigureAwait(false);
            client.NoDelay = true;
            using var stream = client.GetStream();

            var requestHead = await ReadRequestHeadAsync(stream, budget.Token).ConfigureAwait(false);
            var callback = InterpretCallback(requestHead, redirectUri, pkce);

            try
            {
                await WriteBrowserResponseAsync(stream, callback.IsSuccess, budget.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // The code/state decision is already final; a late response write must not change it.
            }
            catch (IOException)
            {
                // The browser may close the connection before reading the page; the code is still valid.
            }
            catch (SocketException)
            {
                // Same as above.
            }

            return callback;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return CallbackResult.Failed(
                OAuthFlowStatus.Timeout,
                message: "The sign-in timed out. Try again.");
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            return CallbackResult.Failed(
                OAuthFlowStatus.InvalidCallback,
                message: "The sign-in callback could not be read.");
        }
    }

    private static CallbackResult InterpretCallback(string? requestHead, Uri redirectUri, OAuthPkce pkce)
    {
        if (string.IsNullOrEmpty(requestHead))
        {
            return CallbackResult.Failed(OAuthFlowStatus.InvalidCallback, message: "The sign-in callback was malformed.");
        }

        var lines = requestHead.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0)
        {
            return CallbackResult.Failed(OAuthFlowStatus.InvalidCallback, message: "The sign-in callback was malformed.");
        }

        var requestLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLine.Length != 3 ||
            !string.Equals(requestLine[0], "GET", StringComparison.Ordinal) ||
            !requestLine[2].StartsWith("HTTP/", StringComparison.Ordinal))
        {
            return CallbackResult.Failed(OAuthFlowStatus.InvalidCallback, message: "The sign-in callback was malformed.");
        }

        var target = requestLine[1];
        if (!target.StartsWith('/'))
        {
            return CallbackResult.Failed(OAuthFlowStatus.InvalidCallback, message: "The sign-in callback was malformed.");
        }

        var queryStart = target.IndexOf('?', StringComparison.Ordinal);
        var rawPath = queryStart >= 0 ? target[..queryStart] : target;
        var rawQuery = queryStart >= 0 ? target[(queryStart + 1)..] : string.Empty;

        if (!string.Equals(rawPath, redirectUri.AbsolutePath, StringComparison.Ordinal))
        {
            return CallbackResult.Failed(OAuthFlowStatus.InvalidCallback, message: "The sign-in callback used the wrong path.");
        }

        if (!HasExpectedHost(lines, redirectUri))
        {
            return CallbackResult.Failed(OAuthFlowStatus.InvalidCallback, message: "The sign-in callback used an unexpected host.");
        }

        if (!TryParseQuery(rawQuery, out var parameters))
        {
            return CallbackResult.Failed(OAuthFlowStatus.InvalidCallback, message: "The sign-in callback was malformed.");
        }

        if (!parameters.TryGetValue("state", out var state) || !pkce.TryValidateAndConsumeState(state))
        {
            return CallbackResult.Failed(
                OAuthFlowStatus.StateMismatch,
                message: "The sign-in response could not be verified. Start the sign-in again.");
        }

        if (parameters.TryGetValue("error", out var error))
        {
            var errorCode = SanitizeErrorCode(error);
            if (string.Equals(errorCode, "access_denied", StringComparison.OrdinalIgnoreCase))
            {
                return CallbackResult.Failed(
                    OAuthFlowStatus.AccessDenied,
                    errorCode: "access_denied",
                    message: "Sign-in was canceled.");
            }

            return CallbackResult.Failed(
                OAuthFlowStatus.ProviderError,
                errorCode: errorCode,
                message: "The calendar provider rejected the sign-in request.");
        }

        if (!parameters.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            return CallbackResult.Failed(OAuthFlowStatus.InvalidCallback, message: "The sign-in callback carried no authorization code.");
        }

        var trimmedCode = code.Trim();
        if (trimmedCode.Length > 8192)
        {
            return CallbackResult.Failed(OAuthFlowStatus.InvalidCallback, message: "The sign-in callback was malformed.");
        }

        return new CallbackResult(OAuthFlowStatus.Success, trimmedCode, null, null);
    }

    private Uri BuildRedirectUri(int actualPort)
    {
        var builder = new UriBuilder(_options.RedirectUri)
        {
            Port = actualPort,
        };

        return builder.Uri;
    }

    private Uri BuildAuthorizationUri(Uri redirectUri, OAuthPkce pkce)
    {
        var parameters = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", _options.ClientId.Trim()),
            new("redirect_uri", redirectUri.AbsoluteUri),
            new("scope", string.Join(' ', _options.Scopes)),
            new("state", pkce.State),
            new("code_challenge", pkce.CodeChallenge),
            new("code_challenge_method", "S256"),
        };

        foreach (var pair in _options.AdditionalAuthorizationParameters)
        {
            parameters.Add(pair);
        }

        var query = string.Join(
            '&',
            parameters.Select(static pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        var builder = new UriBuilder(_options.AuthorizationEndpoint)
        {
            Query = query,
        };

        return builder.Uri;
    }

    private static async Task<string?> ReadRequestHeadAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[2048];
        using var memory = new MemoryStream();

        while (memory.Length < MaxRequestHeadBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            memory.Write(buffer, 0, read);
            var bytes = memory.GetBuffer();
            var length = (int)memory.Length;
            var terminator = IndexOfHeaderTerminator(bytes, length);
            if (terminator >= 0)
            {
                return Encoding.Latin1.GetString(bytes, 0, terminator);
            }
        }

        return null;
    }

    private static int IndexOfHeaderTerminator(byte[] bytes, int length)
    {
        for (var index = 0; index + 3 < length; index++)
        {
            if (bytes[index] == '\r' &&
                bytes[index + 1] == '\n' &&
                bytes[index + 2] == '\r' &&
                bytes[index + 3] == '\n')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool HasExpectedHost(string[] lines, Uri redirectUri)
    {
        var expected = $"{redirectUri.Host}:{redirectUri.Port.ToString(CultureInfo.InvariantCulture)}";

        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))
            {
                var actual = line[5..].Trim();
                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static bool TryParseQuery(string query, out Dictionary<string, string> parameters)
    {
        parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (query.Length == 0)
        {
            return true;
        }

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            var rawKey = separator >= 0 ? part[..separator] : part;
            var rawValue = separator >= 0 ? part[(separator + 1)..] : string.Empty;

            string key;
            string value;
            try
            {
                key = Uri.UnescapeDataString(rawKey.Replace("+", " ", StringComparison.Ordinal));
                value = Uri.UnescapeDataString(rawValue.Replace("+", " ", StringComparison.Ordinal));
            }
            catch (UriFormatException)
            {
                return false;
            }

            if (key.Length == 0 || !parameters.TryAdd(key, value))
            {
                return false;
            }
        }

        return true;
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

    private static async Task WriteBrowserResponseAsync(
        NetworkStream stream,
        bool success,
        CancellationToken cancellationToken)
    {
        var statusCode = success ? 200 : 400;
        var reason = success ? "OK" : "Bad Request";
        var message = success
            ? "Sign-in complete. You can close this window and return to Calwren."
            : "Sign-in could not be completed. You can close this window and try again in Calwren.";

        var html =
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Calwren sign-in</title></head>" +
            $"<body><p>{message}</p></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var header =
            $"HTTP/1.1 {statusCode.ToString(CultureInfo.InvariantCulture)} {reason}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Referrer-Policy: no-referrer\r\n" +
            "Connection: close\r\n\r\n";

        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct CallbackResult(
        OAuthFlowStatus Status,
        string? Code,
        string? ErrorCode,
        string? Message)
    {
        public bool IsSuccess => Status == OAuthFlowStatus.Success && !string.IsNullOrEmpty(Code);

        public static CallbackResult Failed(
            OAuthFlowStatus status,
            string? errorCode = null,
            string? message = null) =>
            new(status, Code: null, errorCode, message);
    }
}
