using System.Collections.Concurrent;
using System.Net;
using System.Text;
using ItsycalForWindows.Services;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Read-only ICS subscription provider for an HTTP(S) URL or a local <c>.ics</c> file.
/// </summary>
/// <remarks>
/// <para>
/// HTTP fetches use conditional requests: the previous <c>ETag</c> and <c>Last-Modified</c> values
/// are kept in memory per account and sent as <c>If-None-Match</c>/<c>If-Modified-Since</c>; a 304
/// re-parses the last good body for the requested range and reports
/// <see cref="ProviderEventResult.NotModified"/>. The body is capped by
/// <see cref="ProviderHttpOptions.MaxResponseBytes"/>, the attempt by
/// <see cref="ProviderHttpOptions.Timeout"/>, and redirects are followed manually up to
/// <see cref="ProviderHttpOptions.MaxRedirects"/> so every hop is validated.
/// </para>
/// <para>
/// Security: only absolute <c>http</c>/<c>https</c> URLs are accepted; URLs with embedded
/// user-info are rejected so credentials cannot enter a URL, a log or an error message. The feed
/// address is user-configured, so private/loopback destinations are allowed for a local feed; the
/// provider does not read arbitrary local paths beyond the configured <c>.ics</c> file. It never
/// logs the URL, the feed body, event content or credentials. A malformed feed or a non-ICS
/// response is an explicit <see cref="ProviderState.InvalidResponse"/> result with no events.
/// </para>
/// <para>
/// The injected <see cref="HttpClient"/> should use a handler with automatic redirects disabled
/// (<c>AllowAutoRedirect = false</c>) so this provider's redirect policy is authoritative; the
/// tests use a fake handler. If the handler follows redirects itself, the final response is still
/// parsed and capped, only the manual redirect count does not apply.
/// </para>
/// </remarks>
public sealed class IcsSubscriptionProvider : ICalendarProvider
{
    /// <summary>Accept header sent to an HTTP(S) subscription.</summary>
    public const string AcceptHeader = "text/calendar, application/ics, text/plain;q=0.9, */*;q=0.1";

    private readonly HttpClient _httpClient;
    private readonly ProviderHttpOptions _options;
    private readonly ConcurrentDictionary<string, CachedFeed> _feeds = new(StringComparer.Ordinal);

    /// <summary>Creates the provider. <paramref name="httpClient"/> is owned by the caller.</summary>
    public IcsSubscriptionProvider(HttpClient httpClient, ProviderHttpOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _options = (options ?? new ProviderHttpOptions()).Validate();
    }

    /// <inheritdoc />
    public ProviderKind Kind => ProviderKind.IcsSubscription;

    /// <inheritdoc />
    public string DisplayName => "ICS subscription";

    /// <inheritdoc />
    public Task<ProviderStatus> ProbeAsync(
        ProviderAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(account.RemoteId))
        {
            return Task.FromResult(ProviderStatus.NotConfigured("No calendar address is configured."));
        }

        if (TryResolveLocalPath(account.RemoteId, out var path))
        {
            return Task.FromResult(File.Exists(path)
                ? ProviderStatus.Ready(ProviderCapabilities.ReadOnlyEvents)
                : ProviderStatus.NotConfigured("The local calendar file was not found."));
        }

        if (TryCreateHttpUri(account.RemoteId, out _))
        {
            return Task.FromResult(ProviderStatus.Ready(
                ProviderCapabilities.ReadOnlyEvents | ProviderCapabilities.IncrementalSync));
        }

