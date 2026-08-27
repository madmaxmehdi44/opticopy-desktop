using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace OptiCopy.Core.Protocol;

/// <summary>
/// Decimen-compatible DCF2 protected file container.
/// Layout matches shared/protocol.ts in decimen-optical-transfer.
/// </summary>
public static class OpticalFileContainer
{
    private static readonly byte[] Magic = [0x44, 0x43, 0x46, 0x32]; // DCF2
    public const int HeaderLength = 49;
    public const int MaxFileBytes = 64 * 1024 * 1024;

    public enum CompressionMode : byte
    {
        None = 0,
        Gzip = 1
    }

    public readonly record struct PackedFile(
        byte[] Container,
        CompressionMode Compression,
        int OriginalSize,
        int TransmittedSize);

    public readonly record struct UnpackedFile(
        string Name,
        string Type,
        byte[] Bytes,
        byte[] Sha256,
        CompressionMode Compression,
        int TransmittedSize);

    public static async Task<PackedFile> PackAsync(
        string fileName,
        string mimeType,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        if (bytes.IsEmpty)
            throw new ArgumentException("The file is empty.", nameof(bytes));
        if (bytes.Length > MaxFileBytes)
            throw new NotSupportedException("The current Decimen format supports files up to 64 MiB.");

        var safeName = SanitizeFileName(fileName);
        var nameBytes = Encoding.UTF8.GetBytes(safeName);
        var typeBytes = Encoding.UTF8.GetBytes(mimeType);
        if (nameBytes.Length > ushort.MaxValue)
            throw new ArgumentException("The file name is too long.", nameof(fileName));
        if (typeBytes.Length > ushort.MaxValue)
            throw new ArgumentException("The MIME type is too long.", nameof(mimeType));

        var original = bytes.ToArray();
        var sha256 = SHA256.HashData(original);
        var tryGzip = original.Length >= 768 && !IsPrecompressedType(mimeType);
        byte[]? compressed = null;
        if (tryGzip)
            compressed = await GzipAsync(original, cancellationToken).ConfigureAwait(false);

        var useGzip = compressed is not null && compressed.Length + 64 < original.Length;
        var transmitted = useGzip ? compressed! : original;
        var compression = useGzip ? CompressionMode.Gzip : CompressionMode.None;

        var output = new byte[checked(HeaderLength + nameBytes.Length + typeBytes.Length + transmitted.Length)];
        Magic.CopyTo(output, 0);
        output[4] = (byte)compression;
        WriteUInt16LE(output, 5, checked((ushort)nameBytes.Length));
        WriteUInt16LE(output, 7, checked((ushort)typeBytes.Length));
        WriteUInt32LE(output, 9, checked((uint)original.Length));
        WriteUInt32LE(output, 13, checked((uint)transmitted.Length));
        sha256.CopyTo(output, 17);
        nameBytes.CopyTo(output, HeaderLength);
        typeBytes.CopyTo(output, HeaderLength + nameBytes.Length);
        transmitted.CopyTo(output, HeaderLength + nameBytes.Length + typeBytes.Length);

        return new PackedFile(output, compression, original.Length, transmitted.Length);
    }

    public static async Task<UnpackedFile> UnpackAsync(
        ReadOnlyMemory<byte> container,
        CancellationToken cancellationToken = default)
    {
        var data = container.Span;
        if (data.Length < HeaderLength)
            throw new InvalidDataException("The DCF2 container is truncated.");
        if (!data[..Magic.Length].SequenceEqual(Magic))
            throw new InvalidDataException("Invalid DCF2 magic.");

        var compressionByte = data[4];
        if (compressionByte > (byte)CompressionMode.Gzip)
            throw new InvalidDataException("Unsupported DCF2 compression mode.");
        var compression = (CompressionMode)compressionByte;
        var nameLength = ReadUInt16LE(data, 5);
        var typeLength = ReadUInt16LE(data, 7);
        var fileLength = ReadUInt32LE(data, 9);
        var transmittedLength = ReadUInt32LE(data, 13);
        var dataOffset = checked(HeaderLength + nameLength + typeLength);

        if (fileLength == 0 || fileLength > MaxFileBytes ||
            transmittedLength == 0 || transmittedLength > MaxFileBytes ||
            dataOffset + transmittedLength != data.Length)
            throw new InvalidDataException("DCF2 container lengths do not match.");

        var transmitted = data.Slice(dataOffset, checked((int)transmittedLength)).ToArray();
        byte[] bytes;
        if (compression == CompressionMode.Gzip)
        {
            if (transmitted.Length < 18)
                throw new InvalidDataException("The gzip payload is incomplete.");
            var trailerSize = ReadUInt32LE(transmitted, transmitted.Length - 4);
            if (trailerSize != fileLength)
                throw new InvalidDataException("The gzip length does not match the DCF2 header.");
            bytes = await GunzipAsync(transmitted, checked((int)fileLength), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            bytes = transmitted;
        }

        if (bytes.Length != fileLength)
            throw new InvalidDataException("The decompressed length does not match the DCF2 header.");

        var name = SanitizeFileName(Encoding.UTF8.GetString(data.Slice(HeaderLength, nameLength)));
        var type = Encoding.UTF8.GetString(data.Slice(HeaderLength + nameLength, typeLength));
        if (string.IsNullOrEmpty(type))
            type = "application/octet-stream";

        return new UnpackedFile(
            name,
            type,
            bytes,
            data.Slice(17, 32).ToArray(),
            compression,
            checked((int)transmittedLength));
    }

    public static bool VerifySha256(UnpackedFile file) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(file.Bytes), file.Sha256);

