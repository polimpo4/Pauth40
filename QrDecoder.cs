using System.Drawing;
using System.Drawing.Drawing2D;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace Pauth40;

internal static class QrDecoder
{
    public static string? DecodeFile(string path)
    {
        try
        {
            using var bitmap = new Bitmap(path);
            return Decode(bitmap);
        }
        catch
        {
            return null;
        }
    }

    public static string? Decode(Bitmap bitmap)
    {
        try
        {
            var reader = CreateReader();
            var result = reader.Decode(bitmap);
            if (result != null)
                return result.Text;

            // Small QR captures sometimes need a little help.
            if (bitmap.Width < 900 || bitmap.Height < 900)
            {
                using var enlarged = Enlarge(bitmap, 3);
                result = reader.Decode(enlarged);
                if (result != null)
                    return result.Text;
            }

            // Try the center crop as a fallback (screenshot with surrounding UI).
            using var center = CenterCrop(bitmap, 0.82f);
            result = reader.Decode(center);
            if (result != null)
                return result.Text;

            if (bitmap.Width < 1800 || bitmap.Height < 1800)
            {
                using var enlargedCenter = Enlarge(center, 2);
                result = reader.Decode(enlargedCenter);
                if (result != null)
                    return result.Text;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static ZXing.Windows.Compatibility.BarcodeReader CreateReader()
    {
        return new ZXing.Windows.Compatibility.BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
            }
        };
    }

    private static Bitmap Enlarge(Bitmap source, int factor)
    {
        var enlarged = new Bitmap(source.Width * factor, source.Height * factor);
        using var graphics = Graphics.FromImage(enlarged);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(source, 0, 0, enlarged.Width, enlarged.Height);
        return enlarged;
    }

    private static Bitmap CenterCrop(Bitmap source, float factor)
    {
        int width = Math.Max(1, (int)(source.Width * factor));
        int height = Math.Max(1, (int)(source.Height * factor));
        int x = (source.Width - width) / 2;
        int y = (source.Height - height) / 2;

        var cropped = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(cropped);
        graphics.DrawImage(source, new Rectangle(0, 0, width, height), new Rectangle(x, y, width, height), GraphicsUnit.Pixel);
        return cropped;
    }
}
