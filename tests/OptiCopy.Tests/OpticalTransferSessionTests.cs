using OptiCopy.Core.Protocol;
using OptiCopy.Core.Transfer;
using Xunit;

namespace OptiCopy.Tests;

public sealed class OpticalTransferSessionTests
{
    [Fact]
    public async Task SessionEmitsDecimenV3BinaryFramesAndDcf2Container()
    {
        var payload = new byte[2048];
        new Random(1234).NextBytes(payload);

        var session = await OpticalTransferSession.CreateAsync(
            payload,
            "sample.bin",
            "application/octet-stream",
            1234);

        var expectedContainerLength = 49 + "sample.bin"u8.Length + "application/octet-stream"u8.Length + payload.Length;
        var expectedBlocks = (expectedContainerLength + OpticalTransferSession.DefaultBlockLength - 1) / OpticalTransferSession.DefaultBlockLength;

        Assert.Equal(expectedBlocks, session.Metadata.SourceBlocks);
        Assert.Equal((uint)(expectedBlocks * 2), session.CycleLength);
        Assert.Equal(expectedContainerLength, session.Container.Length);
        Assert.Equal([0x44, 0x43, 0x46, 0x32], session.Container[..4]);
        Assert.Equal(1234, session.Metadata.SessionId);
        Assert.Equal(OpticalTransferSession.DefaultBlockLength, session.Metadata.BlockLength);
        Assert.Equal(OpticalTransferSession.DefaultFrameBytes, session.Metadata.BlockLength + FrameCodec.HeaderLength);

        var first = session.NextFrame();

        Assert.Equal(0u, first.Sequence);
        Assert.Equal(FrameCodec.WireVersion, first.Frame.Version);
        Assert.Equal(session.Metadata.SessionId, first.Frame.SessionId);
        Assert.Equal((ushort)OpticalTransferSession.DefaultBlockLength, first.Frame.BlockLength);
        Assert.Equal((uint)session.Container.Length, first.Frame.TotalLength);
        Assert.Equal(Fnv1a.Hash(session.Container), first.Frame.PayloadFnv);
        Assert.Equal(session.Container[..OpticalTransferSession.DefaultBlockLength], first.Frame.Payload);

        var binary = Convert.FromBase64String(first.PayloadBase64);
        Assert.True(FrameCodec.TryDecode(binary, out var decoded));
        Assert.Equal(first.Frame.Version, decoded.Version);
        Assert.Equal(first.Frame.Flags, decoded.Flags);
        Assert.Equal(first.Frame.SessionId, decoded.SessionId);
        Assert.Equal(first.Frame.Sequence, decoded.Sequence);
        Assert.Equal(first.Frame.SourceBlocks, decoded.SourceBlocks);
        Assert.Equal(first.Frame.BlockLength, decoded.BlockLength);
        Assert.Equal(first.Frame.TotalLength, decoded.TotalLength);
        Assert.Equal(first.Frame.PayloadFnv, decoded.PayloadFnv);
        Assert.Equal(first.Frame.Payload, decoded.Payload);
        Assert.Equal(first.PayloadBase64, first.ProtocolPacket);
        Assert.Equal(OpticalTransferSession.DefaultFrameBytes, binary.Length);
    }

    [Fact]
    public async Task RestartCreatesFreshSessionWithoutRepacking()
    {
        var payload = new byte[4096];
        new Random(5678).NextBytes(payload);

        var session = await OpticalTransferSession.CreateAsync(
            payload,
            "sample.bin",
            "application/octet-stream",
            100);
        _ = session.NextFrame();

        var restarted = session.Restart(200);
        var first = restarted.NextFrame();

        Assert.Equal(200, restarted.Metadata.SessionId);
        Assert.Equal(100, session.Metadata.SessionId);
        Assert.Equal(session.Container, restarted.Container);
        Assert.Equal(0u, first.Sequence);
        Assert.Equal((ushort)OpticalTransferSession.DefaultBlockLength, first.Frame.BlockLength);
        Assert.Equal(restarted.Metadata.SessionId, first.Frame.SessionId);
    }
}
