using System.Runtime.InteropServices.WindowsRuntime;
using global::Windows.Storage;

namespace OptiCopy.Windows.Pages;

/// <summary>
/// Uses the Windows Storage API for files selected through FileOpenPicker.
/// This avoids depending on direct filesystem access to StorageFile.Path,
/// which can behave differently between WinUI development launch and the
/// packaged/published executable.
/// </summary>
internal static class File
{
    public static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();

        var storageFile = await StorageFile.GetFileFromPathAsync(path);
        var buffer = await FileIO.ReadBufferAsync(storageFile);
        cancellationToken.ThrowIfCancellationRequested();
        return buffer.ToArray();
    }
}
