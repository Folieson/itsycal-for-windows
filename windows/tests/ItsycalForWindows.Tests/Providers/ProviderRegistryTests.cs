using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>Tests for factory validation and kind-based provider resolution.</summary>
public sealed class ProviderRegistryTests
{
    [Fact]
    public void TryGet_ResolvesRegisteredKind()
    {
        var provider = new FakeCalendarProvider { Kind = ProviderKind.Google };
        var registry = new ProviderRegistry([new DelegateProviderFactory(ProviderKind.Google, () => provider)]);

        Assert.True(registry.Contains(ProviderKind.Google));
        Assert.True(registry.TryGet(ProviderKind.Google, out var resolved));
        Assert.Same(provider, resolved);
        Assert.Same(provider, registry.Get(ProviderKind.Google));
    }

    [Fact]
    public void UnknownKind_ReturnsFalseAndThrowsOnGet()
    {
        var registry = new ProviderRegistry(
        [
            new DelegateProviderFactory(ProviderKind.Google, static () => new GoogleCalendarProvider()),
        ]);

        Assert.False(registry.Contains(ProviderKind.MicrosoftGraph));
        Assert.False(registry.TryGet(ProviderKind.MicrosoftGraph, out _));
        Assert.Throws<KeyNotFoundException>(() => registry.Get(ProviderKind.MicrosoftGraph));
    }

    [Fact]
    public void DuplicateKind_IsRejected()
    {
        var factories = new IProviderFactory[]
        {
            new DelegateProviderFactory(ProviderKind.Google, static () => new GoogleCalendarProvider()),
            new DelegateProviderFactory(ProviderKind.Google, static () => new GoogleCalendarProvider()),
        };

        Assert.Throws<ArgumentException>(() => new ProviderRegistry(factories));
    }

    [Fact]
    public void WrongKindFactory_IsRejected()
    {
        var factory = new DelegateProviderFactory(
            ProviderKind.Google,
            static () => new FakeCalendarProvider { Kind = ProviderKind.MicrosoftGraph });

        Assert.Throws<InvalidOperationException>(() => new ProviderRegistry([factory]));
    }

    [Fact]
    public void Catalog_RegistersEveryProviderKind()
    {
        var handler = new FakeHttpMessageHandler(
            static _ => new HttpResponseMessage(HttpStatusCode.OK));
        var registry = ProviderCatalog.CreateRegistry(handler);

        Assert.Equal(
            new[]
            {
                ProviderKind.Google,
                ProviderKind.MicrosoftGraph,
                ProviderKind.YandexCalDav,
                ProviderKind.GenericCalDav,
                ProviderKind.IcsSubscription,
            },
            registry.Kinds.ToArray());
    }
}
