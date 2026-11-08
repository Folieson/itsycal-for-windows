namespace ItsycalForWindows.Providers;

/// <summary>
/// Outcome of a provider probe or fetch. The state is explicit so the settings surface can show
/// "not configured", "not implemented", "unauthorized" and "unavailable" without guessing from an
/// empty event list (which is a valid, successful result).
/// </summary>
public enum ProviderState
{
    /// <summary>The provider is configured and returned a usable answer.</summary>
    Ready,

    /// <summary>The account exists but has no usable configuration or credentials yet.</summary>
    NotConfigured,

    /// <summary>The provider is a deliberate skeleton in this build; no network was attempted.</summary>
    NotImplemented,

    /// <summary>The remote service rejected the credentials (HTTP 401/403).</summary>
    Unauthorized,

    /// <summary>The provider could not be reached or timed out.</summary>
    Unavailable,

    /// <summary>The remote response was not a valid calendar feed.</summary>
    InvalidResponse,
}
