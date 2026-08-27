using System.Buffers.Binary;

namespace OptiCopy.Core.Protocol;

public readonly record struct Frame(
    byte Version,
    byte Flags,
    ushort SessionId,
    uint Sequence,
    ushort SourceBlocks,
    ushort BlockLength,
    uint TotalLength,
    uint PayloadFnv,
    byte[] Payload);

public readonly record struct FrameHeader(
    ushort SessionId,
    uint Sequence,
    ushort SourceBlocks,
    ushort BlockLength,
    uint TotalLength,
    uint PayloadFnv,
    byte Flags);

public enum FrameVerdictKind
{
    Ok,
    Foreign,
    OlderSender,
    NewerSender,
    UnsupportedFlags,
    Malformed
}

public readonly record struct FrameVerdict(FrameVerdictKind Kind, byte Version = 0, byte Flags = 0);

public static class FrameCodec
{
    public const int HeaderLength = 22;
    public const byte Magic0 = 0xD1;
    public const byte Magic1 = 0xC3;
    public const byte WireVersion = 3;
    public const byte CriticalFlags = 0x0F;
    public const byte SupportedFlags = 0x00;
    public const int MaxFileBytes = 64 * 1024 * 1024;

    public static byte[] Encode(Frame frame)
    {
        if (frame.Version != WireVersion)
            throw new ArgumentOutOfRangeException(nameof(frame), "Only Decimen wire version 3 is supported.");
        if ((frame.Flags & CriticalFlags & ~SupportedFlags) != 0)
            throw new ArgumentException("Frame contains unsupported critical flags.", nameof(frame));
        if (frame.SourceBlocks == 0 || frame.BlockLength == 0)
            throw new ArgumentException("SourceBlocks and BlockLength must be non-zero.", nameof(frame));
        if (frame.Payload.Length != frame.BlockLength)
            throw new ArgumentException("Payload length must equal BlockLength.", nameof(frame));

        var output = new byte[HeaderLength + frame.Payload.Length];
        output[0] = Magic0;
        output[1] = Magic1;
        output[2] = frame.Version;
        output[3] = frame.Flags;
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(4, 2), frame.SessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(6, 4), frame.Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(10, 2), frame.SourceBlocks);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(12, 2), frame.BlockLength);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(14, 4), frame.TotalLength);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(18, 4), frame.PayloadFnv);
        frame.Payload.CopyTo(output.AsSpan(HeaderLength));
        return output;
    }

    public static FrameVerdict Classify(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != Magic0)
            return new(FrameVerdictKind.Foreign);

        if (data[1] != Magic1)
        {
            return data[1] switch
            {
                0x0C => new(FrameVerdictKind.OlderSender, 1),
                0x0D => new(FrameVerdictKind.OlderSender, 2),
                _ => new(FrameVerdictKind.Foreign)
            };
        }

        var version = data[2];
        if (version == 0)
            return new(FrameVerdictKind.Malformed, version);
        if (version != WireVersion)
            return version > WireVersion
                ? new(FrameVerdictKind.NewerSender, version)
                : new(FrameVerdictKind.OlderSender, version);

        var unknownCritical = (byte)(data[3] & CriticalFlags & ~SupportedFlags);
        if (unknownCritical != 0)
            return new(FrameVerdictKind.UnsupportedFlags, version, unknownCritical);

        if (data.Length <= HeaderLength)
            return new(FrameVerdictKind.Malformed, version);

        var sourceBlocks = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10, 2));
        var blockLength = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12, 2));
        var totalLength = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(14, 4));
        if (sourceBlocks == 0 || blockLength == 0 || totalLength == 0)
            return new(FrameVerdictKind.Malformed, version);
        if (data.Length != HeaderLength + blockLength)
            return new(FrameVerdictKind.Malformed, version);

        return new(FrameVerdictKind.Ok, version);
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out Frame frame)
    {
        frame = default;
        if (Classify(data).Kind != FrameVerdictKind.Ok)
            return false;

        var header = new FrameHeader(
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(6, 4)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12, 2)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(14, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(18, 4)),
            data[3]);

        frame = new Frame(
            data[2],
            header.Flags,
            header.SessionId,
            header.Sequence,
            header.SourceBlocks,
            header.BlockLength,
            header.TotalLength,
            header.PayloadFnv,
            data.Slice(HeaderLength).ToArray());
        return true;
    }

    public static string StreamIdentity(FrameHeader header)
    {
        var critical = (byte)(header.Flags & CriticalFlags);
        return $"{header.SessionId}:{header.SourceBlocks}:{header.BlockLength}:{header.TotalLength}:{header.PayloadFnv}:{critical}";
    }
}

public static class Fnv1a
{
    public static uint Hash(ReadOnlySpan<byte> data)
    {
        uint hash = 0x811C9DC5;
        foreach (var b in data)
        {
            hash ^= b;
            hash = unchecked(hash * 0x01000193);
        }
        return hash;
    }
}
