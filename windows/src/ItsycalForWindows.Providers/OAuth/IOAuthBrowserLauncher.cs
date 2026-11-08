namespace ItsycalForWindows.Providers;

/// <summary>Opens an OAuth authorization URL in the user's system browser.</summary>
/// <remarks>
/// The launcher is injectable so the loopback flow can be tested offline without opening a
/// browser. The default implementation uses the Windows shell; the app never embeds a WebView for
/// OAuth (ADR 0005 §4.1). Implementations must not log the URL because it contains the OAuth
/// <c>state</c> and PKCE challenge.
/// </remarks>
public interface IOAuthBrowserLauncher
{
    /// <summary>Launches <paramref name="authorizationUrl"/> in the default browser.</summary>
    Task LaunchAsync(Uri authorizationUrl, CancellationToken cancellationToken = default);
}
