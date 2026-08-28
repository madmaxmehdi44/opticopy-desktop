using System.Security.Cryptography;
using OptiCopy.Core.Transfer;
using Xunit;

namespace OptiCopy.Tests;

public sealed class LegacyDot2TransferSessionTests
{
    [Fact]
    public void CreatesAndroidCompatibleDot2SystematicFrames()
    {
        var payload = new byte[1000];
        RandomNumberGenerator.Fill(payload);

        var session = LegacyDot2TransferSession.Create(
            payload,
            "sample.bin",
            "application/octet-stream",
            0x1234);

        Assert.Equal(3, session.Metadata.DataChunks);
        Assert.Equal(4, session.Metadata.TotalChunks);
        Assert.Equal(1, session.Metadata.ParityChunks);
        Assert.Equal(360, session.Metadata.ChunkSize);
        Assert.Equal("sample.bin", session.Metadata.FileName);
        Assert.Equal("application/octet-stream", session.Metadata.MimeType);
        Assert.Equal(payload.LongLength, session.Metadata.OriginalSize);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), session.Metadata.Sha256);

        var first = session.NextFrame();
        Assert.Equal(0, first.Sequence);
        Assert.False(first.IsParity);
        Assert.StartsWith("DOT2|1234|0|3|4|1000|", first.Packet, StringComparison.Ordinal);
        Assert.EndsWith("|" + first.PayloadBase64, first.Packet, StringComparison.Ordinal);
        Assert.Equal(360, Convert.FromBase64String(first.PayloadBase64).Length);

        var second = session.NextFrame();
        var third = session.NextFrame();
        var parity = session.NextFrame();

        Assert.Equal(1, second.Sequence);
        Assert.Equal(2, third.Sequence);
        Assert.Equal(3, parity.Sequence);
        Assert.True(parity.IsParity);
        Assert.Equal(4, session.CycleLength);
    }

    [Fact]
    public void StreamRepeatsFromBeginningAfterCompleteCycle()
    {
        var payload = new byte[500];
        RandomNumberGenerator.Fill(payload);
        var session = LegacyDot2TransferSession.Create(payload, "x.bin", "application/octet-stream", 7);

        _ = session.NextFrame();
        _ = session.NextFrame();
        _ = session.NextFrame();
        _ = session.NextFrame();
        var repeated = session.NextFrame();

        Assert.Equal(0, repeated.Sequence);
        Assert.False(repeated.IsParity);
    }

    [Fact]
    public void RestartUsesFreshTransferIdAndPreservesPayload()
    {
        var payload = new byte[721];
        RandomNumberGenerator.Fill(payload);
        var session = LegacyDot2TransferSession.Create(payload, "data.bin", "application/octet-stream", 10);

        var restarted = session.Restart(11);
        var first = restarted.NextFrame();

        Assert.Equal((ushort)11, restarted.Metadata.TransferId);
        Assert.Equal((ushort)10, session.Metadata.TransferId);
        Assert.StartsWith("DOT2|000B|0|", first.Packet, StringComparison.Ordinal);
        Assert.Equal(payload.Length, restarted.Metadata.OriginalSize);
    }
}
