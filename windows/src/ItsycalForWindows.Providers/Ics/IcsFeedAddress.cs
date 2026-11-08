namespace ItsycalForWindows.Providers;

/// <summary>
/// Parses and redacts ICS subscription addresses so the account registry never holds a
/// token-bearing URL.
/// </summary>
/// <remarks>
/// <para>
/// An ICS subscription is either an absolute HTTPS URL or an absolute local <c>.ics</c> file
/// (a rooted path or a <c>file:</c> URI). The complete address, including any path/query token,
/// is secret material and is stored in <see cref="ProviderSecret.Resource"/>; only
/// <see cref="ProviderAccount.RemoteId"/> is persisted in <c>accounts.json</c>, and for an ICS
/// account it is the display-safe origin (<c>scheme://host[:port]</c>) or
/// <see cref="LocalFileRemoteId"/> for a local file.
/// </para>
/// <para>
/// Plain <c>http</c> is rejected: the provider network policy is HTTPS-only (ADR 0005), and the
/// rule is applied when the account is created so unsafe metadata cannot be persisted at all.
/// Relative paths are rejected so the local-file mode is explicit and testable.
/// </para>
/// </remarks>
public static class IcsFeedAddress
{
    /// <summary>Display-safe <see cref="ProviderAccount.RemoteId"/> value for a local file feed.</summary>
    public const string LocalFileRemoteId = "file";

    /// <summary>
    /// Converts a full feed address to the display-safe account remote id. Throws
    /// <see cref="ArgumentException"/> when the address is not an absolute HTTPS URL or an
    /// absolute local <c>.ics</c> file.
    /// </summary>
    public static string ToRemoteId(string feedAddress)
    {
        if (!TryToRemoteId(feedAddress, out var remoteId))
        {
            throw new ArgumentException(
                "The feed address must be an absolute HTTPS URL or an absolute local .ics file.",
                nameof(feedAddress));
        }

        return remoteId;
    }

    /// <summary>Tries to convert a full feed address to a display-safe remote id.</summary>
    public static bool TryToRemoteId(string? feedAddress, out string remoteId)
    {
        remoteId = string.Empty;
        var address = feedAddress?.Trim();
        if (string.IsNullOrEmpty(address))
        {
            return false;
        }

        if (TryResolveLocalPath(address, out _))
        {
            remoteId = LocalFileRemoteId;
            return true;
        }

        if (!TryCreateHttpUri(address, out var uri))
        {
            return false;
        }

        remoteId = uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        return true;
    }

    /// <summary>
    /// True when <paramref name="remoteId"/> is safe to persist for an ICS account: empty, the
    /// local-file marker, or a bare HTTPS origin with no path, query, fragment or user-info.
    /// </summary>
    public static bool IsSafeRemoteId(string? remoteId)
    {
        if (string.IsNullOrWhiteSpace(remoteId))
        {
            return true;
        }

        var value = remoteId.Trim();
        if (string.Equals(value, LocalFileRemoteId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return uri.AbsolutePath is "" or "/";
    }

    /// <summary>
    /// Creates the secret that carries the full feed address. The address is validated first, so
    /// a malformed value cannot be stored as a secret by accident.
    /// </summary>
    public static ProviderSecret CreateSecret(string feedAddress)
    {
        ToRemoteId(feedAddress);
        return new ProviderSecret { Resource = feedAddress.Trim() };
    }

    /// <summary>
    /// Resolves an absolute local <c>.ics</c> path or a <c>file:</c> URI. Relative paths are not
    /// accepted; the local-file mode is deliberately explicit.
    /// </summary>
    public static bool TryResolveLocalPath(string? feedAddress, out string path)
    {
        path = string.Empty;
        var value = feedAddress?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

#pragma warning disable CA1031 // Any malformed path is simply "not a local feed".
        try
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                if (!string.IsNullOrEmpty(uri.UserInfo) ||
                    (!string.IsNullOrEmpty(uri.Host) &&
                     !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                return TryAcceptLocalPath(uri.LocalPath, out path);
            }

            return TryAcceptLocalPath(value, out path);
        }
        catch (Exception)
        {
            // Not a usable local path.
        }
#pragma warning restore CA1031

        return false;
    }

    /// <summary>
    /// Parses an absolute HTTPS feed URL. Plain <c>http</c>, other schemes, relative addresses and
    /// URLs with embedded user-info are rejected.
    /// </summary>
    public static bool TryCreateHttpUri(string? feedAddress, out Uri uri)
    {
        uri = null!;
        var value = feedAddress?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate))
        {
            return false;
        }

        if (candidate.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(candidate.Host))
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

    private static bool TryAcceptLocalPath(string candidate, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate) ||
            !Path.IsPathFullyQualified(candidate) ||
            !candidate.EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        path = Path.GetFullPath(candidate);
        return true;
    }
}
