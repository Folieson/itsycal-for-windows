using System;
using System.Linq;
using System.Text.Json;
using ItsycalForWindows.Providers;
using ItsycalForWindows.Services;
using Xunit;
using CalendarEvent = ItsycalForWindows.Models.CalendarEvent;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for the public account model: stable ids, display-safe metadata, secret separation,
/// redacted formatting and the provider-facing request/range/status contracts.
/// </summary>
public sealed class ProviderAccountTests
{
    [Fact]
    public void Create_AssignsStableIdAndTrimsMetadata()
    {
        var account = ProviderAccount.Create(
            ProviderKind.Google,
            "  Work  ",
            "  user@example.com  ",
            "  primary  ");

        Assert.Equal(32, account.Id.Length);
        Assert.DoesNotContain("-", account.Id);
        Assert.Equal("Work", account.DisplayName);
        Assert.Equal("user@example.com", account.AccountName);
        Assert.Equal("primary", account.RemoteId);
        Assert.True(account.Enabled);
        Assert.NotEqual(default, account.CreatedAt);
        Assert.Null(account.LastSyncAt);
    }

    [Fact]
    public void Create_WithExplicitIdKeepsThatId()
    {
        var account = ProviderAccount.Create(ProviderKind.IcsSubscription, "Feed", id: "stable-id");

        Assert.Equal("stable-id", account.Id);
    }

