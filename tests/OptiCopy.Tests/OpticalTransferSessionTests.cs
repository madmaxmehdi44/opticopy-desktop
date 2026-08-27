using OptiCopy.Core.Protocol;
using OptiCopy.Core.Transfer;
using Xunit;

namespace OptiCopy.Tests;

public sealed class OpticalTransferSessionTests
{
    [Fact]
    public void SessionEmitsDecimenV3BinaryFramesAndDcf2Container()
    {
        var payload = Enumerable.Range(0, 2048).Select(static i => (byte)(i % 251)).ToArray();
        var session = OpticalTransferSession.Create(payload, "sample.bin", "application/octet-stream", 1234, 256);

        Assert.Equal(9, session.Metadata.SourceBlocks);
        Assert.Equal(18u, session.MinimumFrames);
        Assert.Equal(49 + "sample.bin"u8.Length + "application/octet-stream"u8.Length + payload.Length, session.Container.Length);
        Assert.Equal([0x44, 0x43, 0x46, 0x32], session.Container[..4]);

        var first = session.NextFrame();

        Assert.Equal(0u, first.Sequence);
        Assert.Equal(FrameCodec.WireVersion, first.Frame.Version);
        Assert.Equal(session.Metadata.SessionId, first.Frame.SessionId);
        Assert.Equal((ushort)256, first.Frame.BlockLength);
        Assert.Equal((uint)session.Container.Length, first.Frame.TotalLength);
        Assert.Equal(Fnv1a.Hash(session.Container), first.Frame.PayloadFnv);
        Assert.Equal(session.Container[..256], first.Frame.Payload);

        var binary = Convert.FromBase64String(first.PayloadBase64);
        Assert.True(FrameCodec.TryDecode(binary, out var decoded));
        Assert.Equal(first.Frame, decoded);
        Assert.Equal(string.Empty, first.ProtocolPacket);
    }
}
