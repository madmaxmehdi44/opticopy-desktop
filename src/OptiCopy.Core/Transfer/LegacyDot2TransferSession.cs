using System.Security.Cryptography;
using System.Text;

namespace OptiCopy.Core.Transfer;

public sealed record LegacyDot2TransferMetadata(
    string FileName,
    string MimeType,
    long OriginalSize,
    string Sha256,
    ushort TransferId,
    int DataChunks,
    int TotalChunks,
    int ChunkSize,
    int ParityChunks);

public sealed record LegacyDot2TransferFrame(
    int Sequence,
    bool IsParity,
    string Packet,
    string PayloadBase64);

/// <summary>
/// Android-compatible DOT2 sender.
/// Wire format:
/// DOT2|id|seq|dataChunks|totalChunks|fileSize|sha256|mime|name|base64
/// Payload uses 360-byte chunks and the same GF(256) Cauchy Reed-Solomon
/// generator as the Android receiver/encoder implementation.
/// </summary>
public sealed class LegacyDot2TransferSession
{
    public const int DefaultChunkSize = 360;
    public const double DefaultParityRatio = 0.33;

    private const int FieldSize = 256;
    private const int PrimitivePolynomial = 0x11D;

    private static readonly int[] ExpTable = BuildExpTable();
    private static readonly int[] LogTable = BuildLogTable();

    private readonly List<Chunk> _chunks;
    private int _nextSequence;

    private LegacyDot2TransferSession(
        List<Chunk> chunks,
        LegacyDot2TransferMetadata metadata)
    {
        _chunks = chunks;
        Metadata = metadata;
    }

    private sealed record Chunk(int Sequence, bool IsParity, byte[] Bytes, string Packet);

    public LegacyDot2TransferMetadata Metadata { get; }
    public int CycleLength => _chunks.Count;
    public uint FramesEmitted { get; private set; }

    public static LegacyDot2TransferSession Create(
        ReadOnlySpan<byte> data,
        string fileName,
        string mimeType,
        ushort transferId,
        int chunkSize = DefaultChunkSize,
        double parityRatio = DefaultParityRatio)
    {
        if (data.IsEmpty)
            throw new ArgumentException("Payload cannot be empty.", nameof(data));
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(chunkSize, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegative(parityRatio);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parityRatio, 1.0);

        var safeName = Sanitize(fileName);
        var safeMime = Sanitize(mimeType);
        var source = data.ToArray();
        var dataChunks = checked((source.Length + chunkSize - 1) / chunkSize);

        var parityCount = dataChunks >= 250
            ? 0
            : Math.Min(255 - dataChunks, Math.Max(0, (int)Math.Ceiling(dataChunks * parityRatio)));

        var totalChunks = checked(dataChunks + parityCount);
        if (totalChunks > 255)
            throw new NotSupportedException("DOT2 Reed-Solomon supports at most 255 total chunks.");

        var sha256 = Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
        var chunks = new List<Chunk>(totalChunks);
        var normalized = new byte[dataChunks][];

        for (var i = 0; i < dataChunks; i++)
        {
            var start = checked(i * chunkSize);
            var length = Math.Min(chunkSize, source.Length - start);
            var bytes = new byte[chunkSize];
            source.AsSpan(start, length).CopyTo(bytes);
            normalized[i] = bytes;
        }

        for (var i = 0; i < dataChunks; i++)
        {
            var packetPayload = Convert.ToBase64String(normalized[i]);
            chunks.Add(new Chunk(
                i,
                false,
                normalized[i],
                BuildPacket(transferId, i, dataChunks, totalChunks, source.LongLength, sha256, safeMime, safeName, packetPayload)));
        }

        for (var parity = 0; parity < parityCount; parity++)
        {
            var parityBytes = BuildParityChunk(normalized, parity);
            var sequence = dataChunks + parity;
            var packetPayload = Convert.ToBase64String(parityBytes);
            chunks.Add(new Chunk(
                sequence,
                true,
                parityBytes,
                BuildPacket(transferId, sequence, dataChunks, totalChunks, source.LongLength, sha256, safeMime, safeName, packetPayload)));
        }

        var metadata = new LegacyDot2TransferMetadata(
            safeName,
            safeMime,
            source.LongLength,
            sha256,
            transferId,
            dataChunks,
            totalChunks,
            chunkSize,
            parityCount);

        return new LegacyDot2TransferSession(chunks, metadata);
    }

