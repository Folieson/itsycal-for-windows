using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for the PKCE material: verifier syntax and entropy, S256 challenge derivation, the
/// RFC 7636 test vector, state validation and single-use semantics, and redaction.
/// </summary>
public sealed class OAuthPkceTests
{
    [Fact]
    public void Create_ProducesVerifierChallengeAndStateWithinRfc7636()
    {
        var pkce = OAuthPkce.Create();

        Assert.Equal(86, pkce.CodeVerifier.Length);
        Assert.InRange(pkce.CodeVerifier.Length, OAuthPkce.MinimumVerifierLength, OAuthPkce.MaximumVerifierLength);
        Assert.True(OAuthPkce.IsValidCodeVerifier(pkce.CodeVerifier));
        Assert.Equal(43, pkce.CodeChallenge.Length);
        Assert.Equal(43, pkce.State.Length);
        Assert.Equal(Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(pkce.CodeVerifier))), pkce.CodeChallenge);
        Assert.Equal(pkce.CodeChallenge, OAuthPkce.ComputeCodeChallenge(pkce.CodeVerifier));
        Assert.False(pkce.IsStateConsumed);
    }

    [Fact]
    public void Create_ProducesDistinctValuesPerAttempt()
    {
        var first = OAuthPkce.Create();
        var second = OAuthPkce.Create();

        Assert.NotEqual(first.CodeVerifier, second.CodeVerifier);
        Assert.NotEqual(first.CodeChallenge, second.CodeChallenge);
        Assert.NotEqual(first.State, second.State);
    }

    [Fact]
    public void ComputeCodeChallenge_MatchesTheRfc7636Example()
    {
        const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";

        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", OAuthPkce.ComputeCodeChallenge(verifier));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXkX")] // 44 chars is valid; this is 44 too
    public void IsValidCodeVerifier_RejectsEmptyShortAndOverlongValues(string value)
    {
        var result = OAuthPkce.IsValidCodeVerifier(value);

        if (value.Length is >= OAuthPkce.MinimumVerifierLength and <= OAuthPkce.MaximumVerifierLength)
        {
            Assert.True(result);
        }
        else
        {
            Assert.False(result);
        }
    }

    [Fact]
    public void IsValidCodeVerifier_RejectsCharactersOutsideTheUnreservedSet()
    {
        var valid = new string('a', 43);

        Assert.True(OAuthPkce.IsValidCodeVerifier(valid));
        Assert.False(OAuthPkce.IsValidCodeVerifier(valid[..42] + "+"));
        Assert.False(OAuthPkce.IsValidCodeVerifier(valid[..42] + " "));
        Assert.False(OAuthPkce.IsValidCodeVerifier(new string('a', 42)));
        Assert.False(OAuthPkce.IsValidCodeVerifier(new string('a', 129)));
        Assert.False(OAuthPkce.IsValidCodeVerifier(null));
    }

    [Fact]
    public void TryValidateAndConsumeState_AcceptsOnlyOnce()
    {
        var pkce = OAuthPkce.Create();

        Assert.True(pkce.TryValidateAndConsumeState(pkce.State));
        Assert.True(pkce.IsStateConsumed);
        Assert.False(pkce.TryValidateAndConsumeState(pkce.State));
    }

    [Fact]
    public void TryValidateAndConsumeState_MismatchDoesNotConsumeTheExpectedState()
    {
        var pkce = OAuthPkce.Create();

        Assert.False(pkce.TryValidateAndConsumeState("not-the-state"));
        Assert.False(pkce.TryValidateAndConsumeState(null));
        Assert.False(pkce.IsStateConsumed);
        Assert.True(pkce.TryValidateAndConsumeState(pkce.State));
    }

    [Fact]
    public void ToString_RedactsVerifierChallengeAndState()
    {
        var pkce = OAuthPkce.Create();
        var text = pkce.ToString();

        Assert.DoesNotContain(pkce.CodeVerifier, text, StringComparison.Ordinal);
        Assert.DoesNotContain(pkce.CodeChallenge, text, StringComparison.Ordinal);
        Assert.DoesNotContain(pkce.State, text, StringComparison.Ordinal);
        Assert.Contains("(redacted)", text);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
