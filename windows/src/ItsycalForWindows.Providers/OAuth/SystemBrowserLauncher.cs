using System.Diagnostics;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Default browser launcher: hands an <c>http(s)</c> authorization URL to the Windows shell, which
/// opens the user's default browser and keeps cookies, MFA and password managers in the trusted
/// browser (ADR 0005 §4.1).
/// </summary>
/// <remarks>
/// A WinRT <c>Launcher.LaunchUriAsync</c> call would need package identity and a WinRT projection;
/// the shell <see cref="ProcessStartInfo.UseShellExecute"/> path works identically in the packaged
/// extension and in tests. No URL is logged or written anywhere.
/// </remarks>
public sealed class SystemBrowserLauncher : IOAuthBrowserLauncher
{
    /// <inheritdoc />
    public Task LaunchAsync(Uri authorizationUrl, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizationUrl);
        cancellationToken.ThrowIfCancellationRequested();

        if (!authorizationUrl.IsAbsoluteUri ||
            (authorizationUrl.Scheme != Uri.UriSchemeHttps && authorizationUrl.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Only an absolute http(s) URL can open the system browser.", nameof(authorizationUrl));
        }

        using var process = Process.Start(new ProcessStartInfo(authorizationUrl.AbsoluteUri)
        {
            UseShellExecute = true,
            Verb = "open",
        });

        if (process is null)
        {
            throw new InvalidOperationException("The system browser could not be started.");
        }

        return Task.CompletedTask;
    }
}
