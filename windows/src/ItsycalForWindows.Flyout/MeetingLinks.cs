using System;
using System.Diagnostics.CodeAnalysis;

namespace ItsycalForWindows.Flyout;

/// <summary>
/// Meeting-link policy: only absolute http/https URIs are launchable/copyable. A calendar
/// provider can hand us any URI (file:, ms-*, tel:, malformed text); treating those as a
/// meeting link would surface Join/Copy actions that either do nothing or open an unintended
/// handler, so they are treated as "no link".
/// </summary>
internal static class MeetingLinks
{
    public static bool IsUsable(string? raw) => TryGetUsableUri(raw, out _);

    public static bool TryGetUsableUri(string? raw, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var candidate))
        {
            return false;
        }

        if (!string.Equals(candidate.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        uri = candidate;
        return true;
    }
}
