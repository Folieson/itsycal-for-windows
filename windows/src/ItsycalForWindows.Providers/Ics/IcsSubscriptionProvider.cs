using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ItsycalForWindows.Services;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Read-only ICS subscription provider for an HTTPS URL or a local <c>.ics</c> file.
/// </summary>
/// <remarks>
/// <para>
/// The subscription address is secret material: it arrives through
/// <see cref="ProviderRequest.Secret"/> (<see cref="ProviderSecret.Resource"/>) and never from
/// <see cref="ProviderAccount.RemoteId"/>, so a token-bearing URL cannot leak from the account
/// registry. The provider keeps the last good body and <c>ETag</c>/<c>Last-Modified</c> values in
/// memory, keyed by account and a hash of the address; a different address invalidates the old
/// entry and a 304 with no cached body is an explicit non-ready state rather than an empty
/// "ready" result. <see cref="ProviderRequest.ForceRefresh"/> bypasses the validators and the
/// cache.
/// </para>
/// <para>
/// Transport policy (ADR 0005): HTTPS only, no URL user-info, private/loopback hosts only with
/// <see cref="ProviderHttpOptions.AllowPrivateHosts"/>, link-local/cloud-metadata ranges always
/// blocked, at most three redirects validated hop by hop, one timeout budget for the whole
/// attempt, and manual redirect handling. The provider wraps the supplied handler in an
/// <see cref="HttpClient"/> with automatic redirects turned off for a built-in handler and fails
/// closed if a custom handler redirects behind its back. The response body is capped by
/// <see cref="ProviderHttpOptions.MaxResponseBytes"/>.
/// </para>
/// <para>
/// Security: the provider never logs the URL, the feed body, event content or credentials, and
/// never writes them to disk. A malformed feed or a non-ICS response is an explicit
/// <see cref="ProviderState.InvalidResponse"/> result with no events.
/// </para>
/// </remarks>
public sealed class IcsSubscriptionProvider : ICalendarProvider, IProviderCacheInvalidator, IDisposable
{
    /// <summary>Accept header sent to an HTTPS subscription.</summary>
    public const string AcceptHeader = "text/calendar, application/ics, text/plain;q=0.9, */*;q=0.1";

    private const string TimeoutMessage = "The subscription timed out.";
    private const string NoAddressMessage = "No calendar address is stored for this account.";

    private readonly HttpClient _httpClient;
    private readonly ProviderHttpOptions _options;
    private readonly ConcurrentDictionary<FeedKey, CachedFeed> _feeds = new();

    /// <summary>
    /// Creates the provider over an HTTP handler. The provider owns the <see cref="HttpClient"/> it
    /// creates for that handler and turns automatic redirects off on a built-in handler, so this
    /// provider's per-hop redirect validation is authoritative. A custom handler that redirects
    /// internally is detected at runtime and fails closed.
    /// </summary>
    public IcsSubscriptionProvider(HttpMessageHandler handler, ProviderHttpOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        DisableAutomaticRedirects(handler);

        _httpClient = new HttpClient(handler, disposeHandler: false);
        _options = (options ?? new ProviderHttpOptions()).Validate();
    }

    /// <inheritdoc />
    public ProviderKind Kind => ProviderKind.IcsSubscription;

    /// <inheritdoc />
    public string DisplayName => "ICS subscription";

    /// <inheritdoc />
    public Task<ProviderStatus> ProbeAsync(
        ProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetFeedAddress(request, out var address))
        {
            return Task.FromResult(ProviderStatus.NotConfigured(NoAddressMessage));
        }

        if (IcsFeedAddress.TryResolveLocalPath(address, out var path))
        {
            return Task.FromResult(File.Exists(path)
                ? ProviderStatus.Ready(ProviderCapabilities.ReadOnlyEvents)
                : ProviderStatus.NotConfigured("The local calendar file was not found."));
        }

        if (!IcsFeedAddress.TryCreateHttpUri(address, out var uri))
        {
            return Task.FromResult(ProviderStatus.NotConfigured(
                "The calendar address is neither an HTTPS URL nor a local .ics file."));
        }

        var validation = ProviderNetworkPolicy.ValidateSyntactic(uri, _options);
        if (!validation.IsAllowed)
        {
            return Task.FromResult(ProviderStatus.NotConfigured(validation.Message));
        }

        // A probe does no DNS: a host name is validated again before the first request.
        var warning = _options.AllowPrivateHosts
            ? "Self-hosted feeds are enabled. Only add a calendar or server you trust."
            : null;

