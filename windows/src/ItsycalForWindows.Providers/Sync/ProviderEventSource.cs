using ItsycalForWindows.Models;
using ItsycalForWindows.Services;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Thin <see cref="IEventSource"/> adapter: refreshes the provider accounts for a visible range and
/// returns the merged Core <see cref="CalendarEvent"/> values, so the calendar surfaces can add
/// providers to a <see cref="CompositeEventSource"/> exactly like the local sources.
/// </summary>
/// <remarks>
/// <para>
/// The adapter follows the <see cref="IEventSource"/> contract: an unavailable provider or a
/// refresh failure yields an empty list instead of throwing, while caller cancellation still
/// propagates. Each account's failure is already isolated by <see cref="ProviderSyncService"/>, so
/// one broken account never hides another account's or the local calendar's events.
/// </para>
/// <para>
/// UI wiring is deliberately not part of this slice: neither the Command Palette extension nor the
/// standalone flyout references the provider library yet (see spec 0006). This adapter is the
/// integration point the settings/composition work will use.
/// </para>
/// </remarks>
public sealed class ProviderEventSource : IEventSource
{
    private readonly ProviderSyncService _syncService;
    private readonly bool _forceRefresh;

    /// <summary>Creates the adapter over a sync service.</summary>
    /// <param name="syncService">Provider refresh service that owns accounts and secrets.</param>
    /// <param name="forceRefresh">
    /// True for a "refresh now" source that bypasses both the event cache and provider validators.
    /// </param>
    public ProviderEventSource(ProviderSyncService syncService, bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(syncService);
        _syncService = syncService;
        _forceRefresh = forceRefresh;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        DateOnly fromInclusive,
        DateOnly toExclusive,
        CancellationToken cancellationToken = default)
    {
        if (toExclusive <= fromInclusive)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();

#pragma warning disable CA1031 // IEventSource contract: an unavailable source returns no events.
        try
        {
            var result = await _syncService
                .RefreshAsync(new ProviderEventRange(fromInclusive, toExclusive), _forceRefresh, cancellationToken)
                .ConfigureAwait(false);

            return result.Events;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
#pragma warning restore CA1031
    }
}
