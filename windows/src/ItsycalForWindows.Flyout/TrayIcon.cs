using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace ItsycalForWindows.Flyout;

/// <summary>
/// Minimal Win32 notification-area icon + global hotkey for the standalone flyout.
///
/// The icon and hotkey are attached to a dedicated hidden top-level Win32 window rather than to
/// the WinUI window: replacing a WinUI 3 window's WndProc is unsupported, and a top-level hidden
/// window receives the Explorer <c>TaskbarCreated</c> broadcast after a shell restart. It is
/// created on the UI thread and is therefore pumped by the WinUI dispatcher message loop.
///
/// Lifecycle hardening: the RegisterClassEx atom is checked (a duplicate class is accepted),
/// Shell_NotifyIcon is retried with exponential backoff, the icon is re-added after an Explorer
/// restart, a failed hotkey registration is logged once and surfaced as a tray balloon, and
/// every icon handle this class owns is destroyed.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int WmApp = 0x8000;
    private const int TrayCallbackMessage = WmApp + 1;
    private const int HotkeyId = 0x4954;
    private const int TrayIconId = 1;
    private const int WmHotkey = 0x0312;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const int NimAdd = 0;
    private const int NimModify = 1;
    private const int NimDelete = 2;
    private const int NifMessage = 0x1;
    private const int NifIcon = 0x2;
    private const int NifTip = 0x4;
    private const int NifInfo = 0x10;
    private const int NiifInfo = 0x1;
    private const int NotificationBalloonTimeoutMs = 10000;
    private const int ErrorClassAlreadyExists = 1410;
    private const int MaxIconAddAttempts = 6;
    private const int RetryBaseDelayMs = 500;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VirtualKeyC = 0x43;
    private const string HostClassName = "ItsycalForWindows.Flyout.TrayHost";

    private readonly DispatcherQueue _dispatcher;
    private readonly Action _trayToggle;
    private readonly Action _hotkeyToggle;
    private readonly Action _exit;
    private readonly WndProcDelegate _wndProc;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private readonly bool _hostCreated;
    private readonly uint _taskbarCreatedMessage;
    private bool _hotkeyRegistered;
    private bool _iconAdded;
    private bool _hotkeyFailureBalloonPending;
    private bool _disposed;
    private DispatcherQueueTimer? _retryTimer;
    private int _retryAttempt;

    public TrayIcon(DispatcherQueue dispatcher, Action trayToggle, Action hotkeyToggle, Action exit)
    {
        _dispatcher = dispatcher;
        _trayToggle = trayToggle;
        _hotkeyToggle = hotkeyToggle;
        _exit = exit;
        _wndProc = WndProc;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        try
        {
            _hwnd = CreateHostWindow();
            _hostCreated = true;
        }
        catch (Win32Exception ex)
        {
            // A broken tray host must not take the app down: the flyout still opens, the
            // in-flyout status line explains the missing tray, and Ctrl+Q/Close still quit.
            DiagnosticLog.Write($"TRAY host failed error={ex.NativeErrorCode}");
            _hwnd = IntPtr.Zero;
            _hostCreated = false;
            return;
        }

        _icon = LoadTrayIcon(out _ownsIcon);

        if (TryAddIcon(out var iconError))
        {
            _iconAdded = true;
        }
        else
        {
            DiagnosticLog.Write($"TRAY icon add failed error={iconError}");
            ScheduleIconRetry();
        }

        _hotkeyRegistered = RegisterHotKey(_hwnd, HotkeyId, ModControl | ModAlt, VirtualKeyC); // Ctrl+Alt+C
        if (!_hotkeyRegistered)
        {
            var hotkeyError = Marshal.GetLastWin32Error();
            DiagnosticLog.Write($"HOTKEY register failed error={hotkeyError}");
            _hotkeyFailureBalloonPending = true;
            TryShowHotkeyBalloon();
        }

        DiagnosticLog.Write($"TRAY state host={_hostCreated} icon={_iconAdded} hotkey={_hotkeyRegistered}");
    }

    /// <summary>True once the shell accepted the notification icon (including later retries).</summary>
    public bool IsIconVisible => _iconAdded;

    /// <summary>False when Ctrl+Alt+C was claimed by another process.</summary>
    public bool HotkeyRegistered => _hotkeyRegistered;

    /// <summary>Raised on the UI thread when a retry or Explorer restart changes icon availability.</summary>
    public event EventHandler<bool>? IconAvailabilityChanged;

    /// <summary>
    /// Physical-pixel tray icon rectangle for flyout anchoring. False when the icon is hidden in
    /// the overflow area, the shell API is unavailable, or the icon is not currently registered.
    /// </summary>
    public bool TryGetIconRect(out TrayIconRect rect)
    {
        rect = default;
        if (!_iconAdded || _hwnd == IntPtr.Zero)
        {
            return false;
        }

        var identifier = new NotifyIconIdentifier
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            guidItem = Guid.Empty,
        };

        var result = Shell_NotifyIconGetRect(ref identifier, out var nativeRect);
        if (result < 0)
        {
            return false;
        }

        rect = new TrayIconRect(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
        return rect.IsValid;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _retryTimer?.Stop();
        _retryTimer = null;

        if (_iconAdded)
        {
            var data = CreateData();
            _ = Shell_NotifyIcon(NimDelete, ref data);
            _iconAdded = false;
        }

        if (_hotkeyRegistered)
        {
            _ = UnregisterHotKey(_hwnd, HotkeyId);
            _hotkeyRegistered = false;
        }

        if (_hwnd != IntPtr.Zero)
        {
            _ = DestroyWindow(_hwnd);
        }

        if (_ownsIcon && _icon != IntPtr.Zero)
        {
            _ = DestroyIcon(_icon);
        }

        IconAvailabilityChanged = null;
        GC.SuppressFinalize(this);
    }

    private bool TryAddIcon(out int error)
    {
        var data = CreateData();
        var added = Shell_NotifyIcon(NimAdd, ref data);
        error = added ? 0 : Marshal.GetLastWin32Error();
        return added;
    }

    private void ScheduleIconRetry()
    {
        if (_disposed || _iconAdded || !_hostCreated || _retryTimer is not null || _retryAttempt >= MaxIconAddAttempts)
        {
            return;
        }

        var delayMs = RetryBaseDelayMs * (1 << Math.Min(_retryAttempt, 4));
        _retryAttempt++;

        var timer = _dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(delayMs);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _retryTimer = null;
            if (_disposed || _iconAdded)
            {
                return;
            }

            if (TryAddIcon(out var error))
            {
                HandleIconAvailable();
            }
            else
            {
                DiagnosticLog.Write($"TRAY icon retry failed attempt={_retryAttempt} error={error}");
                ScheduleIconRetry();
            }
        };

        _retryTimer = timer;
        timer.Start();
    }

    private void HandleIconAvailable()
    {
        _iconAdded = true;
        _retryAttempt = 0;
        IconAvailabilityChanged?.Invoke(this, true);
        TryShowHotkeyBalloon();
    }

    private void OnTaskbarCreated()
    {
        if (_disposed || !_hostCreated)
        {
            return;
        }

        // Explorer owns the notification area; after a restart every icon must be added again.
        DiagnosticLog.Write("TRAY taskbar-created");
        _retryTimer?.Stop();
        _retryTimer = null;
        _iconAdded = false;
        _retryAttempt = 0;
        if (TryAddIcon(out var error))
        {
            HandleIconAvailable();
        }
        else
        {
            DiagnosticLog.Write($"TRAY re-add failed error={error}");
            IconAvailabilityChanged?.Invoke(this, false);
            ScheduleIconRetry();
        }
    }

    private void TryShowHotkeyBalloon()
    {
        if (!_hotkeyFailureBalloonPending || !_iconAdded)
        {
            return;
        }

        _hotkeyFailureBalloonPending = false;
        _ = ShowBalloon(
            "Itsycal",
            "Ctrl+Alt+C is already in use. Open Itsycal from the tray icon; press Ctrl+Q to quit.");
    }

    private bool ShowBalloon(string title, string message)
    {
        if (!_iconAdded)
        {
            return false;
        }

        var data = CreateData();
        data.uFlags = NifInfo;
        data.szInfoTitle = title;
        data.szInfo = message;
        data.dwInfoFlags = NiifInfo;
        data.uTimeoutOrVersion = NotificationBalloonTimeoutMs;
        return Shell_NotifyIcon(NimModify, ref data);
    }

    private NotifyIconData CreateData()
    {
        return new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _icon,
            szTip = "Itsycal — quick calendar (Ctrl+Alt+C)",
        };
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_taskbarCreatedMessage != 0 && msg == _taskbarCreatedMessage)
        {
            OnTaskbarCreated();
            return IntPtr.Zero;
        }

        if (msg == TrayCallbackMessage)
        {
            switch (unchecked((int)lParam.ToInt64()) & 0xFFFF)
            {
                case WmLButtonUp:
                    _trayToggle();
                    return IntPtr.Zero;
                case WmRButtonUp:
                    _exit();
                    return IntPtr.Zero;
            }
        }
        else if (msg == WmHotkey && wParam.ToInt64() == HotkeyId)
        {
            _hotkeyToggle();
            return IntPtr.Zero;
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private IntPtr CreateHostWindow()
    {
        var hInstance = GetModuleHandle(null);
        var wndClass = new WindowClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WindowClassEx>(),
            hInstance = hInstance,
            lpszClassName = HostClassName,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
        };

        var atom = RegisterClassEx(ref wndClass);
        if (atom == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, "Could not register the tray host window class.");
            }
        }

        var hwnd = CreateWindowEx(
            0,
            HostClassName,
            "Itsycal tray host",
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the tray host window.");
        }

        return hwnd;
    }

    private static IntPtr LoadTrayIcon(out bool ownsIcon)
    {
        ownsIcon = false;
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                _ = ExtractIconEx(exe, 0, out var large, out var small, 1);

                // ExtractIconEx hands out two owned handles; keep one and destroy the other so a
                // refresh or a second tray object cannot leak icon handles.
                if (small != IntPtr.Zero)
                {
                    ownsIcon = true;
                    if (large != IntPtr.Zero)
                    {
                        _ = DestroyIcon(large);
                    }

                    return small;
                }

                if (large != IntPtr.Zero)
                {
                    ownsIcon = true;
                    return large;
                }
            }
        }
        catch
        {
            // Fall through to the shared generic application icon.
        }

        return LoadIcon(IntPtr.Zero, (IntPtr)32512);
    }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr largeIcon, out IntPtr smallIcon, uint count);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

/// <summary>Physical-pixel tray icon bounds returned by Shell_NotifyIconGetRect.</summary>
internal readonly record struct TrayIconRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsValid => Width > 0 && Height > 0;
}
