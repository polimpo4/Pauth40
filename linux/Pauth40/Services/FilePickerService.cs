using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Pauth40.Services;

/// <summary>
/// File pickers that work on every Uno target. The WinRT pickers need a window
/// handle on the Windows App SDK, while the Uno implementations use the native
/// dialogs of the desktop, mobile and browser platforms.
/// </summary>
public static class FilePickerService
{
    private static readonly string[] ImageExtensions =
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"
    };

    public static async Task<byte[]?> PickImageAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };

            foreach (string extension in ImageExtensions)
                picker.FileTypeFilter.Add(extension);

            Initialize(picker);

            StorageFile? file = await picker.PickSingleFileAsync();
            return file is null ? null : await ReadAllBytesAsync(file);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<byte[]?> PickBackupAsync()
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };

            picker.FileTypeFilter.Add(".pauth40");
            picker.FileTypeFilter.Add("*");

            Initialize(picker);

            StorageFile? file = await picker.PickSingleFileAsync();
            return file is null ? null : await ReadAllBytesAsync(file);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Saves a backup through a picker and returns the user visible location, or
    /// an empty string when the operation was cancelled.
    /// </summary>
    public static async Task<string> SaveBackupAsync(string suggestedName, byte[] payload)
    {
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = suggestedName + ".pauth40"
            };

            picker.FileTypeChoices.Add(
                "Pauth40 backup",
                new List<string> { ".pauth40" });

            Initialize(picker);

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
                return string.Empty;

            await FileIO.WriteBytesAsync(file, payload);

            return string.IsNullOrWhiteSpace(file.Path) ? file.Name : file.Path;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(StorageFile file)
    {
        IBuffer buffer = await FileIO.ReadBufferAsync(file);
        byte[] bytes = new byte[buffer.Length];

        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static void Initialize(object picker)
    {
#if PAUTH40_WINDOWS
        try
        {
            if (App.Instance is { } window)
            {
                IntPtr handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
            }
        }
        catch
        {
            // The picker still works in unpackaged scenarios on newer builds.
        }
#endif
    }
}