    public LegacyDot2TransferSession Restart(ushort transferId)
    {
        var allBytes = AssembleOriginalBytes();
        return Create(allBytes, Metadata.FileName, Metadata.MimeType, transferId, Metadata.ChunkSize, Metadata.ParityChunks == 0 || Metadata.DataChunks == 0
            ? 0.0
            : Metadata.ParityChunks / (double)Metadata.DataChunks);
    }

    public void Reset()
    {
        _nextSequence = 0;
        FramesEmitted = 0;
    }

    public LegacyDot2TransferFrame NextFrame()
    {
        var sequence = _nextSequence % _chunks.Count;
        _nextSequence = checked(_nextSequence + 1);
        FramesEmitted++;

        var chunk = _chunks[sequence];
        return new LegacyDot2TransferFrame(
            sequence,
            chunk.IsParity,
            chunk.Packet,
            Convert.ToBase64String(chunk.Bytes));
    }

    private byte[] AssembleOriginalBytes()
    {
        var length = checked((int)Metadata.OriginalSize);
        var output = new byte[length];
        var offset = 0;
        for (var i = 0; i < Metadata.DataChunks && offset < output.Length; i++)
        {
            var available = Math.Min(Metadata.ChunkSize, output.Length - offset);
            Buffer.BlockCopy(_chunks[i].Bytes, 0, output, offset, available);
            offset += available;
        }

        return output;
    }

    private static string BuildPacket(
        ushort transferId,
        int sequence,
        int dataChunks,
        int totalChunks,
        long fileSize,
        string sha256,
        string mimeType,
        string fileName,
        string payloadBase64)
    {
        return $"DOT2|{transferId:X4}|{sequence}|{dataChunks}|{totalChunks}|{fileSize}|{sha256}|{mimeType}|{fileName}|{payloadBase64}";
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";
        return value.Replace("|", "_").Replace("\r", "").Replace("\n", "");
    }

    private static byte[] BuildParityChunk(byte[][] dataChunks, int parityIndex)
    {
        var k = dataChunks.Length;
        var output = new byte[DefaultChunkSize];
        var rowIndex = k + parityIndex;

        for (var byteIndex = 0; byteIndex < output.Length; byteIndex++)
        {
            var accumulator = 0;
            for (var j = 0; j < k; j++)
            {
                var coefficient = CauchyCoefficient(rowIndex, j);
                var dataByte = dataChunks[j][byteIndex];
                accumulator ^= GfMul(coefficient, dataByte);
            }

            output[byteIndex] = (byte)accumulator;
        }

        return output;
    }

    private static int CauchyCoefficient(int row, int column)
    {
        var denominator = row ^ column;
        return denominator == 0 ? 1 : GfInv(denominator);
    }

    private static int GfMul(int a, int b)
    {
        if (a == 0 || b == 0)
            return 0;
        return ExpTable[LogTable[a] + LogTable[b]];
    }

    private static int GfInv(int value)
    {
        if (value == 0)
            throw new ArithmeticException("Zero has no inverse in GF(256).");
        return ExpTable[(FieldSize - 1) - LogTable[value]];
    }

    private static int[] BuildExpTable()
    {
        var table = new int[FieldSize * 2];
        var x = 1;
        for (var i = 0; i < FieldSize - 1; i++)
        {
            table[i] = x;
            table[i + FieldSize - 1] = x;
            x <<= 1;
            if (x >= FieldSize)
                x ^= PrimitivePolynomial;
        }

        return table;
    }

    private static int[] BuildLogTable()
    {
        var table = new int[FieldSize];
        var x = 1;
        for (var i = 0; i < FieldSize - 1; i++)
        {
            table[x] = i;
            x <<= 1;
            if (x >= FieldSize)
                x ^= PrimitivePolynomial;
        }

        return table;
    }
}
