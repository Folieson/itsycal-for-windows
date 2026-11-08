using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for the OAuth token client: form contents (public client, PKCE verifier, no secret),
/// success parsing, HTTP status mapping, network/timeout failures, malformed/oversized bodies,
/// cancellation and redaction.
/// </summary>
public sealed class OAuthTokenClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExchangeCodeAsync_SendsThePkcePublicClientForm()
    {
        string? form = null;
        var handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("application/json", request.Headers.Accept.ToString());
            form = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(
                "{\"access_token\":\"access-1\",\"refresh_token\":\"refresh-1\",\"expires_in\":3600,\"scope\":\"scope-1\",\"token_type\":\"Bearer\"}");
        });

        var (client, clock) = CreateClient(handler);
        var result = await client.ExchangeCodeAsync(
            "one-time-code",
            "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk",
            "http://127.0.0.1:43210/oauth2/callback");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Token);
        Assert.Equal("access-1", result.Token!.AccessToken);
        Assert.Equal("refresh-1", result.Token.RefreshToken);
        Assert.Equal("scope-1", result.Token.Scope);
        Assert.Equal("Bearer", result.Token.TokenType);
        Assert.Equal(clock.Now.AddSeconds(3600), result.Token.ExpiresAt);
        Assert.NotNull(form);
        Assert.Contains("grant_type=authorization_code", form);
        Assert.Contains("code=one-time-code", form);
        Assert.Contains("code_verifier=dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk", form);
        Assert.Contains("client_id=test-client", form);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A43210%2Foauth2%2Fcallback", form);
        Assert.DoesNotContain("client_secret", form);
    }

    [Fact]
    public async Task RefreshAsync_SendsTheRefreshGrantWithoutASecret()
    {
        string? form = null;
        var handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            form = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse("{\"access_token\":\"access-2\",\"expires_in\":1800}");
        });

        var (client, _) = CreateClient(handler);
        var result = await client.RefreshAsync("refresh-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("access-2", result.Token!.AccessToken);
        Assert.Null(result.Token.RefreshToken);
        Assert.NotNull(form);
        Assert.Contains("grant_type=refresh_token", form);
        Assert.Contains("refresh_token=refresh-1", form);
        Assert.Contains("client_id=test-client", form);
        Assert.DoesNotContain("client_secret", form);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}", OAuthTokenStatus.InvalidGrant)]
    [InlineData(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}", OAuthTokenStatus.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, "{}", OAuthTokenStatus.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", OAuthTokenStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, "{}", OAuthTokenStatus.ServerError)]
    [InlineData(HttpStatusCode.BadGateway, "{}", OAuthTokenStatus.ServerError)]
    [InlineData(HttpStatusCode.BadRequest, "{\"error\":\"invalid_request\"}", OAuthTokenStatus.BadRequest)]
    public async Task RefreshAsync_MapsHttpFailuresToExplicitStatuses(
        HttpStatusCode statusCode,
        string body,
        OAuthTokenStatus expected)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body),
        });

        var (client, _) = CreateClient(handler);
        var result = await client.RefreshAsync("refresh-1");

        Assert.False(result.IsSuccess);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Token);
    }

    [Fact]
    public async Task ExchangeCodeAsync_MalformedJsonIsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("{not-json"));
        var (client, _) = CreateClient(handler);

        var result = await client.ExchangeCodeAsync("code", ValidVerifier, "http://127.0.0.1:1/oauth2/callback");

        Assert.Equal(OAuthTokenStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public async Task ExchangeCodeAsync_MissingAccessTokenIsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("{\"refresh_token\":\"refresh\"}"));
        var (client, _) = CreateClient(handler);

        var result = await client.ExchangeCodeAsync("code", ValidVerifier, "http://127.0.0.1:1/oauth2/callback");

        Assert.Equal(OAuthTokenStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public async Task ExchangeCodeAsync_NetworkFailureIsNetworkError()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("offline"));
        var (client, _) = CreateClient(handler);

        var result = await client.ExchangeCodeAsync("code", ValidVerifier, "http://127.0.0.1:1/oauth2/callback");

        Assert.Equal(OAuthTokenStatus.NetworkError, result.Status);
        Assert.DoesNotContain("offline", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExchangeCodeAsync_TimeoutIsExplicit()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse("{}");
        });
        var (client, _) = CreateClient(handler, tokenTimeout: TimeSpan.FromMilliseconds(50));

        var result = await client.ExchangeCodeAsync("code", ValidVerifier, "http://127.0.0.1:1/oauth2/callback");

        Assert.Equal(OAuthTokenStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task ExchangeCodeAsync_CallerCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var handler = new FakeHttpMessageHandler(_ => JsonResponse("{}"));
        var (client, _) = CreateClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ExchangeCodeAsync(
                "code",
                ValidVerifier,
                "http://127.0.0.1:1/oauth2/callback",
                cancellation.Token));
    }

    [Fact]
    public async Task ExchangeCodeAsync_RejectsRedirects()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://evil.example/token");
            return response;
        });
        var (client, _) = CreateClient(handler);

        var result = await client.ExchangeCodeAsync("code", ValidVerifier, "http://127.0.0.1:1/oauth2/callback");

        Assert.Equal(OAuthTokenStatus.InvalidResponse, result.Status);
        Assert.Contains("redirect", result.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExchangeCodeAsync_OversizedResponseIsInvalidResponse()
    {
        var body = "{\"access_token\":\"" + new string('a', 4096) + "\"}";
        var handler = new FakeHttpMessageHandler(_ => JsonResponse(body));
        var (client, _) = CreateClient(handler, maxResponseBytes: 1024);

        var result = await client.ExchangeCodeAsync("code", ValidVerifier, "http://127.0.0.1:1/oauth2/callback");

        Assert.Equal(OAuthTokenStatus.InvalidResponse, result.Status);
    }

    [Fact]
    public async Task ExchangeCodeAsync_RejectsAnInvalidVerifierBeforeSending()
    {
        var handler = new FakeHttpMessageHandler(_ => JsonResponse("{}"));
        var (client, _) = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.ExchangeCodeAsync("code", "too-short", "http://127.0.0.1:1/oauth2/callback"));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://accounts.example.test/auth", "https://oauth.example.test/token")]
    [InlineData("https://accounts.example.test/auth", "http://oauth.example.test/token")]
    [InlineData("https://user:pass@accounts.example.test/auth", "https://oauth.example.test/token")]
    public void Options_RejectNonHttpsOrCredentialedEndpoints(string authorization, string token)
    {
        var options = Options() with
        {
            AuthorizationEndpoint = new Uri(authorization),
            TokenEndpoint = new Uri(token),
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Theory]
    [InlineData("http://localhost:0/oauth2/callback")]
    [InlineData("http://0.0.0.0:1234/oauth2/callback")]
    [InlineData("https://127.0.0.1:1234/oauth2/callback")]
    [InlineData("http://127.0.0.1:1234/")]
    [InlineData("http://127.0.0.1:1234/oauth2/callback?x=1")]
    public void Options_RejectNonLoopbackRedirectUris(string redirect)
    {
        var options = Options() with { RedirectUri = new Uri(redirect) };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Options_RejectReservedExtraAuthorizationParameters()
    {
        var options = Options() with
        {
            AdditionalAuthorizationParameters = new Dictionary<string, string>
            {
                ["state"] = "attacker-controlled",
            },
        };

        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void TokenResultToString_RedactsTheTokenAndCode()
    {
        var result = OAuthTokenResult.Success(new OAuthToken
        {
            AccessToken = "super-secret-access",
            RefreshToken = "super-secret-refresh",
        });

        var text = result.ToString();

        Assert.DoesNotContain("super-secret-access", text, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-refresh", text, StringComparison.Ordinal);
        Assert.Contains("present, redacted", text);
    }

    private const string ValidVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

    private static (OAuthTokenClient Client, ManualTimeProvider Clock) CreateClient(
        FakeHttpMessageHandler handler,
        TimeSpan? tokenTimeout = null,
        int maxResponseBytes = OAuthOptions.DefaultMaxTokenResponseBytes)
    {
        var clock = new ManualTimeProvider(Now);
        var options = Options(tokenTimeout, maxResponseBytes);
        var client = new OAuthTokenClient(
            new HttpClient(handler, disposeHandler: false),
            options,
            clock);
        return (client, clock);
    }

    private static OAuthOptions Options(
        TimeSpan? tokenTimeout = null,
        int maxResponseBytes = OAuthOptions.DefaultMaxTokenResponseBytes) => new()
    {
        ClientId = "test-client",
        AuthorizationEndpoint = new Uri("https://accounts.example.test/auth"),
        TokenEndpoint = new Uri("https://oauth.example.test/token"),
        RedirectUri = new Uri("http://127.0.0.1:0/oauth2/callback"),
        Scopes = ["https://www.googleapis.com/auth/calendar.readonly"],
        TokenTimeout = tokenTimeout ?? TimeSpan.FromSeconds(5),
        MaxTokenResponseBytes = maxResponseBytes,
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json),
    };
}