        return Task.FromResult(ProviderStatus.NotConfigured(
            "The calendar address is neither an http(s) URL nor a local .ics file."));
    }

    /// <inheritdoc />
    public async Task<ProviderEventResult> GetEventsAsync(
        ProviderAccount account,
        ProviderEventRange range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(account.RemoteId))
        {
            return ProviderEventResult.Empty(ProviderStatus.NotConfigured("No calendar address is configured."));
        }

        if (range.IsEmpty)
        {
            return ProviderEventResult.Ready([]);
        }

        if (TryResolveLocalPath(account.RemoteId, out var path))
        {
            return ReadLocalFeed(path, range, cancellationToken);
        }

        if (TryCreateHttpUri(account.RemoteId, out var uri))
        {
            return await FetchHttpFeedAsync(account, uri, range, cancellationToken).ConfigureAwait(false);
        }

        return ProviderEventResult.Empty(ProviderStatus.NotConfigured(
            "The calendar address is neither an http(s) URL nor a local .ics file."));
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
        catch (OperationCanceledException)
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
        ProviderAccount account,
        Uri initialUri,
        ProviderEventRange range,
        CancellationToken cancellationToken)
    {
        var uri = initialUri;

        for (var redirectCount = 0; ; redirectCount++)
        {
            if (!TryCreateHttpUri(uri.AbsoluteUri, out var requestUri))
            {
                return ProviderEventResult.Empty(ProviderStatus.NotConfigured(
                    "The calendar address is not a valid http(s) URL."));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Accept.ParseAdd(AcceptHeader);
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);

            if (redirectCount == 0 && _feeds.TryGetValue(account.Id, out var cachedFeed))
            {
                if (!string.IsNullOrEmpty(cachedFeed.ETag))
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", cachedFeed.ETag);
                }

                if (cachedFeed.LastModified is { } lastModified)
                {
                    request.Headers.IfModifiedSince = lastModified;
                }
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ProviderEventResult.Empty(ProviderStatus.Unavailable("The subscription timed out."));
            }
            catch (HttpRequestException)
            {
                return ProviderEventResult.Empty(ProviderStatus.Unavailable("The subscription could not be reached."));
            }

            using (response)
            {
                var statusCode = (int)response.StatusCode;

                if (statusCode == 304)
                {
                    if (_feeds.TryGetValue(account.Id, out var cached) && cached.Text.Length > 0)
                    {
                        return ParseFeed(cached.Text, range, notModified: true);
                    }

                    return ProviderEventResult.Ready([]);
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
                        next = location.IsAbsoluteUri ? location : new Uri(requestUri, location);
                    }
                    catch (UriFormatException)
                    {
                        return ProviderEventResult.Empty(
                            ProviderStatus.InvalidResponse("The subscription redirected to an invalid address."));
                    }

                    if (!TryCreateHttpUri(next.AbsoluteUri, out _))
                    {
                        return ProviderEventResult.Empty(
                            ProviderStatus.InvalidResponse("The subscription redirected to an unsupported address."));
                    }

                    uri = next;
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
                    bytes = await ReadAtMostAsync(response.Content, _options.MaxResponseBytes, timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return ProviderEventResult.Empty(ProviderStatus.Unavailable("The subscription timed out."));
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

                _feeds[account.Id] = new CachedFeed(
                    text,
                    response.Headers.ETag?.ToString(),
                    response.Content.Headers.LastModified);

                return ParseFeed(text, range, notModified: false);
            }
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

    private static bool TryResolveLocalPath(string value, out string path)
    {
        path = string.Empty;

#pragma warning disable CA1031 // Any malformed path is simply "not a local feed".
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (File.Exists(value))
            {
                path = Path.GetFullPath(value);
                return true;
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    return false;
                }

                path = uri.LocalPath;
                return !string.IsNullOrWhiteSpace(path);
            }

            if (Path.IsPathFullyQualified(value) &&
                value.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
            {
                path = Path.GetFullPath(value);
                return true;
            }
        }
        catch (Exception)
        {
            // Not a usable local path.
        }
#pragma warning restore CA1031

        return false;
    }

    private static bool TryCreateHttpUri(string value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var candidate))
        {
            return false;
        }

        if (candidate.Scheme != Uri.UriSchemeHttps && candidate.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        // No credentials in URLs: they would leak through logs, errors and redirects.
        if (!string.IsNullOrEmpty(candidate.UserInfo))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private sealed record CachedFeed(string Text, string? ETag, DateTimeOffset? LastModified);
}
