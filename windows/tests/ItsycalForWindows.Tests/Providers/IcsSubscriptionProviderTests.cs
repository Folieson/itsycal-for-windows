using System;
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
/// Tests for the read-only <see cref="IcsSubscriptionProvider"/>: conditional HTTP fetches,
/// redirects, size/timeout limits, non-ICS and malformed responses, local files and URL safety.
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
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => IcsResponse(ics)));
        var provider = new IcsSubscriptionProvider(client);
        var account = HttpAccount();

        var result = await provider.GetEventsAsync(account, Range);

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

        using var client = new HttpClient(handler);
        var provider = new IcsSubscriptionProvider(client);
        var account = HttpAccount();

        var first = await provider.GetEventsAsync(account, Range);
        var second = await provider.GetEventsAsync(account, Range);

        Assert.False(first.NotModified);
        Assert.True(second.NotModified);
        Assert.Single(second.Events);
        Assert.Equal("Cached", second.Events[0].Event.Subject);
        Assert.Equal(2, requestCount);
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

        using var client = new HttpClient(handler);
        var provider = new IcsSubscriptionProvider(client);
        var account = HttpAccount();

        var result = await provider.GetEventsAsync(account, Range);

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

        using var client = new HttpClient(handler);
        var provider = new IcsSubscriptionProvider(client, new ProviderHttpOptions { MaxRedirects = 1 });
        var account = HttpAccount();

        var result = await provider.GetEventsAsync(account, Range);

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(result.Events);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task GetEventsAsync_OversizedResponseIsRejected()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 4096), Encoding.UTF8, "text/calendar"),
        });

        using var client = new HttpClient(handler);
        var provider = new IcsSubscriptionProvider(client, new ProviderHttpOptions { MaxResponseBytes = 1024 });
        var account = HttpAccount();

        var result = await provider.GetEventsAsync(account, Range);

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(result.Events);
        Assert.Contains("large", result.Status.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEventsAsync_NonIcsResponseIsRejected()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>not a calendar</body></html>", Encoding.UTF8, "text/html"),
        });

        using var client = new HttpClient(handler);
        var provider = new IcsSubscriptionProvider(client);

        var result = await provider.GetEventsAsync(HttpAccount(), Range);

        Assert.Equal(ProviderState.InvalidResponse, result.Status.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_MalformedIcsReturnsNoEventsWithoutThrowing()
    {
        const string malformed = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nTHIS IS NOT A PROPERTY\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => IcsResponse(malformed)));
        var provider = new IcsSubscriptionProvider(client);

        var result = await provider.GetEventsAsync(HttpAccount(), Range);

        Assert.True(result.Status.IsReady);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task GetEventsAsync_TimeoutIsReportedWithoutThrowing()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return IcsResponse(IcsTestData.Event("DTSTART:20261110T090000Z", "SUMMARY:Never"));
        });

        using var client = new HttpClient(handler);
        var provider = new IcsSubscriptionProvider(
            client,
            new ProviderHttpOptions { Timeout = TimeSpan.FromMilliseconds(100) });

        var result = await provider.GetEventsAsync(HttpAccount(), Range);

        Assert.Equal(ProviderState.Unavailable, result.Status.State);
        Assert.Contains("timed out", result.Status.Message!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetEventsAsync_CredentialsInUrlAreRejectedWithoutARequest()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not send"));
        using var client = new HttpClient(handler);
        var provider = new IcsSubscriptionProvider(client);
        var account = ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            "Feed",
            remoteId: "https://user:password@example.com/feed.ics");

        var result = await provider.GetEventsAsync(account, Range);

        Assert.Equal(ProviderState.NotConfigured, result.Status.State);
        Assert.Empty(result.Events);
        Assert.Empty(handler.Requests);
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
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP")));
        var provider = new IcsSubscriptionProvider(client);
        var account = ProviderAccount.Create(ProviderKind.IcsSubscription, "Local", remoteId: path);

        var inside = await provider.GetEventsAsync(account, ProviderEventRange.Days(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11)));
        var later = await provider.GetEventsAsync(account, ProviderEventRange.Days(new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 25)));

        Assert.Single(inside.Events);
        Assert.Equal("Inside", inside.Events[0].Event.Subject);
        Assert.Single(later.Events);
        Assert.Equal("Later", later.Events[0].Event.Subject);
    }

    [Fact]
    public async Task GetEventsAsync_MissingLocalFileIsUnavailable()
    {
        using var root = new ProviderTestRoot();
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP")));
        var provider = new IcsSubscriptionProvider(client);
        var account = ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            "Missing",
            remoteId: root.Combine("missing.ics"));

        var result = await provider.GetEventsAsync(account, Range);

        Assert.Equal(ProviderState.Unavailable, result.Status.State);
        Assert.Empty(result.Events);
    }

    [Fact]
    public async Task ProbeAsync_ValidHttpUrlIsReadyWithoutNetwork()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not send"));
        using var client = new HttpClient(handler);
        var provider = new IcsSubscriptionProvider(client);

        var status = await provider.ProbeAsync(HttpAccount());

        Assert.True(status.IsReady);
        Assert.True(status.Capabilities.HasFlag(ProviderCapabilities.IncrementalSync));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ProbeAsync_MissingLocalFileIsNotConfigured()
    {
        using var root = new ProviderTestRoot();
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP")));
        var provider = new IcsSubscriptionProvider(client);
        var account = ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            "Missing",
            remoteId: root.Combine("missing.ics"));

        var status = await provider.ProbeAsync(account);

        Assert.Equal(ProviderState.NotConfigured, status.State);
    }

    [Fact]
    public async Task GetEventsAsync_EmptyRangeReturnsEmptyWithoutRequest()
    {
        var handler = new FakeHttpMessageHandler(_ => throw new InvalidOperationException("must not send"));
        using var client = new HttpClient(handler);
        var provider = new IcsSubscriptionProvider(client);

        var result = await provider.GetEventsAsync(HttpAccount(), ProviderEventRange.Days(From, From));

        Assert.True(result.Status.IsReady);
        Assert.Empty(result.Events);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetEventsAsync_PreCanceledTokenThrows()
    {
        using var client = new HttpClient(new FakeHttpMessageHandler(_ => throw new InvalidOperationException("no HTTP")));
        var provider = new IcsSubscriptionProvider(client);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetEventsAsync(HttpAccount(), Range, cancellation.Token));
    }

    [Theory]
    [InlineData(0, 2048, 5)]
    [InlineData(1000, 0, 5)]
    [InlineData(1000, 2048, 21)]
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
        Assert.Equal(ProviderHttpOptions.DefaultMaxRedirects, options.MaxRedirects);
        Assert.True(options.Timeout > TimeSpan.Zero);
    }

    private static ProviderAccount HttpAccount() =>
        ProviderAccount.Create(ProviderKind.IcsSubscription, "Feed", remoteId: "https://example.com/feed.ics");

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
}
