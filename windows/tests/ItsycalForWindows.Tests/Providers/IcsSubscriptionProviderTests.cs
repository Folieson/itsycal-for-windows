using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for the read-only <see cref="IcsSubscriptionProvider"/>: secret-based addresses,
/// conditional HTTP fetches, the HTTPS/private-host policy, redirects, size/timeout limits,
/// non-ICS and malformed responses, cancellation, local files and cache invalidation.
/// </summary>
public sealed class IcsSubscriptionProviderTests
{
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 12, 1);
    private static readonly ProviderEventRange Range = ProviderEventRange.Days(From, To);

    [Fact]
    public async Task GetEventsAsync_Http200ParsesEvents()
    {
        var ics = IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Remote event");
        var handler = new FakeHttpMessageHandler(_ => IcsResponse(ics));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.True(result.Status.IsReady);
        Assert.False(result.NotModified);
        Assert.Single(result.Events);
        Assert.Equal("Remote event", result.Events[0].Event.Subject);
    }

    [Fact]
    public async Task GetEventsAsync_Http304ReturnsCachedEventsAndSendsValidators()
    {
        var ics = IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Cached");
        var lastModified = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        var requestCount = 0;

        var handler = new FakeHttpMessageHandler(request =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                return IcsResponse(ics, etag: "\"v1\"", lastModified: lastModified);
            }

            Assert.Equal("\"v1\"", request.Headers.GetValues("If-None-Match").Single());
            Assert.Equal(lastModified, request.Headers.IfModifiedSince);
            return new HttpResponseMessage(HttpStatusCode.NotModified);
        });

        var provider = new IcsSubscriptionProvider(handler, PublicOptions());
        var request = FetchRequest("https://example.com/feed.ics");

        var first = await provider.GetEventsAsync(request);
        var second = await provider.GetEventsAsync(request);

        Assert.False(first.NotModified);
        Assert.True(second.NotModified);
        Assert.Single(second.Events);
        Assert.Equal("Cached", second.Events[0].Event.Subject);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task GetEventsAsync_Http304WithoutCachedBodyIsNotReady()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.False(result.Status.IsReady);
        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_FeedCacheIsKeyedByUrlAndDropsChangedAddressValidators()
    {
        var firstIcs = IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:First");
        var secondIcs = IcsTestData.Event("DTSTART:20261111T090000Z", "SUMMARY:Second");
        var requestCount = 0;

        var handler = new FakeHttpMessageHandler(request =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                return IcsResponse(firstIcs, etag: "\"first\"");
            }

            Assert.False(request.Headers.Contains("If-None-Match"));
            return IcsResponse(secondIcs, etag: "\"second\"");
        });

        var provider = new IcsSubscriptionProvider(handler, PublicOptions());
        var account = ProviderAccount.Create(ProviderKind.IcsSubscription, "Feed", id: "stable", remoteId: "https://example.com");
        var firstRequest = ProviderRequest.Fetch(
            account,
            Range,
            IcsFeedAddress.CreateSecret("https://example.com/first.ics"));
        var secondRequest = ProviderRequest.Fetch(
            account,
            Range,
            IcsFeedAddress.CreateSecret("https://example.com/second.ics"));

        var first = await provider.GetEventsAsync(firstRequest);
        var second = await provider.GetEventsAsync(secondRequest);

        Assert.Equal("First", first.Events.Single().Event.Subject);
        Assert.Equal("Second", second.Events.Single().Event.Subject);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task GetEventsAsync_ForceRefreshSkipsCachedValidators()
    {
        var ics = IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Fresh");
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.Headers.Contains("If-None-Match"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotModified);
            }

            return IcsResponse(ics, etag: "\"v1\"");
        });

        var provider = new IcsSubscriptionProvider(handler, PublicOptions());
        var account = ProviderAccount.Create(ProviderKind.IcsSubscription, "Feed", id: "stable", remoteId: "https://example.com");
        var secret = IcsFeedAddress.CreateSecret("https://example.com/feed.ics");

        await provider.GetEventsAsync(ProviderRequest.Fetch(account, Range, secret));
        var forced = await provider.GetEventsAsync(ProviderRequest.Fetch(account, Range, secret, forceRefresh: true));

        Assert.True(forced.Status.IsReady);
        Assert.False(forced.NotModified);
        Assert.Single(forced.Events);
    }

    [Fact]
    public async Task InvalidateCache_DropsStoredValidators()
    {
        var ics = IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Again");
        var handler = new FakeHttpMessageHandler(request =>
        {
            Assert.False(request.Headers.Contains("If-None-Match"));
            return IcsResponse(ics, etag: "\"v1\"");
        });

        var provider = new IcsSubscriptionProvider(handler, PublicOptions());
        var request = FetchRequest("https://example.com/feed.ics");

        await provider.GetEventsAsync(request);
        provider.InvalidateCache(request.Account.Id);
        var result = await provider.GetEventsAsync(request);

        Assert.Single(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_FollowsRedirectAndParsesTheTarget()
    {
        var ics = IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Redirected");
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.Host == "example.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://cdn.example.com/feed.ics");
                return redirect;
            }

            return IcsResponse(ics);
        });

        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.True(result.Status.IsReady);
        Assert.Single(result.Events);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("cdn.example.com", handler.Requests[1].RequestUri!.Host);
    }

    [Fact]
    public async Task GetEventsAsync_TooManyRedirectsIsInvalidResponse()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://example.com/loop.ics");
            return redirect;
        });

        var provider = new IcsSubscriptionProvider(handler, PublicOptions() with { MaxRedirects = 1 });

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(result.Events);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetEventsAsync_DefaultRedirectCapIsThree()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://example.com/loop.ics");
            return redirect;
        });

        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Equal(ProviderHttpOptions.MaxAllowedRedirects + 1, handler.Requests.Count);
    }

    [Fact]
    public async Task GetEventsAsync_RedirectDowngradeToHttpIsRejectedPerHop()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("http://example.com/feed.ics");
            return redirect;
        });

        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_RedirectToLinkLocalMetadataIsBlocked()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("https://169.254.169.254/latest/meta-data");
            return redirect;
        });

        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_TotalTimeoutBudgetCoversEveryRedirectHop()
    {
        var ics = IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Slow");
        var handler = new FakeHttpMessageHandler(async (request, cancellationToken) =>
        {
            await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            if (request.RequestUri!.Host == "example.com")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://cdn.example.com/feed.ics");
                return redirect;
            }

            return IcsResponse(ics);
        });

        var provider = new IcsSubscriptionProvider(
            handler,            PublicOptions() with { Timeout = TimeSpan.FromMilliseconds(100) });

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.Equal(ProviderState.Unavailable, result.Status.State);
        Assert.Contains("timed out", result.Status.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEventsAsync_OversizedResponseIsRejected()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 4096), Encoding.UTF8, "text/calendar"),
        });

        var provider = new IcsSubscriptionProvider(
            handler,            PublicOptions() with { MaxResponseBytes = 1024 });

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(result.Events);
        Assert.Contains("large", result.Status.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEventsAsync_StreamingResponseCapIsEnforcedWithoutContentLength()
    {
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 4096))));
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
        });

        var provider = new IcsSubscriptionProvider(
            handler,            PublicOptions() with { MaxResponseBytes = 1024 });

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Contains("large", result.Status.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEventsAsync_NonIcsResponseIsRejected()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>not a calendar</body></html>", Encoding.UTF8, "text/html"),
        });

        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_MalformedIcsReturnsNoEventsWithoutThrowing()
    {
        const string malformed = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nTHIS IS NOT A PROPERTY\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        var handler = new FakeHttpMessageHandler(_ => IcsResponse(malformed));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.True(result.Status.IsReady);
        Assert.Empty(result.Events);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderState.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ProviderState.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, ProviderState.Unavailable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ProviderState.Unavailable)]
    public async Task GetEventsAsync_NonSuccessStatusIsAnExplicitState(
        HttpStatusCode statusCode,
        ProviderState expectedState)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(statusCode));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.Equal(expectedState, result.Status.State);
        Assert.Empty(result.Events);
        Assert.DoesNotContain("example.com", result.Status.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEventsAsync_TimeoutIsReportedWithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return IcsResponse(IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Never"));
        });

        var provider = new IcsSubscriptionProvider(
            handler,            PublicOptions() with { Timeout = TimeSpan.FromMilliseconds(100) });

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.Equal(ProviderState.Unavailable, result.Status.State);
        Assert.Contains("timed out", result.Status.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEventsAsync_CallerCancellationDuringBodyReadPropagates()
    {
        var content = new BlockingStreamContent();
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
        });

        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        using var cancellation = new CancellationTokenSource();
        var task = provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"), cancellation.Token);

        await content.Stream.ReadStarted.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task GetEventsAsync_HttpIsRejectedWithoutARequest()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not send"));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());
        var account = ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            "Feed",
            remoteId: "https://example.com");
        var request = ProviderRequest.Fetch(
            account,
            Range,
            new ProviderSecret { Resource = "http://example.com/feed.ics" });

        var result = await provider.GetEventsAsync(request);

        Assert.Equal(ProviderState.NotConfigured, result.Status.State);
        Assert.Empty(result.Events);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_CredentialsInUrlAreRejectedWithoutARequest()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not send"));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());
        var account = ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            "Feed",
            remoteId: "https://example.com");
        var request = ProviderRequest.Fetch(
            account,
            Range,
            new ProviderSecret { Resource = "https://user:password@example.com/feed.ics" });

        var result = await provider.GetEventsAsync(request);

        Assert.Equal(ProviderState.NotConfigured, result.Status.State);
        Assert.Empty(result.Events);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_LoopbackIsRejectedWithoutExplicitOptIn()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not send"));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest("https://127.0.0.1/feed.ics"));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(handler.Requests);
        Assert.DoesNotContain("127.0.0.1", result.Status.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetEventsAsync_PrivateDnsResolutionIsRejectedWithoutExplicitOptIn()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not send"));
        var provider = new IcsSubscriptionProvider(
            handler,            PublicOptions() with { HostResolver = ResolveTo("10.0.0.5") });

        var result = await provider.GetEventsAsync(FetchRequest("https://calendar.internal/feed.ics"));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_LinkLocalAndMetadataAreBlockedEvenWithPrivateOptIn()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not send"));
        var provider = new IcsSubscriptionProvider(
            handler,            PublicOptions() with { AllowPrivateHosts = true });

        var result = await provider.GetEventsAsync(FetchRequest("https://169.254.169.254/latest/meta-data"));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_PrivateHostIsAllowedWithExplicitOptIn()
    {
        var ics = IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Self-hosted");
        var handler = new FakeHttpMessageHandler(_ => IcsResponse(ics));
        var provider = new IcsSubscriptionProvider(
            handler,            PublicOptions() with { AllowPrivateHosts = true });

        var result = await provider.GetEventsAsync(FetchRequest("https://192.168.1.10/feed.ics"));

        Assert.True(result.Status.IsReady);
        Assert.Single(result.Events);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Constructor_DisablesAutomaticRedirectsOnHttpClientHandler()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = true };

        _ = new IcsSubscriptionProvider(handler, PublicOptions());

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public void Constructor_DisablesAutomaticRedirectsOnSocketsHttpHandler()
    {
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = true };

        _ = new IcsSubscriptionProvider(handler, PublicOptions());

        Assert.False(handler.AllowAutoRedirect);
    }

    [Fact]
    public async Task GetEventsAsync_DetectsAHiddenAutomaticRedirect()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Unexpected"),
                Encoding.UTF8,
                "text/calendar"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://other.example/feed.ics"),
        });

        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Contains("redirect", result.Status.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEventsAsync_LocalFileParsesAndFiltersTheRange()
    {
        using var root = new ProviderTestRoot();
        var path = root.Write("feed.ics", IcsTestData.CalendarWithEvents(
        [
            ["DTSTART;VALUE=DATE:20261110", "SUMMARY:Inside"],
            ["DTSTART;VALUE=DATE:20261120", "SUMMARY:Later"],
        ]));
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());
        var request = FetchRequest(path);

        var inside = await provider.GetEventsAsync(
            request with { Range = ProviderEventRange.Days(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11)) });
        var later = await provider.GetEventsAsync(
            request with { Range = ProviderEventRange.Days(new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 25)) });

        Assert.Single(inside.Events);
        Assert.Equal("Inside", inside.Events[0].Event.Subject);
        Assert.Single(later.Events);
        Assert.Equal("Later", later.Events[0].Event.Subject);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_FileUriIsAccepted()
    {
        using var root = new ProviderTestRoot();
        var path = root.Write("feed.ics", IcsTestData.Event("DTSTART;VALUE=DATE:20261110", "SUMMARY:FileUri"));
        var fileUri = new Uri(path).AbsoluteUri;
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest(fileUri));

        Assert.True(result.Status.IsReady);
        Assert.Single(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_RelativeLocalPathIsRejected()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());
        var account = ProviderAccount.Create(ProviderKind.IcsSubscription, "Relative", remoteId: "file");
        var request = ProviderRequest.Fetch(account, Range, new ProviderSecret { Resource = "feed.ics" });

        var result = await provider.GetEventsAsync(request);

        Assert.Equal(ProviderState.NotConfigured, result.Status.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_MissingLocalFileIsUnavailable()
    {
        using var root = new ProviderTestRoot();
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var result = await provider.GetEventsAsync(FetchRequest(root.Combine("missing.ics")));

        Assert.Equal(ProviderState.Unavailable, result.Status.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_OversizedLocalFileIsRejected()
    {
        using var root = new ProviderTestRoot();
        var path = root.Write("big.ics", new string('x', 4096));
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        var provider = new IcsSubscriptionProvider(
            handler,            PublicOptions() with { MaxResponseBytes = 1024 });

        var result = await provider.GetEventsAsync(FetchRequest(path));

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Contains("large", result.Status.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEventsAsync_EmptyRangeReturnsEmptyWithoutRequest()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not send"));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());
        var request = FetchRequest("https://example.com/feed.ics");

        var result = await provider.GetEventsAsync(
            request with { Range = ProviderEventRange.Days(From, From) });

        Assert.True(result.Status.IsReady);
        Assert.Empty(result.Events);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_PreCanceledTokenThrows()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetEventsAsync(FetchRequest("https://example.com/feed.ics"), cancellation.Token));
    }

    [Fact]
    public async Task ProbeAsync_ValidHttpsUrlIsReadyWithoutNetwork()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not send"));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var status = await provider.ProbeAsync(ProbeRequest("https://example.com/feed.ics"));

        Assert.True(status.IsReady);
        Assert.True(status.Capabilities.HasFlag(ProviderCapabilities.IncrementalSync));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ProbeAsync_MissingSecretIsNotConfigured()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());
        var account = ProviderAccount.Create(ProviderKind.IcsSubscription, "Feed", remoteId: "https://example.com");

        var status = await provider.ProbeAsync(ProviderRequest.Probe(account));

        Assert.Equal(ProviderState.NotConfigured, status.State);
    }

    [Fact]
    public async Task ProbeAsync_MissingLocalFileIsNotConfigured()
    {
        using var root = new ProviderTestRoot();
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        var provider = new IcsSubscriptionProvider(handler, PublicOptions());

        var status = await provider.ProbeAsync(ProbeRequest(root.Combine("missing.ics")));

        Assert.Equal(ProviderState.NotConfigured, status.State);
    }

    [Fact]
    public async Task ProbeAsync_PrivateOptInShowsAWarning()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP"));
        var provider = new IcsSubscriptionProvider(
            handler,            PublicOptions() with { AllowPrivateHosts = true });

        var status = await provider.ProbeAsync(ProbeRequest("https://192.168.1.10/feed.ics"));

        Assert.True(status.IsReady);
        Assert.Contains("trust", status.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, 2048, 0)]
    [InlineData(1000, 0, 0)]
    [InlineData(1000, 2048, 4)]
    [InlineData(1000, 2048, -1)]
    public void ProviderHttpOptions_RejectsInvalidLimits(int timeoutMilliseconds, int maxResponseBytes, int maxRedirects)
    {
        var options = new ProviderHttpOptions
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds),
            MaxResponseBytes = maxResponseBytes,
            MaxRedirects = maxRedirects,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void ProviderHttpOptions_DefaultsAreValid()
    {
        var options = new ProviderHttpOptions().Validate();

        Assert.Equal(ProviderHttpOptions.DefaultMaxResponseBytes, options.MaxResponseBytes);
        Assert.Equal(3, options.MaxRedirects);
        Assert.False(options.AllowPrivateHosts);
        Assert.True(options.Timeout > TimeSpan.Zero);
    }

    private static ProviderHttpOptions PublicOptions() =>
        new() { HostResolver = ResolveTo("93.184.216.34") };

    private static ProviderHostResolver ResolveTo(params string[] addresses) =>
        (_, _) => Task.FromResult<IReadOnlyList<IPAddress>>(
            addresses.Select(IPAddress.Parse).ToArray());

    private static ProviderRequest FetchRequest(string address) =>
        ProviderRequest.Fetch(AccountFor(address), Range, SecretFor(address));

    private static ProviderRequest ProbeRequest(string address) =>
        ProviderRequest.Probe(AccountFor(address), SecretFor(address));

    private static ProviderAccount AccountFor(string address)
    {
        if (IcsFeedAddress.TryResolveLocalPath(address, out _))
        {
            return ProviderAccount.Create(
                ProviderKind.IcsSubscription,
                "Local",
                remoteId: IcsFeedAddress.LocalFileRemoteId);
        }

        return ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            "Feed",
            remoteId: IcsFeedAddress.ToRemoteId(address));
    }

    private static ProviderSecret SecretFor(string address)
    {
        IcsFeedAddress.ToRemoteId(address);
        return new ProviderSecret { Resource = address };
    }

    private static HttpResponseMessage IcsResponse(
        string ics,
        string? etag = null,
        DateTimeOffset? lastModified = null)
    {
        var content = new StringContent(ics, Encoding.UTF8, "text/calendar");
        if (lastModified is { } value)
        {
            content.Headers.LastModified = value;
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        if (etag is not null)
        {
            response.Headers.ETag = new EntityTagHeaderValue(etag);
        }

        return response;
    }

    /// <summary>Response content whose stream blocks until the caller cancels.</summary>
    private sealed class BlockingStreamContent : HttpContent
    {
        public BlockingStream Stream { get; } = new();

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(Stream);
    }

    private sealed class BlockingStream : Stream
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _started.Task;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
