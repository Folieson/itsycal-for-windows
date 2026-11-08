using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ItsycalForWindows.Models;
using ItsycalForWindows.Services;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Read-only Google Calendar provider: OAuth 2.0 PKCE tokens plus the Calendar API v3
/// <c>events.list</c> endpoint for one configured calendar.
/// </summary>
/// <remarks>
/// <para>
/// The provider is a public OAuth client: the interactive sign-in is performed by
/// <see cref="LoopbackOAuthFlow"/> and tokens are persisted through <see cref="IOAuthTokenStore"/>
/// (production: <see cref="OAuthTokenStore"/> over the DPAPI secret store). A fetch prefers the
/// secret delivered in <see cref="ProviderRequest.Secret"/>, falls back to the token store, refreshes
/// a missing/expired access token once, and on HTTP 401 refreshes once and retries the fetch once.
/// A second 401, a refresh failure or a missing refresh token is an explicit
/// <see cref="ProviderState.Unauthorized"/> result.
/// </para>
/// <para>
/// The single configured calendar comes from <see cref="ProviderAccount.RemoteId"/> when set and
/// otherwise from <see cref="GoogleOAuthOptions.CalendarId"/> (default <c>primary</c>). Calendar
/// selection UX is PRV-306 and is deliberately not implemented here.
/// </para>
/// <para>
/// Request mapping: <c>timeMin</c>/<c>timeMax</c> are RFC 3339 UTC, <c>singleEvents=true</c>,
/// <c>orderBy=startTime</c>, <c>maxResults=2500</c>, optional <c>timeZone=UTC</c> and
/// <c>nextPageToken</c> pagination. A page cap, an event cap, a response-size cap and a per-page
/// timeout fail the fetch with an explicit status. HTTP 403/429/5xx, network failures, malformed
/// JSON and unexpected redirects map to explicit non-ready statuses; caller cancellation always
/// propagates. No event content, calendar id, URL, token or authorization header is logged.
/// </para>
/// </remarks>
public sealed class GoogleCalendarProvider : ICalendarProvider, IDisposable
{
    /// <summary>Google Calendar API v3 events endpoint prefix.</summary>
    public const string EventsEndpointBase = "https://www.googleapis.com/calendar/v3/calendars/";

    /// <summary>Maximum results Google allows on one events page.</summary>
    public const int MaxResultsPerPage = 2500;

    private const string UserAgent = "ItsycalForWindows/1.0 (+https://github.com/Folieson/itsycal-for-windows)";

    /// <summary>Refresh an access token this long before its reported expiry.</summary>
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(2);

    private readonly HttpClient? _httpClient;
    private readonly GoogleOAuthOptions? _options;
    private readonly OAuthTokenClient? _tokenClient;
    private readonly IOAuthTokenStore? _tokenStore;
    private readonly TimeProvider _timeProvider;
    private bool _disposed;

    /// <summary>Creates an unconfigured provider: it reports <see cref="ProviderState.NotConfigured"/>.</summary>
    public GoogleCalendarProvider()
    {
        _timeProvider = TimeProvider.System;
    }

    /// <summary>
    /// Creates a configured provider over an HTTP handler. The handler stays owned by the caller;
    /// automatic redirects are disabled on a built-in handler so a redirect cannot move a bearer
    /// token to another origin.
    /// </summary>
    public GoogleCalendarProvider(
        HttpMessageHandler handler,
        GoogleOAuthOptions options,
        IOAuthTokenStore? tokenStore = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Validate();
        _tokenStore = tokenStore;
        _timeProvider = timeProvider ?? TimeProvider.System;

        DisableAutomaticRedirects(handler);
        _httpClient = new HttpClient(handler, disposeHandler: false);

        if (_options.IsConfigured)
        {
            _tokenClient = new OAuthTokenClient(_httpClient, _options.ToOAuthOptions(), _timeProvider);
        }
    }

    /// <inheritdoc />
    public ProviderKind Kind => ProviderKind.Google;

    /// <inheritdoc />
    public string DisplayName => "Google Calendar";

    /// <inheritdoc />
    public async Task<ProviderStatus> ProbeAsync(
        ProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_options is null || !_options.IsConfigured)
        {
            return ProviderStatus.NotConfigured("Google Calendar sign-in is not configured in this build.");
        }

