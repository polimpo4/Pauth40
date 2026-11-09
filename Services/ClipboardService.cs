using System.Windows;

namespace Pauth40.Services;

public sealed class ClipboardService
{
    public bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                Thread.Sleep(35);
            }
        }

        return false;
    }
}
