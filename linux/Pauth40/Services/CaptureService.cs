using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SkiaSharp;

namespace Pauth40.Services;

/// <summary>
/// Captures the screen where the platform allows it. Windows uses GDI, Linux
/// and macOS use the usual command line tools, and mobile/WebAssembly fall back
/// to the image picker. The result is always PNG bytes, so the rest of the
/// application does not depend on a platform bitmap type.
/// </summary>
public sealed class CaptureService
{
    public async Task<byte[]?> CaptureScreenAsync()
    {
#if PAUTH40_WINDOWS
        try
        {
            return await Task.Run(CaptureWindows);
        }
        catch
        {
            return null;
        }
#elif PAUTH40_DESKTOP
        try
        {
            return await CaptureDesktopAsync();
        }
        catch
        {
            return null;
        }
#else
        await Task.CompletedTask;
        return null;
#endif
    }

#if PAUTH40_DESKTOP && !PAUTH40_WINDOWS
    private static async Task<byte[]?> CaptureDesktopAsync()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"pauth40-capture-{Guid.NewGuid():N}.png");

        (string Command, string Arguments)[] tools = OperatingSystem.IsMacOS()
            ? new[] { ("screencapture", $"-x \"{path}\"") }
            : new[]
            {
                ("gnome-screenshot", $"-f \"{path}\""),
                ("spectacle", $"-b -n -o \"{path}\""),
                ("grim", $"\"{path}\""),
                ("scrot", $"\"{path}\""),
                ("import", $"-window root \"{path}\"")
            };

        foreach ((string command, string arguments) in tools)
        {
            if (await TryRunAsync(command, arguments))
            {
                try
                {
                    if (File.Exists(path) && new FileInfo(path).Length > 0)
                        return await File.ReadAllBytesAsync(path);
                }
                catch
                {
                    // Try the next tool.
                }
            }
        }

        return null;
    }

    private static async Task<bool> TryRunAsync(string command, string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            using Process? process = Process.Start(startInfo);
            if (process is null)
                return false;

            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(8));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(true);
                }
                catch
                {
                    // The process may have exited already.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
#endif

#if PAUTH40_WINDOWS
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint BiRgb = 0;
    private const uint DibRgbColors = 0;
    private const uint Srccopy = 0x00CC0020;

    private static byte[]? CaptureWindows()
    {
        try
        {
            SetProcessDPIAware();
        }
        catch
        {
            // Already DPI aware.
        }

        int x = GetSystemMetrics(SmXVirtualScreen);
        int y = GetSystemMetrics(SmYVirtualScreen);
        int width = GetSystemMetrics(SmCxVirtualScreen);
        int height = GetSystemMetrics(SmCyVirtualScreen);

        if (width <= 0 || height <= 0)
            return null;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            return null;

        IntPtr memoryDc = IntPtr.Zero;
        IntPtr bitmap = IntPtr.Zero;
        IntPtr previous = IntPtr.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);

            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
                return null;

            previous = SelectObject(memoryDc, bitmap);

            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, Srccopy))
                return null;

            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = BiRgb,
                    SizeImage = (uint)(width * height * 4)
                }
            };

            byte[] pixels = new byte[width * height * 4];
            int lines = GetDIBits(
                memoryDc,
                bitmap,
                0,
                (uint)height,
                pixels,
                ref info,
                DibRgbColors);

            if (lines == 0)
                return null;

            using var skBitmap = new SKBitmap(
                new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));

            Marshal.Copy(pixels, 0, skBitmap.GetPixels(), pixels.Length);

            using var image = SKImage.FromBitmap(skBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 92);
            return data.ToArray();
        }
        finally
        {
            if (previous != IntPtr.Zero && memoryDc != IntPtr.Zero)
                SelectObject(memoryDc, previous);

            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);

            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);

            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDPIAware();

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        IntPtr dest,
        int x,
        int y,
        int width,
        int height,
        IntPtr source,
        int sourceX,
        int sourceY,
        uint operation);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc,
        IntPtr bitmap,
        uint start,
        uint lines,
        byte[] bits,
        ref BitmapInfo info,
        uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }
#endif
}
