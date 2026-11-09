using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using Pauth40.Views;

namespace Pauth40.Services;

public sealed class CaptureService
{
    public async Task<Bitmap?> CaptureAsync(Window owner)
    {
        owner.Hide();
        await Task.Delay(180);

        Bitmap? screen = null;
        CaptureOverlayWindow? overlay = null;

        try
        {
            var (x, y, width, height) = NativeMethods.GetVirtualScreenPixels();

            screen = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(screen))
            {
                graphics.CopyFromScreen(x, y, 0, 0, screen.Size, CopyPixelOperation.SourceCopy);
            }

            overlay = new CaptureOverlayWindow(screen, new Rectangle(x, y, width, height));
            screen = null; // ownership moved to the overlay

            bool? result = overlay.ShowDialog();
            return result == true ? overlay.CapturedImage : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            overlay?.Dispose();
            screen?.Dispose();

            if (!owner.IsVisible)
                owner.Show();

            owner.Activate();
        }
    }
}
