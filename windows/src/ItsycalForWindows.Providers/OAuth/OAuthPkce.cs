using System.Security.Cryptography;
using System.Text;

namespace ItsycalForWindows.Providers;

/// <summary>
/// PKCE (RFC 7636) material for one OAuth authorization attempt: a cryptographically random
/// <c>code_verifier</c>, its S256 <c>code_challenge</c> and a high-entropy <c>state</c>.
/// </summary>
/// <remarks>
/// <para>
/// The verifier is 64 random bytes encoded as base64url (86 characters), which is inside the
/// RFC 7636 range of 43–128 characters; the state is 32 random bytes (43 base64url characters,
/// 256 bits of entropy). The values are generated per attempt with
/// <see cref="RandomNumberGenerator"/> and never reused.
/// </para>
/// <para>
/// <see cref="TryValidateAndConsumeState"/> compares the callback state in constant time and makes
/// it single-use: the first matching callback wins and every later call returns <c>false</c>. The
/// verifier and state are secret material: <see cref="ToString"/> redacts both, and callers must
/// never log them or put the verifier in a URL.
/// </para>
/// </remarks>
public sealed class OAuthPkce
{
    /// <summary>Minimum accepted PKCE verifier length (RFC 7636).</summary>
    public const int MinimumVerifierLength = 43;

    /// <summary>Maximum accepted PKCE verifier length (RFC 7636).</summary>
    public const int MaximumVerifierLength = 128;

    /// <summary>Entropy used for the verifier; base64url expands it to 86 characters.</summary>
    public const int VerifierEntropyBytes = 64;

    /// <summary>Entropy used for <c>state</c>; 32 bytes is 256 bits, above the ADR 0005 minimum.</summary>
    public const int StateEntropyBytes = 32;

    private static readonly char[] AllowedVerifierCharacters =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~".ToCharArray();

    private int _stateConsumed;

    private OAuthPkce(string codeVerifier, string codeChallenge, string state)
    {
        CodeVerifier = codeVerifier;
        CodeChallenge = codeChallenge;
        State = state;
    }

    /// <summary>Per-attempt secret verifier; never put it in a URL or a log.</summary>
    public string CodeVerifier { get; }

    /// <summary>S256 challenge derived from <see cref="CodeVerifier"/>; safe to send in the URL.</summary>
    public string CodeChallenge { get; }

    /// <summary>Per-attempt OAuth state; validate the callback against it exactly once.</summary>
    public string State { get; }

    /// <summary>True after a callback state has matched once.</summary>
    public bool IsStateConsumed => Volatile.Read(ref _stateConsumed) != 0;

    /// <summary>Creates fresh PKCE material. A test may inject a deterministic generator.</summary>
    public static OAuthPkce Create(RandomNumberGenerator? random = null)
    {
        var verifier = CreateCodeVerifier(random);
        return new OAuthPkce(verifier, ComputeCodeChallenge(verifier), CreateState(random));
    }

    /// <summary>Creates a base64url verifier of <see cref="VerifierEntropyBytes"/> random bytes.</summary>
    public static string CreateCodeVerifier(RandomNumberGenerator? random = null) =>
        Base64UrlEncode(GetRandomBytes(VerifierEntropyBytes, random));

    /// <summary>Creates a base64url <c>state</c> value of <see cref="StateEntropyBytes"/> random bytes.</summary>
    public static string CreateState(RandomNumberGenerator? random = null) =>
        Base64UrlEncode(GetRandomBytes(StateEntropyBytes, random));

    /// <summary>
    /// Computes the RFC 7636 S256 challenge: base64url(SHA-256(ASCII(verifier))). Throws for a
    /// value that is not a syntactically valid verifier.
    /// </summary>
    public static string ComputeCodeChallenge(string codeVerifier)
    {
        if (!IsValidCodeVerifier(codeVerifier))
        {
            throw new ArgumentException(
                "A PKCE code verifier must be 43–128 unreserved characters.",
                nameof(codeVerifier));
        }

        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    /// <summary>True when the value matches the RFC 7636 verifier syntax (43–128 unreserved chars).</summary>
    public static bool IsValidCodeVerifier(string? value)
    {
        if (value is null || value.Length is < MinimumVerifierLength or > MaximumVerifierLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (Array.IndexOf(AllowedVerifierCharacters, character) < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates the callback state in constant time and consumes it. Returns <c>false</c> for a
    /// missing/mismatched state or when the expected state was already consumed.
    /// </summary>
    public bool TryValidateAndConsumeState(string? state)
    {
        if (string.IsNullOrEmpty(state))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(State);
        var actual = Encoding.UTF8.GetBytes(state);
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            return false;
        }

        return Interlocked.Exchange(ref _stateConsumed, 1) == 0;
    }

    /// <summary>Redacted representation; the verifier and state are never printed.</summary>
    public override string ToString() =>
        $"OAuthPkce {{ CodeVerifier = (redacted), CodeChallenge = (redacted), State = (redacted), " +
        $"StateConsumed = {IsStateConsumed} }}";

    private static byte[] GetRandomBytes(int count, RandomNumberGenerator? random)
    {
        var bytes = new byte[count];
        if (random is null)
        {
            RandomNumberGenerator.Fill(bytes);
        }
        else
        {
            random.GetBytes(bytes);
        }

        return bytes;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
