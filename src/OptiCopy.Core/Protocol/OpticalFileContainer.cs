using System.Security.Cryptography;
using System.Text;

namespace OptiCopy.Core.Protocol;

/// <summary>
/// Decimen-compatible protected file container (DCF2).
/// Layout matches shared/protocol.ts in decimen-optical-transfer.
/// </summary>
public static class OpticalFileContainer
{
    private static readonly byte[] Magic = [0x44, 0x43, 0x46, 0x32]; // DCF2
    private const int HeaderLength = 49;

    public static byte[] Pack(string fileName, string mimeType, ReadOnlySpan<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        if (bytes.IsEmpty)
            throw new ArgumentException("The file is empty.", nameof(bytes));

        var safeName = SanitizeFileName(fileName);
        var nameBytes = Encoding.UTF8.GetBytes(safeName);
        var typeBytes = Encoding.UTF8.GetBytes(mimeType);
        if (nameBytes.Length > ushort.MaxValue)
            throw new ArgumentException("The file name is too long.", nameof(fileName));
        if (typeBytes.Length > ushort.MaxValue)
            throw new ArgumentException("The MIME type is too long.", nameof(mimeType));
        if (bytes.Length > uint.MaxValue)
            throw new NotSupportedException("The current wire format supports files up to 4 GiB.");

        var output = new byte[checked(HeaderLength + nameBytes.Length + typeBytes.Length + bytes.Length)];
        Magic.CopyTo(output, 0);
        output[4] = 0; // no compression; the receiver accepts this DCF2 mode.

        BitConverter.TryWriteBytes(output.AsSpan(5, 2), checked((ushort)nameBytes.Length));
        BitConverter.TryWriteBytes(output.AsSpan(7, 2), checked((ushort)typeBytes.Length));
        BitConverter.TryWriteBytes(output.AsSpan(9, 4), checked((uint)bytes.Length));
        BitConverter.TryWriteBytes(output.AsSpan(13, 4), checked((uint)bytes.Length));
        SHA256.HashData(bytes).CopyTo(output, 17);

        nameBytes.CopyTo(output, HeaderLength);
        typeBytes.CopyTo(output, HeaderLength + nameBytes.Length);
        bytes.CopyTo(output.AsSpan(HeaderLength + nameBytes.Length + typeBytes.Length));
        return output;
    }

    private static string SanitizeFileName(string name)
    {
        var baseName = name.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        var cleaned = new string(baseName.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned is "" or "." or ".." ? "transfer.bin" : cleaned;
    }
}
