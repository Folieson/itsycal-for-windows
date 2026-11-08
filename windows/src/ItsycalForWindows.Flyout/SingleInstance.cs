using System;
using System.Threading;

namespace ItsycalForWindows.Flyout;

/// <summary>
/// One tray app per user session. The first process owns the named mutex; a later launch
/// finds it, signals the named event and exits before any window, tray icon or hotkey is
/// created. The owner waits on the event on the thread pool and marshals show/activate to
/// its UI thread.
///
/// The "Local\" prefix scopes both objects to the logon session, so two users can each run
/// their own flyout. Named kernel objects are not a place for user data; the names are
/// constants.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\Calwren.Flyout";
    private const string ShowEventName = @"Local\Calwren.Flyout.Show";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _wait;
    private readonly object _handlerLock = new();
    private Action? _showHandler;
    private bool _showPending;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
        _wait = ThreadPool.RegisterWaitForSingleObject(
            showEvent,
            OnShowEvent,
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>
    /// Returns null when another process already owns the single-instance mutex
    /// (<paramref name="alreadyRunning"/> is then true) or when the kernel objects cannot be
    /// created at all (<paramref name="alreadyRunning"/> is false: run as a normal instance).
    /// The second process must call <see cref="SignalExistingInstance"/> and exit without UI.
    /// </summary>
    public static SingleInstance? TryAcquire(out bool alreadyRunning)
    {
        alreadyRunning = false;
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                alreadyRunning = true;
                return null;
            }
        }
        catch (Exception)
        {
            // If the kernel object cannot be created, refusing to start is worse than running
            // a second window. Best-effort single instance only.
            DiagnosticLog.Write("SINGLE mutex unavailable");
            return null;
        }

        try
        {
            var showEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                ShowEventName,
                out _);
            return new SingleInstance(mutex, showEvent);
        }
        catch (Exception)
        {
            DiagnosticLog.Write("SINGLE show-event unavailable");
            mutex.Dispose();
            return null;
        }
    }

    /// <summary>
    /// Registers the UI action that shows/activates the flyout. Signals that arrived before
    /// the window existed are replayed once the handler is installed.
    /// </summary>
    public void SetShowHandler(Action showHandler)
    {
        lock (_handlerLock)
        {
            _showHandler = showHandler;
            if (!_showPending || _disposed)
            {
                return;
            }

            _showPending = false;
            showHandler();
        }
    }

    /// <summary>Best-effort wake-up call from the second process.</summary>
    public static void SignalExistingInstance()
    {
        // The owner may still be constructing the named event when a second launch lands;
        // retry briefly instead of losing the activation (the mutex is created first).
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var showEvent))
                {
                    using (showEvent)
                    {
                        showEvent.Set();
                    }

                    return;
                }
            }
            catch (Exception)
            {
                // The owner may be exiting; fall through to the next attempt.
            }

            Thread.Sleep(50);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wait.Unregister(null);
        _showEvent.Dispose();
        _mutex.Dispose();
    }

    private void OnShowEvent(object? state, bool timedOut)
    {
        if (timedOut || _disposed)
        {
            return;
        }

        Action? handler;
        lock (_handlerLock)
        {
            if (_disposed)
            {
                return;
            }

            handler = _showHandler;
            if (handler is null)
            {
                _showPending = true;
                return;
            }
        }

        try
        {
            handler();
        }
        catch (Exception)
        {
            // Never let a wake-up callback take down the app.
        }
    }
}