    [Fact]
    public void Create_IcsSubscriptionWithATokenizedRemoteIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            "Leaky",
            remoteId: $"https://example.com/private/SECRET-TOKEN/feed.ics"));
    }

    [Fact]
    public void CreateIcsSubscription_KeepsOnlyTheOriginInAccountMetadata()
    {
        const string token = "SECRET-FEED-TOKEN";
        var (account, secret) = ProviderAccount.CreateIcsSubscription(
            "Team feed",
            $"https://calendar.example.com/private/{token}/feed.ics?token={token}");

        Assert.Equal(ProviderKind.IcsSubscription, account.Kind);
        Assert.Equal("https://calendar.example.com", account.RemoteId);
        Assert.Equal($"https://calendar.example.com/private/{token}/feed.ics?token={token}", secret.Resource);
        Assert.False(secret.IsEmpty);

        var json = JsonSerializer.Serialize(account);
        Assert.DoesNotContain(token, json, StringComparison.Ordinal);
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("?token", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateIcsSubscription_LocalFileKeepsNoPathInMetadata()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "itsycal-provider-tests",
            Guid.NewGuid().ToString("N"),
            "feed.ics");

        var (account, secret) = ProviderAccount.CreateIcsSubscription("Local", path);

        Assert.Equal(IcsFeedAddress.LocalFileRemoteId, account.RemoteId);
        Assert.Equal(path, secret.Resource);
    }

    [Theory]
    [InlineData("http://example.com/feed.ics")]
    [InlineData("feed.ics")]
    [InlineData("https://user:password@example.com/feed.ics")]
    [InlineData("ftp://example.com/feed.ics")]
    public void CreateIcsSubscription_RejectsUnsafeOrRelativeAddresses(string address)
    {
        Assert.Throws<ArgumentException>(() => ProviderAccount.CreateIcsSubscription("Feed", address));
    }

    [Fact]
    public void Serialization_HasNoSecretFieldsOrValues()
    {
        const string token = "super-secret-token-value";
        var account = ProviderAccount.Create(
            ProviderKind.GenericCalDav,
            "Nextcloud",
            "alice",
            "https://cloud.example.com/remote.php/dav/calendars/alice/personal/");

        var json = JsonSerializer.Serialize(account);

        Assert.DoesNotContain(token, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resource", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, account.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToString_ContainsOnlyIdKindEnabledAndHost()
    {
        var account = ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            "Work",
            "user@contoso.com",
            "https://example.com");

        var text = account.ToString();

        Assert.Contains(account.Id, text);
        Assert.Contains("IcsSubscription", text);
        Assert.Contains("example.com", text);
        Assert.DoesNotContain("Work", text);
        Assert.DoesNotContain("user@contoso.com", text);
        Assert.DoesNotContain("contoso", text);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToString_RedactsPathAndQuerySecretsInRemoteUrls()
    {
        // A hand-written or legacy record can still carry an unsafe value; ToString must never
        // print its path or query even though the store rejects persisting one.
        var account = new ProviderAccount
        {
            Id = "legacy",
            Kind = ProviderKind.IcsSubscription,
            DisplayName = "Feed",
            RemoteId = "https://example.com/private/super-secret/feed.ics?token=super-secret",
        };

        var text = account.ToString();

        Assert.DoesNotContain("super-secret", text, StringComparison.Ordinal);
        Assert.Contains("example.com", text);
        Assert.DoesNotContain("private", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_DoesNotPrintLocalPaths()
    {
        var account = ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            "Local",
            remoteId: IcsFeedAddress.LocalFileRemoteId);

        var text = account.ToString();

        Assert.Contains("(local file)", text);
        Assert.DoesNotContain("feed.ics", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Secret_ToStringRedactsEverySecretValue()
    {
        var secret = new ProviderSecret
        {
            AccessToken = "access-token-123",
            RefreshToken = "refresh-token-456",
            Password = "app-password-789",
            Resource = "https://example.com/private/feed-token/feed.ics",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };

        var text = secret.ToString();

        Assert.DoesNotContain("access-token-123", text);
        Assert.DoesNotContain("refresh-token-456", text);
        Assert.DoesNotContain("app-password-789", text);
        Assert.DoesNotContain("feed-token", text, StringComparison.Ordinal);
        Assert.Contains("(redacted)", text);
    }

    [Fact]
    public void Secret_IsEmptyRecognizesEveryEmptyShape()
    {
        Assert.True(new ProviderSecret().IsEmpty);
        Assert.False(new ProviderSecret { AccessToken = "a" }.IsEmpty);
        Assert.False(new ProviderSecret { RefreshToken = "r" }.IsEmpty);
        Assert.False(new ProviderSecret { Password = "p" }.IsEmpty);
        Assert.False(new ProviderSecret { Resource = "https://example.com/feed.ics" }.IsEmpty);
        Assert.False(new ProviderSecret { ExpiresAt = DateTimeOffset.UtcNow }.IsEmpty);
        Assert.True(new ProviderSecret { AccessToken = "a" }.HasOAuthToken);
        Assert.True(new ProviderSecret { Resource = "https://example.com/feed.ics" }.HasResource);
    }

    [Fact]
    public void ProviderRequest_ToStringRedactsTheSecret()
    {
        var account = ProviderAccount.Create(ProviderKind.IcsSubscription, "Feed", remoteId: "https://example.com");
        var request = ProviderRequest.Fetch(
            account,
            ProviderEventRange.Days(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1)),
            new ProviderSecret { Resource = "https://example.com/secret-token/feed.ics" });

        var text = request.ToString();

        Assert.DoesNotContain("secret-token", text, StringComparison.Ordinal);
        Assert.Contains("(present, redacted)", text);
        Assert.Contains("example.com", text);
    }

    [Fact]
    public void ProviderEvent_ToStringRedactsEventContent()
    {
        var calendarEvent = new CalendarEvent(
            "Secret board meeting",
            new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero),
            Location: "Room 42",
            Details: "Confidential notes");

        var text = new ProviderEvent(calendarEvent, remoteId: "event-1").ToString();

        Assert.DoesNotContain("Secret board meeting", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Room 42", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Confidential notes", text, StringComparison.Ordinal);
        Assert.Contains("event-1", text);
        Assert.Contains("content redacted", text);
    }

    [Fact]
    public void ProviderEventRange_IsHalfOpenAndFiltersOverlaps()
    {
        var start = EventRange.StartOfDayLocal(new DateOnly(2026, 11, 10)).AddHours(9);
        var inside = new ProviderEvent(new CalendarEvent("Inside", start, start.AddHours(1)));
        var touchingEnd = new ProviderEvent(new CalendarEvent(
            "Touching",
            EventRange.StartOfDayLocal(new DateOnly(2026, 11, 11)),
            EventRange.StartOfDayLocal(new DateOnly(2026, 11, 11)).AddHours(1)));

        var range = ProviderEventRange.Days(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11));

        Assert.True(range.Contains(inside.Event));
        Assert.False(range.Contains(touchingEnd.Event));
        Assert.Equal(1, range.DayCount);
        Assert.False(range.IsEmpty);
    }

    [Fact]
    public void ProviderStatus_FactoriesAreExplicit()
    {
        Assert.True(ProviderStatus.Ready().IsReady);
        Assert.Equal(ProviderState.NotConfigured, ProviderStatus.NotConfigured("setup").State);
        Assert.Equal(ProviderState.NotImplemented, ProviderStatus.NotImplemented("later").State);
        Assert.Equal(ProviderState.Unauthorized, ProviderStatus.Unauthorized("auth").State);
        Assert.Equal(ProviderState.Unavailable, ProviderStatus.Unavailable("offline").State);
        Assert.Equal(ProviderState.InvalidResponse, ProviderStatus.InvalidResponse("bad").State);
        Assert.False(ProviderStatus.NotConfigured().IsConfigured);
        Assert.True(ProviderStatus.Unavailable().IsConfigured);
    }

    [Fact]
    public void ProviderEventResult_ProjectsToCoreEventsAndRedactsToString()
    {
        var start = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var result = ProviderEventResult.Ready(
        [
            new ProviderEvent(new CalendarEvent("A", start, start.AddHours(1))),
            new ProviderEvent(new CalendarEvent("B", start.AddDays(1), start.AddDays(1).AddHours(1))),
        ]);

        Assert.True(result.Status.IsReady);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal(new[] { "A", "B" }, result.ToCalendarEvents().Select(calendarEvent => calendarEvent.Subject).ToArray());
        Assert.DoesNotContain("\"A\"", result.ToString(), StringComparison.Ordinal);
        Assert.Contains("EventCount = 2", result.ToString());
    }

    [Fact]
    public void SyncResultToStrings_RedactEventContent()
    {
        var account = ProviderAccount.Create(ProviderKind.IcsSubscription, "Feed", remoteId: "https://example.com");
        var start = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var outcome = new ProviderSyncOutcome(
            account,
            ProviderSyncStatus.Succeeded,
            ProviderStatus.Ready(),
            [new CalendarEvent("Confidential board meeting", start, start.AddHours(1))],
            start,
            error: null);

        var result = new ProviderSyncResult([outcome]);

        Assert.DoesNotContain("Confidential board meeting", outcome.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Confidential board meeting", result.ToString(), StringComparison.Ordinal);
        Assert.Contains("EventCount = 1", outcome.ToString());
        Assert.Contains("Succeeded = 1", result.ToString());
    }
}
