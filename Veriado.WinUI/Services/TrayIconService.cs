using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Veriado.WinUI.Services;

internal sealed class TrayIconService : IDisposable
{
    private const int CallbackMessageId = Win32Values.WM_APP + 1;
    private const int TrayIconId = 1;
    private const int CommandOpen = 1001;
    private const int CommandRestart = 1002;
    private const int CommandExit = 1003;

    private readonly TrayIconOptions _options;
    private readonly Action _openAction;
    private readonly Action _restartAction;
    private readonly Action _exitAction;
    private readonly Win32Values.WndProc _wndProc;

    private nint _windowHandle;
    private nint _iconHandle;
    private nint _menuHandle;
    private ushort _classAtom;
    private bool _iconAdded;

    public TrayIconService(TrayIconOptions options, Action openAction, Action restartAction, Action exitAction)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _openAction = openAction ?? throw new ArgumentNullException(nameof(openAction));
        _restartAction = restartAction ?? throw new ArgumentNullException(nameof(restartAction));
        _exitAction = exitAction ?? throw new ArgumentNullException(nameof(exitAction));
        _wndProc = WindowProc;
    }

    public void Initialize()
    {
        if (_windowHandle != 0)
        {
            return;
        }

        RegisterWindowClass();
        CreateMessageWindow();
        CreateContextMenu();
        LoadIcon();
        AddOrUpdateIcon(Win32Values.NIM_ADD);
    }

    public void Dispose()
    {
        RemoveIcon();

        if (_menuHandle != 0)
        {
            _ = Win32Values.DestroyMenu(_menuHandle);
            _menuHandle = 0;
        }

        if (_iconHandle != 0)
        {
            _ = Win32Values.DestroyIcon(_iconHandle);
            _iconHandle = 0;
        }

        if (_windowHandle != 0)
        {
            _ = Win32Values.DestroyWindow(_windowHandle);
            _windowHandle = 0;
        }

        if (_classAtom != 0)
        {
            _ = Win32Values.UnregisterClass((nint)_classAtom, Win32Values.GetModuleHandle(null));
            _classAtom = 0;
        }

        GC.SuppressFinalize(this);
    }

    private void RegisterWindowClass()
    {
        var wndClass = new Win32Values.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Win32Values.WNDCLASSEXW>(),
            hInstance = Win32Values.GetModuleHandle(null),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            lpszClassName = "Veriado.TrayMessageWindow",
        };

        _classAtom = Win32Values.RegisterClassEx(ref wndClass);
        if (_classAtom == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register tray window class");
        }
    }

    private void CreateMessageWindow()
    {
        _windowHandle = Win32Values.CreateWindowEx(
            0,
            (nint)_classAtom,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            Win32Values.HWND_MESSAGE,
            0,
            Win32Values.GetModuleHandle(null),
            0);

        if (_windowHandle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create tray message window");
        }
    }

    private void CreateContextMenu()
    {
        _menuHandle = Win32Values.CreatePopupMenu();
        if (_menuHandle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create tray context menu");
        }

        Win32Values.AppendMenu(_menuHandle, Win32Values.MF_STRING, CommandOpen, "Otevřít Veriado");
        Win32Values.AppendMenu(_menuHandle, Win32Values.MF_STRING, CommandRestart, "Restartovat");
        Win32Values.AppendMenu(_menuHandle, Win32Values.MF_STRING, CommandExit, "Ukončit");
    }

    private void LoadIcon()
    {
        if (!string.IsNullOrWhiteSpace(_options.IconPath))
        {
            _iconHandle = Win32Values.LoadImage(
                0,
                _options.IconPath!,
                Win32Values.IMAGE_ICON,
                0,
                0,
                Win32Values.LR_DEFAULTSIZE | Win32Values.LR_LOADFROMFILE);
        }

        if (_iconHandle == 0)
        {
            _iconHandle = Win32Values.LoadIcon(0, (nint)Win32Values.IDI_APPLICATION);
        }
    }

    private void AddOrUpdateIcon(uint message)
    {
        var data = new Win32Values.NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<Win32Values.NOTIFYICONDATAW>(),
            hWnd = _windowHandle,
            uID = TrayIconId,
            uFlags = Win32Values.NIF_MESSAGE | Win32Values.NIF_ICON | Win32Values.NIF_TIP,
            uCallbackMessage = CallbackMessageId,
            hIcon = _iconHandle,
            szTip = _options.Tooltip ?? string.Empty,
        };

        _iconAdded = Win32Values.Shell_NotifyIcon(message, ref data);
    }

    private void RemoveIcon()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = new Win32Values.NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<Win32Values.NOTIFYICONDATAW>(),
            hWnd = _windowHandle,
            uID = TrayIconId,
        };

        _ = Win32Values.Shell_NotifyIcon(Win32Values.NIM_DELETE, ref data);
        _iconAdded = false;
    }

    private nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case CallbackMessageId:
                HandleTrayCallback(lParam);
                break;
            case Win32Values.WM_COMMAND:
                HandleCommand(wParam);
                break;
            case Win32Values.WM_DESTROY:
                RemoveIcon();
                break;
        }

        return Win32Values.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void HandleTrayCallback(nint lParam)
    {
        var eventId = (uint)lParam;
        switch (eventId)
        {
            case Win32Values.WM_LBUTTONUP:
            case Win32Values.WM_LBUTTONDBLCLK:
                _openAction();
                break;
            case Win32Values.WM_RBUTTONUP:
                ShowContextMenu();
                break;
        }
    }

    private void HandleCommand(nint wParam)
    {
        var commandId = (int)(wParam.ToInt64() & 0xFFFF);

        switch (commandId)
        {
            case CommandOpen:
                _openAction();
                break;
            case CommandRestart:
                _restartAction();
                break;
            case CommandExit:
                _exitAction();
                break;
        }
    }

    private void ShowContextMenu()
    {
        if (_menuHandle == 0)
        {
            return;
        }

        if (!Win32Values.GetCursorPos(out var point))
        {
            return;
        }

        Win32Values.SetForegroundWindow(_windowHandle);
        var commandId = Win32Values.TrackPopupMenuEx(
            _menuHandle,
            Win32Values.TPM_RETURNCMD | Win32Values.TPM_RIGHTBUTTON,
            point.X,
            point.Y,
            _windowHandle,
            0);

        if (commandId != 0)
        {
            HandleCommand((nint)commandId);
        }
    }
}

internal sealed record TrayIconOptions(string? IconPath, string Tooltip);

internal static class Win32Values
{
    public const int WM_COMMAND = 0x0111;
    public const int WM_DESTROY = 0x0002;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_APP = 0x8000;

    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;

    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;

    public const int TPM_RIGHTBUTTON = 0x0002;
    public const int TPM_RETURNCMD = 0x0100;

    public const int MF_STRING = 0x0000;

    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x00000010;
    public const uint LR_DEFAULTSIZE = 0x00000040;

    public const int IDI_APPLICATION = 0x7F00;

    public static readonly nint HWND_MESSAGE = (nint)(-3);

    public delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string? lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

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
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint CreateWindowEx(
        int dwExStyle,
        nint lpClassName,
        string lpWindowName,
        int dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(nint hIcon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenu(nint hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int TrackPopupMenuEx(nint hmenu, uint fuFlags, int x, int y, nint hwnd, nint lptpm);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern nint DefWindowProc(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint LoadImage(nint hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint LoadIcon(nint hInstance, nint lpIconName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool UnregisterClass(nint lpClassName, nint hInstance);

}
