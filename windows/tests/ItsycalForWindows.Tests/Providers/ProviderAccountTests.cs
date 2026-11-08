using System;
using System.Linq;
using System.Text.Json;
using ItsycalForWindows.Providers;
using Xunit;
using CalendarEvent = ItsycalForWindows.Models.CalendarEvent;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for the public account model: stable ids, non-secret metadata, redacted secret
/// formatting and the provider-facing range/status contracts.
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
    public void Serialization_HasNoSecretFieldsOrValues()
    {
        const string secret = "super-secret-token-value";
        var account = ProviderAccount.Create(
            ProviderKind.GenericCalDav,
            "Nextcloud",
            "alice",
            "https://cloud.example.com/remote.php/dav/calendars/alice/personal/");

        var json = JsonSerializer.Serialize(account);

        Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, account.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToString_ContainsOnlyPublicMetadata()
    {
        var account = ProviderAccount.Create(
            ProviderKind.MicrosoftGraph,
            "Work",
            "user@contoso.com",
            "calendar-id");

        var text = account.ToString();

        Assert.Contains("MicrosoftGraph", text);
        Assert.Contains("Work", text);
        Assert.Contains("calendar-id", text);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToString_RedactsQuerySecretsInRemoteUrls()
    {
        var account = ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            "Feed",
            remoteId: "https://example.com/feed.ics?token=super-secret");

        var text = account.ToString();

        Assert.DoesNotContain("super-secret", text);
        Assert.Contains("example.com/feed.ics", text);
    }

    [Fact]
    public void Secret_ToStringRedactsEverySecretValue()
    {
        var secret = new ProviderSecret
        {
            AccessToken = "access-token-123",
            RefreshToken = "refresh-token-456",
            Password = "app-password-789",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        };

        var text = secret.ToString();

        Assert.DoesNotContain("access-token-123", text);
        Assert.DoesNotContain("refresh-token-456", text);
        Assert.DoesNotContain("app-password-789", text);
        Assert.Contains("(redacted)", text);
    }

    [Fact]
    public void Secret_IsEmptyRecognizesEveryEmptyShape()
    {
        Assert.True(new ProviderSecret().IsEmpty);
        Assert.False(new ProviderSecret { AccessToken = "a" }.IsEmpty);
        Assert.False(new ProviderSecret { RefreshToken = "r" }.IsEmpty);
        Assert.False(new ProviderSecret { Password = "p" }.IsEmpty);
        Assert.False(new ProviderSecret { ExpiresAt = DateTimeOffset.UtcNow }.IsEmpty);
        Assert.True(new ProviderSecret { AccessToken = "a" }.HasOAuthToken);
    }

    [Fact]
    public void ProviderEventRange_IsHalfOpenAndFiltersOverlaps()
    {
        var start = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var inside = new ProviderEvent(new CalendarEvent("Inside", start, start.AddHours(1)));
        var touchingEnd = new ProviderEvent(new CalendarEvent(
            "Touching",
            new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 11, 1, 0, 0, TimeSpan.Zero)));

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
    public void ProviderEventResult_ProjectsToCoreEvents()
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
    }
}
