using System;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for the deliberate OAuth/CalDAV skeletons: they must report an explicit state and must
/// never fabricate events or perform I/O.
/// </summary>
public sealed class SkeletonProviderTests
{
    [Theory]
    [InlineData(ProviderKind.Google)]
    [InlineData(ProviderKind.MicrosoftGraph)]
    [InlineData(ProviderKind.YandexCalDav)]
    [InlineData(ProviderKind.GenericCalDav)]
    public async Task SkeletonWithoutConfiguration_ReturnsNotConfigured(ProviderKind kind)
    {
        var provider = CreateProvider(kind);
        var account = ProviderAccount.Create(kind, "Account");

        var status = await provider.ProbeAsync(ProviderRequest.Probe(account));

        Assert.Equal(ProviderState.NotConfigured, status.State);
        Assert.False(status.IsReady);
        Assert.False(status.IsConfigured);
    }

    [Theory]
    [InlineData(ProviderKind.Google)]
    [InlineData(ProviderKind.MicrosoftGraph)]
    [InlineData(ProviderKind.YandexCalDav)]
    [InlineData(ProviderKind.GenericCalDav)]
    public async Task SkeletonWithAddress_ReturnsNotImplemented(ProviderKind kind)
    {
        var provider = CreateProvider(kind);
        var account = ProviderAccount.Create(kind, "Account", remoteId: "primary");

        var status = await provider.ProbeAsync(ProviderRequest.Probe(account));

        Assert.Equal(ProviderState.NotImplemented, status.State);
        Assert.False(status.IsReady);
    }

    [Theory]
    [InlineData(ProviderKind.Google)]
    [InlineData(ProviderKind.MicrosoftGraph)]
    [InlineData(ProviderKind.YandexCalDav)]
    [InlineData(ProviderKind.GenericCalDav)]
    public async Task SkeletonWithSecret_ReturnsNotImplemented(ProviderKind kind)
    {
        var provider = CreateProvider(kind);
        var account = ProviderAccount.Create(kind, "Account");

        var status = await provider.ProbeAsync(
            ProviderRequest.Probe(account, new ProviderSecret { Password = "app-password" }));

        Assert.Equal(ProviderState.NotImplemented, status.State);
    }

    [Theory]
    [InlineData(ProviderKind.Google)]
    [InlineData(ProviderKind.MicrosoftGraph)]
    [InlineData(ProviderKind.YandexCalDav)]
    [InlineData(ProviderKind.GenericCalDav)]
    public async Task SkeletonGetEvents_ReturnsEmptyAndExplicitStatus(ProviderKind kind)
    {
        var provider = CreateProvider(kind);
        var account = ProviderAccount.Create(kind, "Account", remoteId: "primary");
        var range = ProviderEventRange.Days(new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1));

        var result = await provider.GetEventsAsync(ProviderRequest.Fetch(account, range));

        Assert.Empty(result.Events);
        Assert.Equal(ProviderState.NotImplemented, result.Status.State);
        Assert.False(result.NotModified);
    }

    [Fact]
    public async Task Skeleton_ProbeHonorsCancellation()
    {
        var provider = new GoogleCalendarProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.ProbeAsync(
                ProviderRequest.Probe(ProviderAccount.Create(ProviderKind.Google, "Account")),
                cancellation.Token));
    }

    private static ICalendarProvider CreateProvider(ProviderKind kind) => kind switch
    {
        ProviderKind.Google => new GoogleCalendarProvider(),
        ProviderKind.MicrosoftGraph => new MicrosoftGraphProvider(),
        ProviderKind.YandexCalDav => new YandexCalDavProvider(),
        ProviderKind.GenericCalDav => new GenericCalDavProvider(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}
