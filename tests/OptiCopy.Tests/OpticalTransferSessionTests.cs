using OptiCopy.Core.Protocol;
using OptiCopy.Core.Transfer;
using Xunit;

namespace OptiCopy.Tests;

public sealed class OpticalTransferSessionTests
{
    [Fact]
    public void SessionEmitsDecodableFramesAndCompletesCycle()
    {
        var payload = Enumerable.Range(0, 2048).Select(static i => (byte)(i % 251)).ToArray();
        var session = OpticalTransferSession.Create(payload, "sample.bin", "application/octet-stream", 1234, 256);

        Assert.Equal(8, session.Metadata.SourceBlocks);
        Assert.Equal(16u, session.MinimumFrames);

        var first = session.NextFrame();
        Assert.Equal(0u, first.Sequence);
        Assert.True(Convert.TryFromBase64String(first.PayloadBase64, new byte[first.PayloadBase64.Length], out _));

        var binary = Convert.FromBase64String(first.PayloadBase64);
        Assert.True(FrameCodec.TryDecode(binary, out var decoded));
        Assert.Equal(session.Metadata.SessionId, decoded.SessionId);
        Assert.Equal(first.Sequence, decoded.Sequence);
        Assert.Equal(Fnv1a.Hash(decoded.Payload), decoded.PayloadFnv);
        Assert.Equal((ushort)256, decoded.BlockLength);
        Assert.Equal((uint)payload.Length, decoded.TotalLength);
    }
}