        return Task.FromResult(ProviderStatus.Ready(
            ProviderCapabilities.ReadOnlyEvents | ProviderCapabilities.IncrementalSync,
            warning));
    }

    /// <inheritdoc />
    public async Task<ProviderEventResult> GetEventsAsync(
        ProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetFeedAddress(request, out var address))
        {
            return ProviderEventResult.Empty(ProviderStatus.NotConfigured(NoAddressMessage));
        }

        if (request.Range.IsEmpty)
        {
            return ProviderEventResult.Empty(ProviderStatus.Ready(message: "The requested range is empty."));
        }

        if (IcsFeedAddress.TryResolveLocalPath(address, out var localPath))
        {
            return ReadLocalFeed(localPath, request.Range, cancellationToken);
        }

        if (!IcsFeedAddress.TryCreateHttpUri(address, out var uri))
        {
            return ProviderEventResult.Empty(ProviderStatus.NotConfigured(
                "The calendar address is neither an HTTPS URL nor a local .ics file."));
        }

        return await FetchHttpFeedAsync(request, uri, address, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void InvalidateCache(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        foreach (var key in _feeds.Keys.Where(key => key.AccountId == accountId).ToArray())
        {
            _feeds.TryRemove(key, out _);
        }
    }

    /// <summary>Disposes the <see cref="HttpClient"/> this provider created; the handler stays owned by the caller.</summary>
    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private ProviderEventResult ReadLocalFeed(
        string path,
        ProviderEventRange range,
        CancellationToken cancellationToken)
    {
#pragma warning disable CA1031 // A missing/corrupt local feed must not take the refresh down.
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return ProviderEventResult.Empty(ProviderStatus.Unavailable("The local calendar file was not found."));
            }

            if (info.Length > _options.MaxResponseBytes)
            {
                return ProviderEventResult.Empty(ProviderStatus.InvalidResponse("The calendar file is too large."));
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.SequentialScan);
            using var memory = new MemoryStream();
            var buffer = new byte[8192];

            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (memory.Length + read > _options.MaxResponseBytes)
                {
                    return ProviderEventResult.Empty(ProviderStatus.InvalidResponse("The calendar file is too large."));
                }

                memory.Write(buffer, 0, read);
            }

            return ParseFeed(Decode(memory.ToArray()), range, notModified: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return ProviderEventResult.Empty(ProviderStatus.InvalidResponse("The local calendar file could not be read."));
        }