        var token = await ResolveTokenAsync(request, cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return ProviderStatus.NotConfigured("This Google Calendar account is not signed in.");
        }

        var now = _timeProvider.GetUtcNow();
        if (token.HasUsableAccessToken(now, TokenRefreshSkew))
        {
            return ProviderStatus.Ready(ProviderCapabilities.ReadOnlyEvents);
        }

        if (!string.IsNullOrEmpty(token.RefreshToken))
        {
            // The access token is missing or expired, but a lazy refresh can recover it.
            return ProviderStatus.Ready(ProviderCapabilities.ReadOnlyEvents);
        }

        return ProviderStatus.Unauthorized("The Google Calendar sign-in has expired. Sign in again.");
    }

    /// <inheritdoc />
    public async Task<ProviderEventResult> GetEventsAsync(
        ProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (_options is null || !_options.IsConfigured)
        {
            return ProviderEventResult.Empty(
                ProviderStatus.NotConfigured("Google Calendar sign-in is not configured in this build."));
        }

        if (request.Range.IsEmpty)
        {
            return ProviderEventResult.Empty(ProviderStatus.Ready(message: "The requested range is empty."));
        }

        var token = await ResolveTokenAsync(request, cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return ProviderEventResult.Empty(
                ProviderStatus.NotConfigured("This Google Calendar account is not signed in."));
        }

        var now = _timeProvider.GetUtcNow();
        if (string.IsNullOrEmpty(token.AccessToken) || token.IsExpired(now, TokenRefreshSkew))
        {
            var refreshed = await RefreshTokenAsync(request, token, cancellationToken).ConfigureAwait(false);
            if (!refreshed.IsSuccess || refreshed.Token is null)
            {
                return ProviderEventResult.Empty(MapTokenFailure(refreshed));
            }

            token = refreshed.Token;
        }

        var outcome = await FetchAllPagesAsync(request, token.AccessToken, cancellationToken).ConfigureAwait(false);
        if (outcome.NeedsRefresh)
        {
            if (string.IsNullOrEmpty(token.RefreshToken))
            {
                return ProviderEventResult.Empty(
                    ProviderStatus.Unauthorized("The Google Calendar sign-in is no longer valid. Sign in again."));
            }

            var refreshed = await RefreshTokenAsync(request, token, cancellationToken).ConfigureAwait(false);
            if (!refreshed.IsSuccess || refreshed.Token is null)
            {
                return ProviderEventResult.Empty(MapTokenFailure(refreshed));
            }

            outcome = await FetchAllPagesAsync(request, refreshed.Token.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.NeedsRefresh)
            {
                return ProviderEventResult.Empty(
                    ProviderStatus.Unauthorized("The Google Calendar sign-in is no longer valid. Sign in again."));
            }
        }

        return outcome.Result ?? ProviderEventResult.Empty(
            ProviderStatus.InvalidResponse("Google Calendar returned an invalid response."));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _httpClient?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async Task<OAuthToken?> ResolveTokenAsync(
        ProviderRequest request,
        CancellationToken cancellationToken)
    {
        var fromRequest = OAuthToken.FromSecret(request.Secret);
        if (fromRequest is not null)
        {
            return fromRequest;
        }

        return _tokenStore is null
            ? null
            : await _tokenStore.GetAsync(request.Account.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OAuthTokenResult> RefreshTokenAsync(
        ProviderRequest request,
        OAuthToken token,
        CancellationToken cancellationToken)
    {
        if (_tokenClient is null || string.IsNullOrEmpty(token.RefreshToken))
        {
            return OAuthTokenResult.Failure(OAuthTokenStatus.InvalidGrant);
        }

        var result = await _tokenClient.RefreshAsync(token.RefreshToken, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Token is null)
        {
            return result;
        }

        // A refresh response may omit the refresh token/scope; never destroy the stored values.
        var merged = result.Token with
        {
            RefreshToken = string.IsNullOrEmpty(result.Token.RefreshToken)
                ? token.RefreshToken
                : result.Token.RefreshToken,
            Scope = string.IsNullOrEmpty(result.Token.Scope) ? token.Scope : result.Token.Scope,
        };

        if (_tokenStore is not null)
        {
            try
            {
                await _tokenStore.SaveAsync(request.Account.Id, merged, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // A storage failure is an explicit provider status, not a crash.
            catch (Exception)
            {
                return OAuthTokenResult.Failure(OAuthTokenStatus.StorageError);
            }
#pragma warning restore CA1031
        }

        return OAuthTokenResult.Success(merged);
    }

    private async Task<FetchOutcome> FetchAllPagesAsync(
        ProviderRequest request,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var calendarId = ResolveCalendarId(request.Account);
        var events = new List<ProviderEvent>();
        string? pageToken = null;
        var options = _options!;

        for (var page = 1; page <= options.MaxPages; page++)
        {
            var pageResult = await FetchPageAsync(
                    request.Range,
                    calendarId,
                    accessToken,
                    pageToken,
                    cancellationToken)
                .ConfigureAwait(false);

            if (pageResult.NeedsRefresh)
            {
                return FetchOutcome.Refresh();
            }

            if (pageResult.Failure is not null)
            {
                return FetchOutcome.Failure(pageResult.Failure);
            }

            if (events.Count + pageResult.Events.Count > options.MaxEvents)
            {
                return FetchOutcome.Failure(ProviderEventResult.Empty(
                    ProviderStatus.InvalidResponse("The Google Calendar response contains too many events.")));
            }

            events.AddRange(pageResult.Events);
            pageToken = pageResult.NextPageToken;

            if (string.IsNullOrEmpty(pageToken))
            {
                return FetchOutcome.Success(ProviderEventResult.Ready(events));
            }
        }

        return FetchOutcome.Failure(ProviderEventResult.Empty(
            ProviderStatus.InvalidResponse("The Google Calendar response spans too many pages.")));
    }

    private async Task<PageFetchResult> FetchPageAsync(
        ProviderEventRange range,
        string calendarId,
        string accessToken,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        var options = _options!;
        var uri = BuildEventsUri(range, calendarId, pageToken);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(options.RequestTimeout);

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, uri);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            httpRequest.Headers.UserAgent.ParseAdd(UserAgent);

            using var response = await _httpClient!
                .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, budget.Token)
                .ConfigureAwait(false);

            if (response.RequestMessage?.RequestUri is { } actual &&
                !string.Equals(actual.AbsoluteUri, uri.AbsoluteUri, StringComparison.Ordinal))
            {
                return PageFetchResult.Failed(ProviderEventResult.Empty(
                    ProviderStatus.InvalidResponse("Google Calendar returned an unexpected redirect.")));
            }

            var statusCode = (int)response.StatusCode;
            if (statusCode == 401)
            {
                return PageFetchResult.Refresh();
            }

            if (statusCode == 403)
            {
                return PageFetchResult.Failed(ProviderEventResult.Empty(
                    ProviderStatus.Unauthorized("Google Calendar denied access to this calendar.")));
            }

            if (statusCode == 429)
            {
                return PageFetchResult.Failed(ProviderEventResult.Empty(
                    ProviderStatus.Unavailable("Google is rate limiting requests. Try again later.")));
            }

            if (statusCode == 404)
            {
                return PageFetchResult.Failed(ProviderEventResult.Empty(
                    ProviderStatus.InvalidResponse("The configured Google calendar was not found.")));
            }

            if (statusCode is >= 300 and < 400)
            {
                return PageFetchResult.Failed(ProviderEventResult.Empty(
                    ProviderStatus.InvalidResponse("Google Calendar returned an unexpected redirect.")));
            }

            if (statusCode >= 500)
            {
                return PageFetchResult.Failed(ProviderEventResult.Empty(
                    ProviderStatus.Unavailable($"Google Calendar is temporarily unavailable (HTTP {statusCode}).")));
            }

            if (!response.IsSuccessStatusCode)
            {
                return PageFetchResult.Failed(ProviderEventResult.Empty(
                    ProviderStatus.InvalidResponse($"Google Calendar returned HTTP {statusCode}.")));
            }

            if (response.Content.Headers.ContentLength is { } contentLength &&
                contentLength > options.MaxResponseBytes)
            {
                return PageFetchResult.Failed(ProviderEventResult.Empty(
                    ProviderStatus.InvalidResponse("The Google Calendar response is too large.")));
            }

            var body = await ReadAtMostAsync(response.Content, options.MaxResponseBytes, budget.Token)
                .ConfigureAwait(false);
            if (body is null)
            {
                return PageFetchResult.Failed(ProviderEventResult.Empty(
                    ProviderStatus.InvalidResponse("The Google Calendar response is too large.")));
            }

            if (!TryParsePage(body, range, calendarId, out var events, out var nextPageToken))
            {
                return PageFetchResult.Failed(ProviderEventResult.Empty(
                    ProviderStatus.InvalidResponse("Google Calendar returned an invalid response.")));
            }

            return PageFetchResult.Success(events, nextPageToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return PageFetchResult.Failed(ProviderEventResult.Empty(
                ProviderStatus.Unavailable("Google Calendar timed out.")));
        }
        catch (HttpRequestException)
        {
            return PageFetchResult.Failed(ProviderEventResult.Empty(
                ProviderStatus.Unavailable("Google Calendar could not be reached.")));
        }
        catch (IOException)
        {
            return PageFetchResult.Failed(ProviderEventResult.Empty(
                ProviderStatus.Unavailable("Google Calendar could not be reached.")));
        }
    }

    private static Uri BuildEventsUri(
        ProviderEventRange range,
        string calendarId,
        string? pageToken)
    {
        var builder = new UriBuilder($"{EventsEndpointBase}{Uri.EscapeDataString(calendarId)}/events")
        {
            Query = BuildQuery(range, pageToken),
        };

        return builder.Uri;
    }

    private static string BuildQuery(ProviderEventRange range, string? pageToken)
    {
        var query = new List<string>
        {
            $"timeMin={Uri.EscapeDataString(FormatUtcInstant(range.FromInclusive))}",
            $"timeMax={Uri.EscapeDataString(FormatUtcInstant(range.ToExclusive))}",
            "singleEvents=true",
            "orderBy=startTime",
            $"maxResults={MaxResultsPerPage.ToString(CultureInfo.InvariantCulture)}",
            "timeZone=UTC",
        };

        if (!string.IsNullOrEmpty(pageToken))
        {
            query.Add($"pageToken={Uri.EscapeDataString(pageToken)}");
        }

        return string.Join('&', query);
    }

    private static string FormatUtcInstant(DateOnly date) =>
        EventRange.StartOfDayLocal(date)
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private string ResolveCalendarId(ProviderAccount account)
    {
        var remoteId = account.RemoteId?.Trim();
        return string.IsNullOrEmpty(remoteId) ? _options!.CalendarId.Trim() : remoteId;
    }

    private static bool TryParsePage(
        byte[] body,
        ProviderEventRange range,
        string calendarId,
        out List<ProviderEvent> events,
        out string? nextPageToken)
    {
        events = [];
        nextPageToken = null;

        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = document.RootElement;
            if (root.TryGetProperty("nextPageToken", out var next) &&
                next.ValueKind == JsonValueKind.String)
            {
                var value = next.GetString();
                nextPageToken = string.IsNullOrWhiteSpace(value) ? null : value;
            }

            if (!root.TryGetProperty("items", out var items))
            {
                return true;
            }

            if (items.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                if (!TryMapEvent(item, range, calendarId, out var mapped))
                {
                    return false;
                }

                if (mapped is not null)
                {
                    events.Add(mapped);
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryMapEvent(
        JsonElement item,
        ProviderEventRange range,
        string calendarId,
        out ProviderEvent? providerEvent)
    {
        providerEvent = null;

        if (!TryReadStartEnd(item, out var start, out var end, out var isAllDay))
        {
            return false;
        }

        if (end < start)
        {
            end = start;
        }

        if (isAllDay && end <= start)
        {
            end = start.AddDays(1);
        }

        var subject = ReadString(item, "summary") ?? string.Empty;
        var location = ReadString(item, "location");
        var details = ReadString(item, "description");
        var onlineMeetingLink = NormalizeLink(ReadString(item, "hangoutLink")) ??
            ReadConferenceLink(item);
        var htmlLink = ReadString(item, "htmlLink");

        var calendarEvent = new CalendarEvent(
            subject,
            start,
            end,
            isAllDay,
            location,
            onlineMeetingLink,
            details);

        if (!range.Contains(calendarEvent))
        {
            return true;
        }

        providerEvent = new ProviderEvent(
            calendarEvent,
            ReadString(item, "id"),
            calendarId,
            htmlLink);

        return true;
    }

    private static bool TryReadStartEnd(
        JsonElement item,
        out DateTimeOffset start,
        out DateTimeOffset end,
        out bool isAllDay)
    {
        start = default;
        end = default;
        isAllDay = false;

        if (!item.TryGetProperty("start", out var startElement) ||
            startElement.ValueKind != JsonValueKind.Object ||
            !TryReadEventTime(startElement, out start, out isAllDay))
        {
            return false;
        }

        if (item.TryGetProperty("end", out var endElement) &&
            endElement.ValueKind == JsonValueKind.Object &&
            TryReadEventTime(endElement, out end, out _))
        {
            return true;
        }

        end = isAllDay ? start.AddDays(1) : start;
        return true;
    }

    private static bool TryReadEventTime(
        JsonElement element,
        out DateTimeOffset instant,
        out bool isAllDay)
    {
        instant = default;
        isAllDay = false;

        if (element.TryGetProperty("dateTime", out var dateTime) &&
            dateTime.ValueKind == JsonValueKind.String &&
            dateTime.GetString() is { } text)
        {
            return DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out instant);
        }

        if (element.TryGetProperty("date", out var date) &&
            date.ValueKind == JsonValueKind.String &&
            date.GetString() is { } dateText &&
            DateOnly.TryParseExact(
                dateText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var day))
        {
            isAllDay = true;
            instant = EventRange.StartOfDayLocal(day);
            return true;
        }

        return false;
    }

    private static string? ReadConferenceLink(JsonElement item)
    {
        if (!item.TryGetProperty("conferenceData", out var conferenceData) ||
            conferenceData.ValueKind != JsonValueKind.Object ||
            !conferenceData.TryGetProperty("entryPoints", out var entryPoints) ||
            entryPoints.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? fallback = null;
        foreach (var entryPoint in entryPoints.EnumerateArray())
        {
            if (entryPoint.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var uri = NormalizeLink(ReadString(entryPoint, "uri"));
            if (uri is null)
            {
                continue;
            }

            if (string.Equals(ReadString(entryPoint, "entryPointType"), "video", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            fallback ??= uri;
        }

        return fallback;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            property.GetString() is { } text &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text.Trim();
        }

        return null;
    }

    private static string? NormalizeLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static ProviderStatus MapTokenFailure(OAuthTokenResult result) => result.Status switch
    {
        OAuthTokenStatus.InvalidGrant or OAuthTokenStatus.Unauthorized =>
            ProviderStatus.Unauthorized("The Google Calendar sign-in has expired. Sign in again."),
        OAuthTokenStatus.RateLimited =>
            ProviderStatus.Unavailable("Google is rate limiting requests. Try again later."),
        OAuthTokenStatus.Timeout =>
            ProviderStatus.Unavailable("The Google sign-in service timed out."),
        OAuthTokenStatus.NetworkError =>
            ProviderStatus.Unavailable("The Google sign-in service could not be reached."),
        OAuthTokenStatus.ServerError =>
            ProviderStatus.Unavailable("The Google sign-in service is temporarily unavailable."),
        OAuthTokenStatus.StorageError =>
            ProviderStatus.Unavailable("The refreshed Google Calendar sign-in could not be saved."),
        _ => ProviderStatus.InvalidResponse("The Google sign-in service returned an invalid response."),
    };

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

    private readonly record struct FetchOutcome(bool NeedsRefresh, ProviderEventResult? Result)
    {
        public static FetchOutcome Refresh() => new(true, null);

        public static FetchOutcome Success(ProviderEventResult result) => new(false, result);

        public static FetchOutcome Failure(ProviderEventResult result) => new(false, result);
    }

    private readonly record struct PageFetchResult(
        bool NeedsRefresh,
        ProviderEventResult? Failure,
        List<ProviderEvent> Events,
        string? NextPageToken)
    {
        public static PageFetchResult Refresh() => new(true, null, [], null);

        public static PageFetchResult Failed(ProviderEventResult result) => new(false, result, [], null);

        public static PageFetchResult Success(List<ProviderEvent> events, string? nextPageToken) =>
            new(false, null, events, nextPageToken);
    }
}

