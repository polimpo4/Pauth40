using System.Runtime.InteropServices;

namespace Pauth40;

internal static class NativeMethods
{
    // ---------------------------------------------------------------- DWM
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    public static void UseModernWindowFrame(IntPtr handle)
    {
        try
        {
            int dark = 1;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            int corner = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref corner, sizeof(int));

            // COLORREF 0x00BBGGRR -> #0C3754
            int border = unchecked((int)0x0054370C);
            DwmSetWindowAttribute(handle, DwmwaBorderColor, ref border, sizeof(int));
        }
        catch
        {
            // DWM is not available on older Windows versions.
        }
    }

    // ---------------------------------------------------------------- virtual screen
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public static (int X, int Y, int Width, int Height) GetVirtualScreenPixels()
    {
        return (
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen),
            Math.Max(1, GetSystemMetrics(SmCxVirtualScreen)),
            Math.Max(1, GetSystemMetrics(SmCyVirtualScreen)));
    }

    // ---------------------------------------------------------------- maximize to work area
    public const int WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public RectStruct Monitor;
        public RectStruct Work;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectStruct
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    public static void ConstrainMaximizedBounds(IntPtr handle, ref MinMaxInfo info)
    {
        try
        {
            IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
                return;

            var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
                return;

            var work = monitorInfo.Work;
            var screen = monitorInfo.Monitor;

            info.MaxPosition.X = work.Left - screen.Left;
            info.MaxPosition.Y = work.Top - screen.Top;
            info.MaxSize.X = work.Right - work.Left;
            info.MaxSize.Y = work.Bottom - work.Top;
        }
        catch
        {
            // ignore
        }
    }
}