#pragma warning restore CA1031
    }

    private async Task<ProviderEventResult> FetchHttpFeedAsync(
        ProviderRequest request,
        Uri initialUri,
        string address,
        CancellationToken cancellationToken)
    {
        var feedKey = new FeedKey(request.Account.Id, HashAddress(address));
        var uri = initialUri;
        var redirectCount = 0;

        // One budget for every redirect hop and the body read, so a chain of slow hops cannot
        // exceed the documented attempt timeout.
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_options.Timeout);

        while (true)
        {
            ProviderUrlValidation validation;
            try
            {
                validation = await ProviderNetworkPolicy
                    .ValidateAsync(uri, _options, budget.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return ProviderEventResult.Empty(ProviderStatus.Unavailable(TimeoutMessage));
            }

            if (!validation.IsAllowed)
            {
                return ProviderEventResult.Empty(new ProviderStatus(
                    validation.State,
                    ProviderCapabilities.None,
                    validation.Message));
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
            httpRequest.Headers.Accept.ParseAdd(AcceptHeader);
            httpRequest.Headers.UserAgent.ParseAdd(_options.UserAgent);

            if (redirectCount == 0 &&
                !request.ForceRefresh &&
                _feeds.TryGetValue(feedKey, out var cachedFeed))
            {
                if (!string.IsNullOrEmpty(cachedFeed.ETag))
                {
                    httpRequest.Headers.TryAddWithoutValidation("If-None-Match", cachedFeed.ETag);
                }

                if (cachedFeed.LastModified is { } lastModified)
                {
                    httpRequest.Headers.IfModifiedSince = lastModified;
                }
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient
                    .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, budget.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return ProviderEventResult.Empty(ProviderStatus.Unavailable(TimeoutMessage));
            }
            catch (HttpRequestException)
            {
                return ProviderEventResult.Empty(
                    ProviderStatus.Unavailable("The subscription could not be reached."));
            }

            using (response)
            {
                if (WasAutomaticallyRedirected(response, uri))
                {
                    return ProviderEventResult.Empty(ProviderStatus.InvalidResponse(
                        "The HTTP handler followed a redirect automatically; every redirect hop must be validated."));
                }

                var statusCode = (int)response.StatusCode;

                if (statusCode == 304)
                {
                    if (!request.ForceRefresh &&
                        _feeds.TryGetValue(feedKey, out var cached) &&
                        cached.Text.Length > 0)
                    {
                        return ParseFeed(cached.Text, request.Range, notModified: true);
                    }

                    return ProviderEventResult.Empty(ProviderStatus.InvalidResponse(
                        "The subscription reported 'not modified' but no cached copy is available."));
                }

                if (statusCode is >= 300 and < 400)
                {
                    if (redirectCount >= _options.MaxRedirects)
                    {
                        return ProviderEventResult.Empty(
                            ProviderStatus.InvalidResponse("The subscription redirected too many times."));
                    }

                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        return ProviderEventResult.Empty(
                            ProviderStatus.InvalidResponse("The subscription redirected without a location."));
                    }

                    Uri next;
                    try
                    {
                        next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    }
                    catch (UriFormatException)
                    {
                        return ProviderEventResult.Empty(
                            ProviderStatus.InvalidResponse("The subscription redirected to an invalid address."));
                    }

                    // The full network policy (scheme, credentials, DNS and blocked ranges) is
                    // re-applied at the top of the loop for every hop.
                    if (next.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(next.UserInfo))
                    {
                        return ProviderEventResult.Empty(
                            ProviderStatus.InvalidResponse("The subscription redirected to an unsupported address."));
                    }

                    uri = next;
                    redirectCount++;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                        ? ProviderEventResult.Empty(ProviderStatus.Unauthorized("The subscription requires authorization."))
                        : ProviderEventResult.Empty(
                            ProviderStatus.Unavailable($"The subscription returned HTTP {statusCode}."));
                }

                if (response.Content.Headers.ContentLength is { } contentLength &&
                    contentLength > _options.MaxResponseBytes)
                {
                    return ProviderEventResult.Empty(ProviderStatus.InvalidResponse("The subscription is too large."));
                }

                byte[]? bytes;
                try
                {
                    bytes = await ReadAtMostAsync(response.Content, _options.MaxResponseBytes, budget.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // A caller cancellation must not be converted into an "unavailable" status by
                    // the general handler below. This guard is what keeps the contract honest
                    // while a response body is streaming.
                    throw;
                }
                catch (OperationCanceledException)
                {
                    return ProviderEventResult.Empty(ProviderStatus.Unavailable(TimeoutMessage));
                }
#pragma warning disable CA1031 // Any body-read failure is an explicit unavailable state.
                catch (Exception)
                {
                    return ProviderEventResult.Empty(ProviderStatus.Unavailable("The subscription could not be read."));
                }
#pragma warning restore CA1031

                if (bytes is null)
                {
                    return ProviderEventResult.Empty(ProviderStatus.InvalidResponse("The subscription is too large."));
                }

                var text = Decode(bytes);
                if (!LooksLikeIcs(text))
                {
                    return ProviderEventResult.Empty(
                        ProviderStatus.InvalidResponse("The subscription is not an iCalendar feed."));
                }

                RememberFeed(feedKey, new CachedFeed(
                    text,
                    response.Headers.ETag?.ToString(),
                    response.Content.Headers.LastModified));

                return ParseFeed(text, request.Range, notModified: false);
            }
        }
    }

    private void RememberFeed(FeedKey feedKey, CachedFeed feed)
    {
        _feeds[feedKey] = feed;

        // A changed address must not keep serving the previous feed's validators or body.
        foreach (var stale in _feeds.Keys
            .Where(key => key.AccountId == feedKey.AccountId && key != feedKey)
            .ToArray())
        {
            _feeds.TryRemove(stale, out _);
        }
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

    private static ProviderEventResult ParseFeed(
        string feedText,
        ProviderEventRange range,
        bool notModified)
    {
        if (!LooksLikeIcs(feedText))
        {
            return ProviderEventResult.Empty(
                ProviderStatus.InvalidResponse("The resource is not an iCalendar feed."));
        }

        var events = IcsParser.Parse(feedText, range.FromInclusive, range.ToExclusive)
            .Select(static calendarEvent => new ProviderEvent(calendarEvent))
            .ToArray();

        return ProviderEventResult.Ready(events, notModified);
    }

    private static string Decode(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    private static bool LooksLikeIcs(string text)
    {
        var trimmed = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return trimmed.StartsWith("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetFeedAddress(ProviderRequest request, out string address)
    {
        address = request.Secret?.Resource?.Trim() ?? string.Empty;
        return address.Length > 0;
    }

    private static string HashAddress(string address) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address)));

    private static bool WasAutomaticallyRedirected(HttpResponseMessage response, Uri requestedUri)
    {
        var actual = response.RequestMessage?.RequestUri;
        return actual is not null &&
            !string.Equals(actual.AbsoluteUri, requestedUri.AbsoluteUri, StringComparison.Ordinal);
    }

    private static void DisableAutomaticRedirects(HttpMessageHandler handler)
    {
        switch (handler)
        {
            case HttpClientHandler httpClientHandler:
                httpClientHandler.AllowAutoRedirect = false;
                break;
            case SocketsHttpHandler socketsHttpHandler:
                socketsHttpHandler.AllowAutoRedirect = false;
                break;
        }
    }

    private readonly record struct FeedKey(string AccountId, string AddressHash);

    private sealed record CachedFeed(string Text, string? ETag, DateTimeOffset? LastModified);
}
