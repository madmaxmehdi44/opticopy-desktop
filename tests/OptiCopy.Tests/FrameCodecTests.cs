using OptiCopy.Core.Protocol;
using Xunit;

namespace OptiCopy.Tests;

public sealed class FrameCodecTests
{
    [Fact]
    public void EncodeDecodeRoundTripsFrame()
    {
        var payload = Enumerable.Range(0, 32).Select(static i => (byte)i).ToArray();
        var frame = new Frame(
            FrameCodec.WireVersion,
            0,
            42,
            123,
            4,
            (ushort)payload.Length,
            128,
            Fnv1a.Hash(payload),
            payload);

        var encoded = FrameCodec.Encode(frame);

        Assert.True(FrameCodec.TryDecode(encoded, out var decoded));
        Assert.Equal(frame.Version, decoded.Version);
        Assert.Equal(frame.SessionId, decoded.SessionId);
        Assert.Equal(frame.Sequence, decoded.Sequence);
        Assert.Equal(frame.SourceBlocks, decoded.SourceBlocks);
        Assert.Equal(frame.BlockLength, decoded.BlockLength);
        Assert.Equal(frame.TotalLength, decoded.TotalLength);
        Assert.Equal(frame.PayloadFnv, decoded.PayloadFnv);
        Assert.Equal(frame.Payload, decoded.Payload);
    }
}
