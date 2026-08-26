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

public static class FrameCodec
{
    public const int HeaderLength = 22;
    public const byte Magic0 = 0xD1;
    public const byte Magic1 = 0xC3;
    public const byte WireVersion = 3;

    public static byte[] Encode(Frame frame)
    {
        if (frame.Payload.Length != frame.BlockLength)
            throw new ArgumentException("Payload length must equal BlockLength.", nameof(frame));
        var output = new byte[HeaderLength + frame.Payload.Length];
        output[0] = Magic0;
        output[1] = Magic1;
        output[2] = frame.Version;
        output[3] = frame.Flags;
        BitConverter.TryWriteBytes(output.AsSpan(4, 2), frame.SessionId);
        BitConverter.TryWriteBytes(output.AsSpan(6, 4), frame.Sequence);
        BitConverter.TryWriteBytes(output.AsSpan(10, 2), frame.SourceBlocks);
        BitConverter.TryWriteBytes(output.AsSpan(12, 2), frame.BlockLength);
        BitConverter.TryWriteBytes(output.AsSpan(14, 4), frame.TotalLength);
        BitConverter.TryWriteBytes(output.AsSpan(18, 4), frame.PayloadFnv);
        Buffer.BlockCopy(frame.Payload, 0, output, HeaderLength, frame.Payload.Length);
        return output;
    }

    public static bool TryDecode(ReadOnlySpan<byte> data, out Frame frame)
    {
        frame = default;
        if (data.Length < HeaderLength || data[0] != Magic0 || data[1] != Magic1 || data[2] != WireVersion)
            return false;

        var session = BitConverter.ToUInt16(data.Slice(4, 2));
        var sequence = BitConverter.ToUInt32(data.Slice(6, 4));
        var k = BitConverter.ToUInt16(data.Slice(10, 2));
        var blockLength = BitConverter.ToUInt16(data.Slice(12, 2));
        var totalLength = BitConverter.ToUInt32(data.Slice(14, 4));
        var fnv = BitConverter.ToUInt32(data.Slice(18, 4));
        if (k == 0 || blockLength == 0 || data.Length != HeaderLength + blockLength)
            return false;

        frame = new Frame(data[2], data[3], session, sequence, k, blockLength, totalLength, fnv,
            data.Slice(HeaderLength).ToArray());
        return true;
    }
}

public static class Fnv1a
{
    public static uint Hash(ReadOnlySpan<byte> data)
    {
        uint hash = 2166136261;
        foreach (var b in data)
        {
            hash ^= b;
            hash = unchecked(hash * 16777619);
        }
        return hash;
    }
}
