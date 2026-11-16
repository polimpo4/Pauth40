using System;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Pauth40;

/// <summary>
/// Cross-platform QR decoder. Images are decoded with SkiaSharp on every Uno
/// target and the pixels are handed to ZXing.Net, which has no platform
/// specific drawing dependency.
/// </summary>
internal static class QrDecoder
{
    public static string? DecodeFile(string path)
    {
        try
        {
            byte[] data = File.ReadAllBytes(path);
            return Decode(data);
        }
        catch
        {
            return null;
        }
    }

    public static string? Decode(byte[] encodedImage)
    {
        if (encodedImage is null || encodedImage.Length == 0)
            return null;

        try
        {
            using var decoded = SKBitmap.Decode(encodedImage);
            if (decoded is null)
                return null;

            return Decode(decoded);
        }
        catch
        {
            return null;
        }
    }

    public static string? Decode(SKBitmap bitmap)
    {
        try
        {
            return DecodeCore(bitmap) ?? DecodeUpscaled(bitmap);
        }
        catch
        {
            return null;
        }
    }

    private static string? DecodeCore(SKBitmap bitmap)
    {
        byte[] gray = ToGrayscale(bitmap, out int width, out int height);
        var source = new RGBLuminanceSource(gray, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
        return CreateReader().Decode(source)?.Text;
    }

    private static string? DecodeUpscaled(SKBitmap bitmap)
    {
        // Very small captures are hard for the detector. Scale them up once.
        if (bitmap.Width >= 900 && bitmap.Height >= 900)
            return null;

        float scale = 2.5f;

        try
        {
            using var resized = bitmap.Resize(
                new SKImageInfo(
                    (int)(bitmap.Width * scale),
                    (int)(bitmap.Height * scale),
                    bitmap.ColorType,
                    bitmap.AlphaType),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

            return resized is null ? null : DecodeCore(resized);
        }
        catch
        {
            return null;
        }
    }

    private static BarcodeReaderGeneric CreateReader() => new()
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            PossibleFormats = new[] { BarcodeFormat.QR_CODE }
        }
    };

    private static byte[] ToGrayscale(SKBitmap bitmap, out int width, out int height)
    {
        width = bitmap.Width;
        height = bitmap.Height;

        using var bgra = ConvertToBgra(bitmap);
        byte[] pixels = new byte[width * height];
        IntPtr source = bgra.GetPixels();
        int rowBytes = bgra.RowBytes;
        byte[] row = new byte[width * 4];

        for (int y = 0; y < height; y++)
        {
            Marshal.Copy(source + y * rowBytes, row, 0, row.Length);

            for (int x = 0; x < width; x++)
            {
                int index = x * 4;
                byte b = row[index];
                byte g = row[index + 1];
                byte r = row[index + 2];

                // Rec. 601 luminance, integer approximation.
                pixels[y * width + x] = (byte)((r * 299 + g * 587 + b * 114) / 1000);
            }
        }

        return pixels;
    }

    private static SKBitmap ConvertToBgra(SKBitmap bitmap)
    {
        if (bitmap.ColorType == SKColorType.Bgra8888)
            return bitmap.Copy();

        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var target = new SKBitmap(info);

        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(bitmap, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }

        return target;
    }
}
