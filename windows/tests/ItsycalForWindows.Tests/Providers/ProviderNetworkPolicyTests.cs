using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for <see cref="ProviderNetworkPolicy"/>: HTTPS-only, credentials rejection, private
/// host opt-in and always-blocked link-local/cloud-metadata ranges (including IPv4-mapped IPv6).
/// </summary>
public sealed class ProviderNetworkPolicyTests
{
    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("fe80::1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::")]
    [InlineData("fd00:ec2::254")]
    [InlineData("100.100.100.200")]
    public void IsBlockedAddress_TrueForLinkLocalAndMetadata(string address)
    {
        Assert.True(ProviderNetworkPolicy.IsBlockedAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    public void IsBlockedAddress_FalseForRoutableAndPrivate(string address)
    {
        Assert.False(ProviderNetworkPolicy.IsBlockedAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.20.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    public void IsPrivateOrLoopbackAddress_TrueForPrivateRanges(string address)
    {
        Assert.True(ProviderNetworkPolicy.IsPrivateOrLoopbackAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("8.8.8.8")]
    public void IsPrivateOrLoopbackAddress_FalseForPublicAddresses(string address)
    {
        Assert.False(ProviderNetworkPolicy.IsPrivateOrLoopbackAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public void ValidateSyntactic_RejectsHttpAndCredentials()
    {
        var options = new ProviderHttpOptions();

        Assert.False(ProviderNetworkPolicy.ValidateSyntactic(new Uri("http://example.com/feed.ics"), options).IsAllowed);
        Assert.False(ProviderNetworkPolicy.ValidateSyntactic(
            new Uri("https://user:password@example.com/feed.ics"),
            options).IsAllowed);
        Assert.True(ProviderNetworkPolicy.ValidateSyntactic(new Uri("https://example.com/feed.ics"), options).IsAllowed);
    }

    [Fact]
    public void ValidateSyntactic_RejectsLocalhostUnlessSelfHostedIsEnabled()
    {
        Assert.False(ProviderNetworkPolicy.ValidateSyntactic(
            new Uri("https://localhost/feed.ics"),
            new ProviderHttpOptions()).IsAllowed);
        Assert.True(ProviderNetworkPolicy.ValidateSyntactic(
            new Uri("https://localhost/feed.ics"),
            new ProviderHttpOptions { AllowPrivateHosts = true }).IsAllowed);
    }

    [Fact]
    public async Task ValidateAsync_ResolvesHostsAndRejectsPrivateAddresses()
    {
        var options = new ProviderHttpOptions
        {
            HostResolver = (_, _) => Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("10.0.0.5")]),
        };

        var result = await ProviderNetworkPolicy.ValidateAsync(
            new Uri("https://calendar.internal/feed.ics"),
            options,
            CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Equal(ProviderState.InvalidResponse, result.State);
        Assert.DoesNotContain("calendar.internal", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_AllowsPublicHostsAndHonorsSelfHostedOptIn()
    {
        var publicOptions = new ProviderHttpOptions
        {
            HostResolver = (_, _) => Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("93.184.216.34")]),
        };
        var privateOptions = publicOptions with
        {
            AllowPrivateHosts = true,
            HostResolver = (_, _) => Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("10.0.0.5")]),
        };

        Assert.True((await ProviderNetworkPolicy.ValidateAsync(
            new Uri("https://example.com/feed.ics"),
            publicOptions,
            CancellationToken.None)).IsAllowed);
        Assert.True((await ProviderNetworkPolicy.ValidateAsync(
            new Uri("https://calendar.internal/feed.ics"),
            privateOptions,
            CancellationToken.None)).IsAllowed);
    }

    [Fact]
    public async Task ValidateAsync_DnsFailureIsUnavailableWithoutLeakingTheHost()
    {
        var options = new ProviderHttpOptions
        {
            HostResolver = (_, _) => throw new System.Net.Sockets.SocketException(),
        };

        var result = await ProviderNetworkPolicy.ValidateAsync(
            new Uri("https://missing.example/feed.ics"),
            options,
            CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Equal(ProviderState.Unavailable, result.State);
        Assert.DoesNotContain("missing.example", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
