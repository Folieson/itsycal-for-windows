using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ItsycalForWindows.Flyout;

/// <summary>
/// Minimal Win32 notification-area icon + global hotkey for the spike. A vetted tray abstraction
/// belongs in the production app; here we only need a real tray icon, tooltip, click callback and
/// the Ctrl+Alt+C hotkey.
///
/// The icon and hotkey are attached to a dedicated hidden Win32 window rather than to the WinUI
/// window: replacing a WinUI 3 window's WndProc is unsupported, so the hidden window keeps the
/// tray plumbing out of the XAML framework's message handling. It is created on the UI thread and
/// is therefore pumped by the WinUI dispatcher message loop.
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
    private const int NimDelete = 2;
    private const int NifMessage = 0x1;
    private const int NifIcon = 0x2;
    private const int NifTip = 0x4;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VirtualKeyC = 0x43;
    private const string HostClassName = "ItsycalForWindows.Flyout.TrayHost";

    private static TrayIcon? s_active;

    private readonly Action _toggle;
    private readonly Action _exit;
    private readonly WndProcDelegate _wndProc;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private readonly bool _hotkeyRegistered;
    private bool _iconAdded;
    private bool _disposed;

    public TrayIcon(Action toggle, Action exit)
    {
        _toggle = toggle;
        _exit = exit;
        _wndProc = WndProc;
        s_active = this;

        _hwnd = CreateHostWindow();
        _icon = LoadTrayIcon(out _ownsIcon);
        _iconAdded = TryAddIcon(out var error);
        _hotkeyRegistered = RegisterHotKey(_hwnd, HotkeyId, ModControl | ModAlt, VirtualKeyC); // Ctrl+Alt+C

        if (!_iconAdded)
        {
            // In the agent sandbox the process is forced to low integrity, where the shell refuses
            // notification icons with ERROR_ACCESS_DENIED. Keep the failure visible in the log.
            DiagnosticLog.Write($"Tray icon registration failed: {error}");
        }
    }

    /// <summary>
    /// Retries the notification icon registration once the UI message loop is running; the shell
    /// can reject the icon while the window is still being created.
    /// </summary>
    public void RetryAddIcon()
    {
        if (_disposed || _iconAdded)
        {
            return;
        }

        _iconAdded = TryAddIcon(out var error);
        if (!_iconAdded)
        {
            DiagnosticLog.Write($"Tray icon retry failed: {error}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_iconAdded)
        {
            var data = CreateData();
            _ = Shell_NotifyIcon(NimDelete, ref data);
        }

        if (_hotkeyRegistered)
        {
            _ = UnregisterHotKey(_hwnd, HotkeyId);
        }

        if (_hwnd != IntPtr.Zero)
        {
            _ = DestroyWindow(_hwnd);
        }

        if (_ownsIcon && _icon != IntPtr.Zero)
        {
            _ = DestroyIcon(_icon);
        }

        s_active = null;
    }

    private bool TryAddIcon(out int error)
    {
        var data = CreateData();
        var added = Shell_NotifyIcon(NimAdd, ref data);
        error = added ? 0 : Marshal.GetLastWin32Error();
        return added;
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
        if (msg == TrayCallbackMessage)
        {
            switch (unchecked((int)lParam.ToInt64()) & 0xFFFF)
            {
                case WmLButtonUp:
                    _toggle();
                    return IntPtr.Zero;
                case WmRButtonUp:
                    _exit();
                    return IntPtr.Zero;
            }
        }
        else if (msg == WmHotkey && wParam.ToInt64() == HotkeyId)
        {
            _toggle();
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

        // A duplicate class registration (e.g. a second instance in the same process) is harmless.
        _ = RegisterClassEx(ref wndClass);
        var error = Marshal.GetLastWin32Error();
        if (error != 0 && error != 1410 /* ERROR_CLASS_ALREADY_EXISTS */)
        {
            throw new Win32Exception(error, "Could not register the tray host window class.");
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
                if (small != IntPtr.Zero)
                {
                    ownsIcon = true;
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
            // Fall through to the generic application icon.
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

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
