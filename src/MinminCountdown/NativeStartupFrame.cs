using System.Runtime.InteropServices;

namespace MinminCountdown;

internal static class NativeStartupFrame
{
    private const string ClassName = "MinminCountdownNativeStartupFrame";
    private const string WindowTitle = "敏敏子悬浮窗";
    private const int WindowWidth = 520;
    private const int WindowHeight = 390;
    private const uint WsPopup = 0x80000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsExToolwindow = 0x00000080;
    private const uint WsExTopmost = 0x00000008;
    private const uint WmDestroy = 0x0002;
    private const int SwShowNoactivate = 4;
    private static bool _classRegistered;
    private static readonly WindowProc WndProc = OnWindowMessage;

    internal static IntPtr Show()
    {
        var module = GetModuleHandleW(null);
        if (!_classRegistered)
        {
            var windowClass = new WndClassEx
            {
                cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProc),
                hInstance = module,
                lpszClassName = ClassName
            };
            if (RegisterClassExW(ref windowClass) == 0)
            {
                throw new InvalidOperationException("无法注册启动首帧窗口类。");
            }

            _classRegistered = true;
        }

        var handle = CreateWindowExW(
            WsExToolwindow | WsExTopmost,
            ClassName,
            WindowTitle,
            WsPopup | WsVisible,
            0,
            0,
            WindowWidth,
            WindowHeight,
            IntPtr.Zero,
            IntPtr.Zero,
            module,
            IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("无法创建启动首帧窗口。");
        }

        ShowWindow(handle, SwShowNoactivate);
        return handle;
    }

    internal static void Close(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            DestroyWindow(handle);
        }
    }

    private static IntPtr OnWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmDestroy)
        {
            return IntPtr.Zero;
        }

        return DefWindowProcW(window, message, wParam, lParam);
    }

    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint exStyle,
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

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? moduleName);
}
