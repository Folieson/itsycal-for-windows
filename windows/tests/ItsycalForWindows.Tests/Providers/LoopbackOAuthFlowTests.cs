using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Offline tests for the loopback PKCE flow: authorization URL shape, one callback, state and
/// denial handling, timeout/cancellation, browser-launch failure, single code exchange and
/// redaction. A test launcher sends the browser callback over a real loopback socket; no external
/// network or browser is used.
/// </summary>
public sealed class LoopbackOAuthFlowTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AuthorizeAsync_BuildsThePkceUrlAndExchangesTheCodeOnce()
    {
        Uri? authorizationUrl = null;
        var tokenRequests = new List<string>();
        var handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            tokenRequests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return JsonResponse("{\"access_token\":\"access-from-code\",\"refresh_token\":\"refresh\",\"expires_in\":3600}");
        });

        var launcher = new DelegateBrowserLauncher(async (url, _) =>
        {
            authorizationUrl = url;
            await SendCallbackAsync(url, new Dictionary<string, string> { ["code"] = "test-code" });
        });

        var result = await CreateFlow(handler, launcher).AuthorizeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("access-from-code", result.Token!.AccessToken);
        Assert.Single(tokenRequests);

        Assert.NotNull(authorizationUrl);
        var query = ParseQuery(authorizationUrl!.Query);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("test-client", query["client_id"]);
        Assert.Equal("https://www.googleapis.com/auth/calendar.readonly", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(43, query["state"].Length);
        Assert.Equal(43, query["code_challenge"].Length);
        Assert.Equal("offline", query["access_type"]);
        Assert.Equal("consent", query["prompt"]);
        Assert.DoesNotContain("client_secret", authorizationUrl.Query, StringComparison.OrdinalIgnoreCase);

        var redirect = new Uri(query["redirect_uri"]);
        Assert.Equal("http", redirect.Scheme);
        Assert.Equal("127.0.0.1", redirect.Host);
        Assert.NotEqual(0, redirect.Port);
        Assert.Equal("/oauth2/callback", redirect.AbsolutePath);

        var form = tokenRequests.Single();
        Assert.Contains("grant_type=authorization_code", form);
        Assert.Contains("code=test-code", form);
        Assert.Contains("client_id=test-client", form);
        Assert.Contains("code_verifier=", form);
        Assert.DoesNotContain("client_secret", form);

        var verifier = ParseForm(form)["code_verifier"];
        Assert.Equal(86, verifier.Length);
        Assert.DoesNotContain(verifier, authorizationUrl.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("test-code", authorizationUrl.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain("access-from-code", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("test-code", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthorizeAsync_StateMismatchFailsWithoutTokenExchange()
    {
        var tokenRequestCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            tokenRequestCount++;
            return JsonResponse("{\"access_token\":\"should-not-be-used\"}");
        });

        var launcher = new DelegateBrowserLauncher(async (url, _) =>
        {
            await SendCallbackAsync(url, new Dictionary<string, string>
            {
                ["code"] = "test-code",
                ["state"] = "attacker-state",
            });
        });

        var result = await CreateFlow(handler, launcher).AuthorizeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(OAuthFlowStatus.StateMismatch, result.Status);
        Assert.Equal(0, tokenRequestCount);
    }

    [Fact]
    public async Task AuthorizeAsync_AccessDeniedIsExplicitAndSendsNoTokenRequest()
    {
        var tokenRequestCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            tokenRequestCount++;
            return JsonResponse("{}");
        });

        var launcher = new DelegateBrowserLauncher(async (url, _) =>
        {
            await SendCallbackAsync(url, new Dictionary<string, string> { ["error"] = "access_denied" });
        });

        var result = await CreateFlow(handler, launcher).AuthorizeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(OAuthFlowStatus.AccessDenied, result.Status);
        Assert.Equal("access_denied", result.ErrorCode);
        Assert.Equal(0, tokenRequestCount);
    }

    [Fact]
    public async Task AuthorizeAsync_TimeoutIsExplicit()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("{}"));
        var launcher = new DelegateBrowserLauncher((_, _) => Task.CompletedTask);
        var flow = CreateFlow(handler, launcher, authorizationTimeout: TimeSpan.FromMilliseconds(100));

        var result = await flow.AuthorizeAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal(OAuthFlowStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task AuthorizeAsync_CallerCancellationPropagates()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("{}"));
        using var cancellation = new CancellationTokenSource();
        var launcher = new DelegateBrowserLauncher((_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateFlow(handler, launcher).AuthorizeAsync(cancellation.Token));
    }

    [Fact]
    public async Task AuthorizeAsync_BrowserLaunchFailureIsExplicit()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("{}"));
        var launcher = new DelegateBrowserLauncher((_, _) =>
            throw new InvalidOperationException("no browser"));

        var result = await CreateFlow(handler, launcher).AuthorizeAsync();

        Assert.Equal(OAuthFlowStatus.BrowserLaunchFailed, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AuthorizeAsync_MissingCodeIsInvalidCallback()
    {
        var tokenRequestCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            tokenRequestCount++;
            return JsonResponse("{}");
        });
        var launcher = new DelegateBrowserLauncher(async (url, _) =>
        {
            await SendCallbackAsync(url, new Dictionary<string, string>());
        });

        var result = await CreateFlow(handler, launcher).AuthorizeAsync();

        Assert.Equal(OAuthFlowStatus.InvalidCallback, result.Status);
        Assert.Equal(0, tokenRequestCount);
    }

    [Fact]
    public async Task AuthorizeAsync_WrongHostIsRejected()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("{}"));
        var launcher = new DelegateBrowserLauncher(async (url, _) =>
        {
            await SendCallbackAsync(
                url,
                new Dictionary<string, string> { ["code"] = "test-code" },
                hostOverride: "evil.example:80");
        });

        var result = await CreateFlow(handler, launcher).AuthorizeAsync();

        Assert.Equal(OAuthFlowStatus.InvalidCallback, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AuthorizeAsync_AcceptsOnlyOneCallback()
    {
        Uri? authorizationUrl = null;
        var tokenRequestCount = 0;
        var handler = new FakeHttpMessageHandler(_ =>
        {
            tokenRequestCount++;
            return JsonResponse("{\"access_token\":\"access\"}");
        });
        var launcher = new DelegateBrowserLauncher(async (url, _) =>
        {
            authorizationUrl = url;
            await SendCallbackAsync(url, new Dictionary<string, string> { ["code"] = "test-code" });
            await SendSecondCallbackWithoutWaitingAsync(url);
        });

        var result = await CreateFlow(handler, launcher).AuthorizeAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(1, tokenRequestCount);
        Assert.NotNull(authorizationUrl);
    }

    [Fact]
    public async Task AuthorizeAsync_ResultAndUrlNeverExposeTheVerifier()
    {
        string? verifier = null;
        Uri? authorizationUrl = null;
        var handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            var form = ParseForm(await request.Content!.ReadAsStringAsync(cancellationToken));
            verifier = form["code_verifier"];
            return JsonResponse("{\"access_token\":\"secret-access\",\"refresh_token\":\"secret-refresh\"}");
        });
        var launcher = new DelegateBrowserLauncher(async (url, _) =>
        {
            authorizationUrl = url;
            await SendCallbackAsync(url, new Dictionary<string, string> { ["code"] = "secret-code" });
        });

        var result = await CreateFlow(handler, launcher).AuthorizeAsync();

        Assert.True(result.IsSuccess);
        Assert.NotNull(verifier);
        Assert.NotNull(authorizationUrl);
        var resultText = result.ToString();
        Assert.DoesNotContain(verifier!, resultText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-access", resultText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-refresh", resultText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-code", authorizationUrl!.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain(verifier!, authorizationUrl.AbsoluteUri, StringComparison.Ordinal);
    }

    private static LoopbackOAuthFlow CreateFlow(
        FakeHttpMessageHandler handler,
        IOAuthBrowserLauncher launcher,
        TimeSpan? authorizationTimeout = null)
    {
        var options = Options(authorizationTimeout);
        var clock = new ManualTimeProvider(Now);
        var httpClient = new HttpClient(handler, disposeHandler: false);
        var tokenClient = new OAuthTokenClient(httpClient, options, clock);
        return new LoopbackOAuthFlow(options, tokenClient, launcher);
    }

    private static OAuthOptions Options(TimeSpan? authorizationTimeout = null) => new()
    {
        ClientId = "test-client",
        AuthorizationEndpoint = new Uri("https://accounts.example.test/auth"),
        TokenEndpoint = new Uri("https://oauth.example.test/token"),
        RedirectUri = new Uri("http://127.0.0.1:0/oauth2/callback"),
        Scopes = ["https://www.googleapis.com/auth/calendar.readonly"],
        AuthorizationTimeout = authorizationTimeout ?? TimeSpan.FromSeconds(10),
        TokenTimeout = TimeSpan.FromSeconds(5),
        AdditionalAuthorizationParameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        },
    };

    private static async Task SendCallbackAsync(
        Uri authorizationUrl,
        Dictionary<string, string> values,
        string? hostOverride = null)
    {
        var authorizationQuery = ParseQuery(authorizationUrl.Query);
        var redirect = new Uri(authorizationQuery["redirect_uri"]);
        var parameters = new Dictionary<string, string>(values, StringComparer.Ordinal);
        if (!parameters.ContainsKey("state"))
        {
            parameters["state"] = authorizationQuery["state"];
        }

        var query = string.Join(
            '&',
            parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, redirect.Port);
        var stream = client.GetStream();
        var host = hostOverride ?? $"{redirect.Host}:{redirect.Port}";
        var request =
            $"GET {redirect.AbsolutePath}?{query} HTTP/1.1\r\n" +
            $"Host: {host}\r\n" +
            "Connection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
        await stream.FlushAsync();

        // The flow calls the browser launcher before it starts accepting the callback, so the
        // launcher must return without waiting for the HTTP response; a background read drains the
        // page and closes the socket after the flow has written it.
        _ = Task.Run(async () =>
        {
            try
            {
                var buffer = new byte[2048];
                await stream.ReadAsync(buffer);
            }
            catch (IOException)
            {
                // The flow may close the connection right after writing its page.
            }
            finally
            {
                stream.Dispose();
                client.Dispose();
            }
        });
    }

    private static async Task SendSecondCallbackWithoutWaitingAsync(Uri authorizationUrl)
    {
        var authorizationQuery = ParseQuery(authorizationUrl.Query);
        var redirect = new Uri(authorizationQuery["redirect_uri"]);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, redirect.Port);
            await using var stream = client.GetStream();
            var request =
                $"GET {redirect.AbsolutePath}?code=second&state={Uri.EscapeDataString(authorizationQuery["state"])} " +
                "HTTP/1.1\r\n" +
                $"Host: {redirect.Host}:{redirect.Port}\r\n" +
                "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request));
            await stream.FlushAsync();
        }
        catch (SocketException)
        {
            // The listener may already be closed; exactly one callback is the expected outcome.
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            var key = Uri.UnescapeDataString(separator >= 0 ? part[..separator] : part);
            var value = Uri.UnescapeDataString(separator >= 0 ? part[(separator + 1)..] : string.Empty);
            result[key] = value;
        }

        return result;
    }

    private static Dictionary<string, string> ParseForm(string form) => ParseQuery(form);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json),
    };

    private sealed class DelegateBrowserLauncher : IOAuthBrowserLauncher
    {
        private readonly Func<Uri, CancellationToken, Task> _launch;

        public DelegateBrowserLauncher(Func<Uri, CancellationToken, Task> launch) => _launch = launch;

        public Task LaunchAsync(Uri authorizationUrl, CancellationToken cancellationToken = default) =>
            _launch(authorizationUrl, cancellationToken);
    }
}
