using System;
using System.Text.Json;
using System.Threading.Tasks;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for the OAuth token store over <see cref="IProviderSecretStore"/>: access/refresh/expiry/
/// scope round-trip, refresh-token preservation on rotation, removal on sign-out, and no token in
/// account metadata or formatted output.
/// </summary>
public sealed class OAuthTokenStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SaveAndGet_RoundTripsAccessRefreshExpiryAndScope()
    {
        var secrets = new InMemorySecretStore();
        var store = new OAuthTokenStore(secrets);

        await store.SaveAsync("account-1", new OAuthToken
        {
            AccessToken = "access-token-value",
            RefreshToken = "refresh-token-value",
            ExpiresAt = Now.AddHours(1),
            Scope = "https://www.googleapis.com/auth/calendar.readonly",
        });

        var loaded = await store.GetAsync("account-1");

        Assert.NotNull(loaded);
        Assert.Equal("access-token-value", loaded!.AccessToken);
        Assert.Equal("refresh-token-value", loaded.RefreshToken);
        Assert.Equal(Now.AddHours(1), loaded.ExpiresAt);
        Assert.Equal("https://www.googleapis.com/auth/calendar.readonly", loaded.Scope);

        Assert.True(secrets.TryGet("account-1", out var secret));
        Assert.Equal("access-token-value", secret!.AccessToken);
        Assert.Equal("refresh-token-value", secret.RefreshToken);
        Assert.Equal(Now.AddHours(1), secret.ExpiresAt);
        Assert.Equal("https://www.googleapis.com/auth/calendar.readonly", secret.Scope);
        Assert.DoesNotContain("access-token-value", secret.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-token-value", secret.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveAsync_WithoutARefreshTokenKeepsTheStoredRefreshToken()
    {
        var secrets = new InMemorySecretStore();
        var store = new OAuthTokenStore(secrets);

        await store.SaveAsync("account-1", new OAuthToken
        {
            AccessToken = "access-1",
            RefreshToken = "refresh-1",
            ExpiresAt = Now.AddHours(1),
            Scope = "scope-1",
        });

        await store.SaveAsync("account-1", new OAuthToken
        {
            AccessToken = "access-2",
            ExpiresAt = Now.AddHours(2),
        });

        var loaded = await store.GetAsync("account-1");

        Assert.Equal("access-2", loaded!.AccessToken);
        Assert.Equal("refresh-1", loaded.RefreshToken);
        Assert.Equal("scope-1", loaded.Scope);
    }

    [Fact]
    public async Task RemoveAsync_DeletesTheStoredToken()
    {
        var secrets = new InMemorySecretStore();
        var store = new OAuthTokenStore(secrets);
        await store.SaveAsync("account-1", new OAuthToken { AccessToken = "access" });

        await store.RemoveAsync("account-1");

        Assert.Null(await store.GetAsync("account-1"));
        Assert.False(secrets.TryGet("account-1", out _));
    }

    [Fact]
    public async Task GetAsync_ReturnsNullForAnEmptyOrNonOAuthSecret()
    {
        var secrets = new InMemorySecretStore();
        var store = new OAuthTokenStore(secrets);

        Assert.Null(await store.GetAsync("missing"));

        secrets.Save("account-1", new ProviderSecret { Password = "app-password" });
        Assert.Null(await store.GetAsync("account-1"));
    }

    [Fact]
    public void OAuthTokenFromSecret_MapsTheStoredValues()
    {
        var token = OAuthToken.FromSecret(new ProviderSecret
        {
            AccessToken = "access",
            RefreshToken = "refresh",
            ExpiresAt = Now.AddMinutes(30),
            Scope = "scope",
        });

        Assert.NotNull(token);
        Assert.Equal("access", token!.AccessToken);
        Assert.Equal("refresh", token.RefreshToken);
        Assert.Equal(Now.AddMinutes(30), token.ExpiresAt);
        Assert.Equal("scope", token.Scope);
        Assert.Null(OAuthToken.FromSecret(null));
        Assert.Null(OAuthToken.FromSecret(new ProviderSecret()));
    }

    [Fact]
    public void OAuthToken_ExpiryHonorsTheSkewAndRedactsItsValues()
    {
        var token = new OAuthToken
        {
            AccessToken = "secret-access",
            RefreshToken = "secret-refresh",
            ExpiresAt = Now.AddMinutes(1),
        };

        Assert.False(token.IsExpired(Now));
        Assert.True(token.IsExpired(Now, TimeSpan.FromMinutes(2)));
        Assert.True(token.HasUsableAccessToken(Now));
        Assert.False(token.HasUsableAccessToken(Now, TimeSpan.FromMinutes(2)));

        var text = token.ToString();
        Assert.DoesNotContain("secret-access", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-refresh", text, StringComparison.Ordinal);
        Assert.Contains("(redacted)", text);
    }

    [Fact]
    public void ProviderAccountJson_NeverCarriesOAuthTokenFields()
    {
        var account = ProviderAccount.Create(ProviderKind.Google, "Google", remoteId: "primary");

        var json = JsonSerializer.Serialize(account);

        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("scope", json, StringComparison.OrdinalIgnoreCase);
    }
}