    private static async Task<byte[]> GzipAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        await using var destination = new MemoryStream();
        await using (var gzip = new GZipStream(destination, CompressionLevel.Optimal, leaveOpen: true))
        {
            await gzip.WriteAsync(bytes.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        return destination.ToArray();
    }

    private static async Task<byte[]> GunzipAsync(byte[] bytes, int maxBytes, CancellationToken cancellationToken)
    {
        await using var source = new MemoryStream(bytes, writable: false);
        await using var gzip = new GZipStream(source, CompressionMode.Decompress, leaveOpen: false);
        await using var destination = new MemoryStream(Math.Min(maxBytes, bytes.Length * 2));
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await gzip.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > maxBytes)
                throw new InvalidDataException("The gzip payload expands beyond the DCF2 file limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return destination.ToArray();
    }

    private static bool IsPrecompressedType(string type)
    {
        var media = type.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (media.StartsWith("video/", StringComparison.Ordinal)) return true;
        if (media.StartsWith("image/", StringComparison.Ordinal))
            return !media.Equals("image/bmp", StringComparison.Ordinal) &&
                   !media.Equals("image/x-ms-bmp", StringComparison.Ordinal) &&
                   !media.Equals("image/svg+xml", StringComparison.Ordinal) &&
                   !media.Equals("image/tiff", StringComparison.Ordinal) &&
                   !media.Equals("image/x-icon", StringComparison.Ordinal) &&
                   !media.Equals("image/vnd.microsoft.icon", StringComparison.Ordinal);
        if (media.StartsWith("audio/", StringComparison.Ordinal))
            return !media.Equals("audio/wav", StringComparison.Ordinal) &&
                   !media.Equals("audio/x-wav", StringComparison.Ordinal) &&
                   !media.Equals("audio/wave", StringComparison.Ordinal) &&
                   !media.Equals("audio/vnd.wave", StringComparison.Ordinal) &&
                   !media.Equals("audio/aiff", StringComparison.Ordinal) &&
                   !media.Equals("audio/x-aiff", StringComparison.Ordinal) &&
                   !media.Equals("audio/basic", StringComparison.Ordinal) &&
                   !media.Equals("audio/l16", StringComparison.Ordinal);
        if (media.StartsWith("application/vnd.openxmlformats-officedocument.", StringComparison.Ordinal) ||
            media.StartsWith("application/vnd.oasis.opendocument.", StringComparison.Ordinal) ||
            media.EndsWith("+zip", StringComparison.Ordinal)) return true;

        return media is
            "application/gzip" or
            "application/java-archive" or
            "application/vnd.rar" or
            "application/x-7z-compressed" or
            "application/x-brotli" or
            "application/x-bzip" or
            "application/x-bzip2" or
            "application/x-gzip" or
            "application/x-lzma" or
            "application/x-rar-compressed" or
            "application/x-xz" or
            "application/x-zip-compressed" or
            "application/zip" or
            "application/zstd";
    }

    private static string SanitizeFileName(string name)
    {
        var baseName = name.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        var chars = baseName.Where(c => !char.IsControl(c) && c != '\u007f').ToArray();
        var cleaned = new string(chars).Trim();
        return cleaned is "" or "." or ".." ? "transfer.bin" : cleaned;
    }

    private static ushort ReadUInt16LE(ReadOnlySpan<byte> data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadUInt32LE(ReadOnlySpan<byte> data, int offset) =>
        (uint)(data[offset] |
               (data[offset + 1] << 8) |
               (data[offset + 2] << 16) |
               (data[offset + 3] << 24));

    private static void WriteUInt16LE(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32LE(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }
}
