using OptiCopy.Core.Protocol;
using OptiCopy.Core.Transfer;
using Xunit;

namespace OptiCopy.Tests;

public sealed class OpticalTransferSessionTests
{
    [Fact]
    public void SessionEmitsMobileCompatibleDot3Packets()
    {
        var payload = Enumerable.Range(0, 2048).Select(static i => (byte)(i % 251)).ToArray();
        var session = OpticalTransferSession.Create(payload, "sample.bin", "application/octet-stream", 1234, 256);

        Assert.Equal(8, session.Metadata.SourceBlocks);
        Assert.Equal(8u, session.MinimumFrames);

        var first = session.NextFrame();

        Assert.Equal(0u, first.Sequence);
        Assert.StartsWith("DOT3|", first.ProtocolPacket, StringComparison.Ordinal);

        var parts = first.ProtocolPacket.Split('|', 11);
        Assert.Equal(11, parts.Length);
        Assert.Equal("DOT3", parts[0]);
        Assert.Equal("04d2", parts[1]);
        Assert.Equal("0", parts[2]);
        Assert.Equal("8", parts[3]);
        Assert.Equal("8", parts[4]);
        Assert.Equal(payload.Length.ToString(), parts[5]);
        Assert.Equal(session.Metadata.Sha256, parts[6]);
        Assert.Equal("0", parts[7]);
        Assert.Equal("application/octet-stream", parts[8]);
        Assert.Equal("sample.bin", parts[9]);
        Assert.NotEmpty(parts[10]);

        var chunk = Convert.FromBase64String(parts[10]);
        Assert.Equal(256, chunk.Length);
        Assert.Equal(payload.Take(256).ToArray(), chunk);

        var binary = Convert.FromBase64String(first.PayloadBase64);
        Assert.True(FrameCodec.TryDecode(binary, out var decoded));
        Assert.Equal(session.Metadata.SessionId, decoded.SessionId);
        Assert.Equal(first.Sequence, decoded.Sequence);
        Assert.Equal(Fnv1a.Hash(decoded.Payload), decoded.PayloadFnv);
        Assert.Equal((ushort)256, decoded.BlockLength);
        Assert.Equal((uint)payload.Length, decoded.TotalLength);
    }
}
