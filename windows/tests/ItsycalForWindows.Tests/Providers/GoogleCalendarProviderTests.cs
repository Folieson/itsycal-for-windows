using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Providers;
using ItsycalForWindows.Services;
using Xunit;
using CalendarEvent = ItsycalForWindows.Models.CalendarEvent;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Offline tests for <see cref="GoogleCalendarProvider"/>: configuration probing, request shape,
/// event mapping (timed/all-day/meeting links), pagination and caps, refresh-on-401, explicit
/// HTTP/JSON/network errors, cancellation and token persistence.
/// </summary>
public sealed class GoogleCalendarProviderTests
{
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 12, 1);
    private static readonly ProviderEventRange Range = ProviderEventRange.Days(From, To);
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProbeAsync_WithoutAClientId_IsNotConfiguredWithoutNetwork()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        using var provider = CreateProvider(handler, Options() with { ClientId = string.Empty });

        var status = await provider.ProbeAsync(ProviderRequest.Probe(Account()));

        Assert.Equal(ProviderState.NotConfigured, status.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ProbeAsync_WithoutAToken_IsNotConfiguredWithoutNetwork()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        using var provider = CreateProvider(handler, Options());

        var status = await provider.ProbeAsync(ProviderRequest.Probe(Account()));

        Assert.Equal(ProviderState.NotConfigured, status.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ProbeAsync_WithAUsableAccessToken_IsReadyWithoutNetwork()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        using var provider = CreateProvider(handler, Options());

        var status = await provider.ProbeAsync(
            ProviderRequest.Probe(Account(), Token(expires: Now.AddHours(1))));

        Assert.True(status.IsReady);
        Assert.Equal(ProviderCapabilities.ReadOnlyEvents, status.Capabilities);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ProbeAsync_WithOnlyARefreshToken_IsReady()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        using var provider = CreateProvider(handler, Options());

        var status = await provider.ProbeAsync(
            ProviderRequest.Probe(Account(), new ProviderSecret { RefreshToken = "refresh" }));

        Assert.True(status.IsReady);
    }

    [Fact]
    public async Task ProbeAsync_WithAnExpiredAccessTokenAndNoRefreshToken_IsUnauthorized()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        using var provider = CreateProvider(handler, Options());

        var status = await provider.ProbeAsync(
            ProviderRequest.Probe(Account(), Token(expires: Now.AddMinutes(-1))));

        Assert.Equal(ProviderState.Unauthorized, status.State);
    }

    [Fact]
    public async Task ProbeAsync_PreCanceledTokenThrows()
    {
        using var provider = new GoogleCalendarProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ProbeAsync(ProviderRequest.Probe(Account()), cancellation.Token));
    }

    [Fact]
    public async Task GetEventsAsync_BuildsTheExpectedRequest()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            captured = request;
            return Json("{\"items\":[]}");
        });
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        Assert.True(result.Status.IsReady);
        Assert.Empty(result.Events);
        Assert.NotNull(captured);
        var uri = captured!.RequestUri!;
        Assert.Equal("/calendar/v3/calendars/primary/events", uri.AbsolutePath);

        var query = ParseQuery(uri.Query);
        Assert.Equal(
            EventRange.StartOfDayLocal(From).ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            query["timeMin"]);
        Assert.Equal(
            EventRange.StartOfDayLocal(To).ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            query["timeMax"]);
        Assert.Equal("true", query["singleEvents"]);
        Assert.Equal("startTime", query["orderBy"]);
        Assert.Equal("2500", query["maxResults"]);
        Assert.Equal("UTC", query["timeZone"]);
        Assert.DoesNotContain("pageToken", query.Keys);
        Assert.Equal("Bearer", captured.Headers.Authorization!.Scheme);
        Assert.Equal("access-token", captured.Headers.Authorization.Parameter);
        Assert.Equal("application/json", captured.Headers.Accept.ToString());
        Assert.DoesNotContain("access-token", uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEventsAsync_UsesTheAccountRemoteIdAsTheCalendarId()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(request =>
        {
            captured = request;
            return Json("{\"items\":[]}");
        });
        using var provider = CreateProvider(handler, Options());

        await provider.GetEventsAsync(ProviderRequest.Fetch(
            Account("work@example.com"),
            Range,
            Token(expires: Now.AddHours(1))));

        Assert.NotNull(captured);
        Assert.Contains("work%40example.com", captured!.RequestUri!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEventsAsync_MapsATimedEvent()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("""
            {"items":[{
              "id":"event-1",
              "summary":"Board meeting",
              "start":{"dateTime":"2026-11-10T09:00:00+01:00"},
              "end":{"dateTime":"2026-11-10T10:00:00+01:00"},
              "location":"Room 42",
              "description":"Quarterly notes",
              "hangoutLink":"https://meet.google.com/abc-defg-hij",
              "htmlLink":"https://www.google.com/calendar/event?eid=abc"
            }]}
            """));
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        Assert.True(result.Status.IsReady);
        var providerEvent = Assert.Single(result.Events);
        var calendarEvent = providerEvent.Event;
        Assert.Equal("Board meeting", calendarEvent.Subject);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(1)), calendarEvent.Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.FromHours(1)), calendarEvent.End);
        Assert.False(calendarEvent.IsAllDay);
        Assert.Equal("Room 42", calendarEvent.Location);
        Assert.Equal("Quarterly notes", calendarEvent.Details);
        Assert.Equal("https://meet.google.com/abc-defg-hij", calendarEvent.OnlineMeetingLink);
        Assert.Equal("event-1", providerEvent.RemoteId);
        Assert.Equal("primary", providerEvent.CalendarId);
        Assert.Equal("https://www.google.com/calendar/event?eid=abc", providerEvent.RemoteLink);
        Assert.DoesNotContain("Board meeting", providerEvent.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("meet.google.com", providerEvent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEventsAsync_MapsAnAllDayEvent()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("""
            {"items":[{
              "id":"event-all-day",
              "summary":"Holiday",
              "start":{"date":"2026-11-10"},
              "end":{"date":"2026-11-11"}
            }]}
            """));
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        var calendarEvent = Assert.Single(result.Events).Event;
        Assert.True(calendarEvent.IsAllDay);
        Assert.Equal(EventRange.StartOfDayLocal(new DateOnly(2026, 11, 10)), calendarEvent.Start);
        Assert.Equal(EventRange.StartOfDayLocal(new DateOnly(2026, 11, 11)), calendarEvent.End);
        Assert.Equal(new DateOnly(2026, 11, 10), calendarEvent.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 10), calendarEvent.EndDateInclusive);
    }

    [Fact]
    public async Task GetEventsAsync_MapsConferenceDataWhenHangoutLinkIsAbsent()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("""
            {"items":[{
              "id":"event-2",
              "summary":"Stand-up",
              "start":{"dateTime":"2026-11-10T09:00:00Z"},
              "end":{"dateTime":"2026-11-10T09:15:00Z"},
              "conferenceData":{"entryPoints":[
                {"entryPointType":"phone","uri":"tel:+1-555-0100"},
                {"entryPointType":"video","uri":"https://meet.google.com/xyz-1234-abc"}
              ]}
            }]}
            """));
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        var calendarEvent = Assert.Single(result.Events).Event;
        Assert.Equal("https://meet.google.com/xyz-1234-abc", calendarEvent.OnlineMeetingLink);
    }

    [Fact]
    public async Task GetEventsAsync_SkipsEventsOutsideTheRange()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("""
            {"items":[{
              "id":"event-outside",
              "summary":"Old",
              "start":{"dateTime":"2026-10-01T09:00:00Z"},
              "end":{"dateTime":"2026-10-01T10:00:00Z"}
            }]}
            """));
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        Assert.True(result.Status.IsReady);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_PaginatesWithNextPageToken()
    {
        var requests = new List<Uri>();
        var handler = new FakeHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!);
            var query = ParseQuery(request.RequestUri!.Query);
            return query.ContainsKey("pageToken")
                ? Json("""
                    {"items":[{"id":"event-2","summary":"Second","start":{"date":"2026-11-12"},"end":{"date":"2026-11-13"}}]}
                    """)
                : Json("""
                    {"items":[{"id":"event-1","summary":"First","start":{"date":"2026-11-10"},"end":{"date":"2026-11-11"}}],
                     "nextPageToken":"page-2"}
                    """);
        });
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        Assert.True(result.Status.IsReady);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal("First", result.Events[0].Event.Subject);
        Assert.Equal("Second", result.Events[1].Event.Subject);
        Assert.Equal(2, requests.Count);
        Assert.Equal("page-2", ParseQuery(requests[1].Query)["pageToken"]);
    }

    [Fact]
    public async Task GetEventsAsync_TooManyPagesIsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("""
            {"items":[],"nextPageToken":"page-2"}
            """));
        using var provider = CreateProvider(handler, Options(maxPages: 1));

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(result.Events);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_TooManyEventsIsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("""
            {"items":[
              {"id":"event-1","summary":"One","start":{"date":"2026-11-10"},"end":{"date":"2026-11-11"}},
              {"id":"event-2","summary":"Two","start":{"date":"2026-11-12"},"end":{"date":"2026-11-13"}}
            ]}
            """));
        using var provider = CreateProvider(handler, Options(maxEvents: 1));

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_401RefreshesOnceAndRetriesAndStoresTheNewToken()
    {
        var calendarRequests = 0;
        var tokenRequests = 0;
        string? tokenForm = null;
        var handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            if (IsTokenRequest(request))
            {
                tokenRequests++;
                tokenForm = await request.Content!.ReadAsStringAsync(cancellationToken);
                return Json("{\"access_token\":\"new-access\",\"expires_in\":3600}");
            }

            calendarRequests++;
            if (calendarRequests == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            return Json("""
                {"items":[{"id":"event-1","summary":"After refresh","start":{"dateTime":"2026-11-10T09:00:00Z"},"end":{"dateTime":"2026-11-10T10:00:00Z"}}]}
                """);
        });

        var secrets = new InMemorySecretStore();
        var tokenStore = new OAuthTokenStore(secrets);
        using var provider = CreateProvider(handler, Options(), tokenStore: tokenStore);
        var account = Account();

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(account, Range, Token("old-access", "refresh-token", Now.AddHours(1))));

        Assert.True(result.Status.IsReady);
        Assert.Single(result.Events);
        Assert.Equal("After refresh", result.Events[0].Event.Subject);
        Assert.Equal(2, calendarRequests);
        Assert.Equal(1, tokenRequests);
        Assert.Contains("grant_type=refresh_token", tokenForm);
        Assert.Contains("refresh_token=refresh-token", tokenForm);

        var stored = await tokenStore.GetAsync(account.Id);
        Assert.NotNull(stored);
        Assert.Equal("new-access", stored!.AccessToken);
        Assert.Equal("refresh-token", stored.RefreshToken);
    }

    [Fact]
    public async Task GetEventsAsync_401AgainAfterRefreshIsUnauthorized()
    {
        var calendarRequests = 0;
        var tokenRequests = 0;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (IsTokenRequest(request))
            {
                tokenRequests++;
                return Task.FromResult(Json("{\"access_token\":\"new-access\"}"));
            }

            calendarRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token("old-access", "refresh-token", Now.AddHours(1))));

        Assert.Equal(ProviderState.Unauthorized, result.Status.State);
        Assert.Equal(2, calendarRequests);
        Assert.Equal(1, tokenRequests);
    }

    [Fact]
    public async Task GetEventsAsync_RefreshFailureIsUnauthorized()
    {
        var calendarRequests = 0;
        var tokenRequests = 0;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (IsTokenRequest(request))
            {
                tokenRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"invalid_grant\"}"),
                });
            }

            calendarRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        });
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token("old-access", "refresh-token", Now.AddHours(1))));

        Assert.Equal(ProviderState.Unauthorized, result.Status.State);
        Assert.Equal(1, calendarRequests);
        Assert.Equal(1, tokenRequests);
    }

    [Fact]
    public async Task GetEventsAsync_ExpiredAccessTokenRefreshesBeforeTheFetch()
    {
        string? authorization = null;
        var tokenRequests = 0;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (IsTokenRequest(request))
            {
                tokenRequests++;
                return Task.FromResult(Json("{\"access_token\":\"new-access\",\"expires_in\":3600}"));
            }

            authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(Json("""
                {"items":[{"id":"event-1","summary":"Fresh","start":{"date":"2026-11-10"},"end":{"date":"2026-11-11"}}]}
                """));
        });
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token("old-access", "refresh-token", Now.AddMinutes(-1))));

        Assert.True(result.Status.IsReady);
        Assert.Single(result.Events);
        Assert.Equal(1, tokenRequests);
        Assert.Equal("Bearer new-access", authorization);
    }

    [Fact]
    public async Task GetEventsAsync_WithA401AndNoRefreshTokenIsUnauthorized()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token("access", refresh: null, expires: Now.AddHours(1))));

        Assert.Equal(ProviderState.Unauthorized, result.Status.State);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_UsesTheTokenStoreWhenTheRequestHasNoSecret()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("""
            {"items":[{"id":"event-1","summary":"Stored","start":{"date":"2026-11-10"},"end":{"date":"2026-11-11"}}]}
            """));
        var tokenStore = new OAuthTokenStore(new InMemorySecretStore());
        var account = Account();
        await tokenStore.SaveAsync(account.Id, new OAuthToken
        {
            AccessToken = "stored-access",
            ExpiresAt = Now.AddHours(1),
        });
        using var provider = CreateProvider(handler, Options(), tokenStore: tokenStore);

        var result = await provider.GetEventsAsync(ProviderRequest.Fetch(account, Range));

        Assert.True(result.Status.IsReady);
        Assert.Single(result.Events);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ProviderState.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderState.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderState.Unavailable)]
    [InlineData(HttpStatusCode.NotFound, ProviderState.InvalidResponse)]
    public async Task GetEventsAsync_MapsHttpFailuresToExplicitStates(
        HttpStatusCode statusCode,
        ProviderState expected)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode));
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        Assert.Equal(expected, result.Status.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_NetworkFailureIsUnavailable()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new HttpRequestException("offline"));
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        Assert.Equal(ProviderState.Unavailable, result.Status.State);
    }

    [Fact]
    public async Task GetEventsAsync_MalformedJsonIsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("{not-json"));
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_OversizedResponseIsInvalidResponse()
    {
        var body = "{\"items\":[{\"id\":\"" + new string('a', 4096) + "\"}]}";
        var handler = new FakeHttpMessageHandler(_ => Json(body));
        using var provider = CreateProvider(handler, Options(maxResponseBytes: 1024));

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_RedirectIsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://evil.example/calendar");
            return response;
        });
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
    }

    [Fact]
    public async Task GetEventsAsync_EmptyRangeReturnsEmptyWithoutARequest()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        using var provider = CreateProvider(handler, Options());
        var emptyRange = ProviderEventRange.Days(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10));

        var result = await provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), emptyRange, Token(expires: Now.AddHours(1))));

        Assert.True(result.Status.IsReady);
        Assert.Empty(result.Events);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_WithoutATokenIsNotConfiguredWithoutARequest()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        using var provider = CreateProvider(handler, Options());

        var result = await provider.GetEventsAsync(ProviderRequest.Fetch(Account(), Range));

        Assert.Equal(ProviderState.NotConfigured, result.Status.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_CancellationDuringTheRequestPropagates()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json("{}");
        });
        using var provider = CreateProvider(handler, Options());
        using var cancellation = new CancellationTokenSource();

        var task = provider.GetEventsAsync(
            ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))),
            cancellation.Token);
        await started.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task GetEventsAsync_PreCanceledTokenThrows()
    {
        var handler = new FakeHttpMessageHandler(_ => Json("{}"));
        using var provider = CreateProvider(handler, Options());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetEventsAsync(
                ProviderRequest.Fetch(Account(), Range, Token(expires: Now.AddHours(1))),
                cancellation.Token));
    }

    [Fact]
    public async Task ProviderCatalog_WithGoogleOptions_CreatesAConfiguredProvider()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        var registry = ProviderCatalog.CreateRegistry(handler, googleOptions: Options());

        Assert.True(registry.TryGet(ProviderKind.Google, out var provider));
        var status = await provider!.ProbeAsync(
            ProviderRequest.Probe(Account(), new ProviderSecret { AccessToken = "access-token" }));

        Assert.True(status.IsReady);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(0, 10, 2048)]
    [InlineData(1, 0, 2048)]
    [InlineData(1, 10, 0)]
    public void GoogleOAuthOptions_RejectInvalidFetchLimits(int maxPages, int maxEvents, int maxResponseBytes)
    {
        var options = Options() with
        {
            MaxPages = maxPages,
            MaxEvents = maxEvents,
            MaxResponseBytes = maxResponseBytes,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    private static GoogleOAuthOptions Options(
        int maxPages = GoogleOAuthOptions.DefaultMaxPages,
        int maxEvents = GoogleOAuthOptions.DefaultMaxEvents,
        int maxResponseBytes = GoogleOAuthOptions.DefaultMaxResponseBytes) => new()
    {
        ClientId = "test-client",
        RedirectUri = new Uri("http://127.0.0.1:0/oauth2/callback"),
        CalendarId = "primary",
        Scopes = [GoogleOAuthOptions.CalendarReadonlyScope],
        AuthorizationTimeout = TimeSpan.FromSeconds(10),
        TokenTimeout = TimeSpan.FromSeconds(5),
        RequestTimeout = TimeSpan.FromSeconds(5),
        MaxPages = maxPages,
        MaxEvents = maxEvents,
        MaxResponseBytes = maxResponseBytes,
    };

    private static GoogleCalendarProvider CreateProvider(
        FakeHttpMessageHandler handler,
        GoogleOAuthOptions options,
        IOAuthTokenStore? tokenStore = null) =>
        new(handler, options, tokenStore, new ManualTimeProvider(Now));

    private static ProviderAccount Account(string? remoteId = "primary") =>
        ProviderAccount.Create(ProviderKind.Google, "Google", remoteId: remoteId);

    private static ProviderSecret Token(
        string access = "access-token",
        string? refresh = null,
        DateTimeOffset? expires = null) => new()
    {
        AccessToken = access,
        RefreshToken = refresh,
        ExpiresAt = expires,
        Scope = GoogleOAuthOptions.CalendarReadonlyScope,
    };

    private static bool IsTokenRequest(HttpRequestMessage request) =>
        string.Equals(request.RequestUri?.Host, "oauth2.googleapis.com", StringComparison.Ordinal);

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

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}

