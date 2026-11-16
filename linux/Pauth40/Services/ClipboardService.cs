using System;
using Windows.ApplicationModel.DataTransfer;

namespace Pauth40.Services;

/// <summary>Cross-platform clipboard bridge (Windows, Skia desktop, mobile and WebAssembly).</summary>
public sealed class ClipboardService
{
    public bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);

            // Flush keeps the value available after the application closes on
            // platforms that support it. Some sandboxes do not, and that must
            // never turn a successful copy into an error.
            try
            {
                Clipboard.Flush();
            }
            catch
            {
                // Best effort only.
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
