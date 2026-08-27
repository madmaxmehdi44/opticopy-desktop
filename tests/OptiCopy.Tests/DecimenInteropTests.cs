using System.Text;
using OptiCopy.Core.Fountain;
using OptiCopy.Core.Protocol;
using Xunit;

namespace OptiCopy.Tests;

public sealed class DecimenInteropTests
{
    [Fact]
    public void FrameUsesLittleEndianWireLayout()
    {
        var frame = new Frame(FrameCodec.WireVersion, 0, 0x1234, 0x01020304, 2, 4, 0x05060708, 0x090A0B0C, [1, 2, 3, 4]);
        var bytes = FrameCodec.Encode(frame);

        Assert.Equal(new byte[]
        {
            0xD1, 0xC3, 0x03, 0x00,
            0x34, 0x12,
            0x04, 0x03, 0x02, 0x01,
            0x02, 0x00,
            0x04, 0x00,
            0x08, 0x07, 0x06, 0x05,
            0x0C, 0x0B, 0x0A, 0x09,
            1, 2, 3, 4
        }, bytes);
    }

    [Fact]
    public void FrameClassifierRecognizesVersionAndFlagFailures()
    {
        var frame = new Frame(FrameCodec.WireVersion, 0, 1, 0, 1, 4, 4, 0, [1, 2, 3, 4]);
        var bytes = FrameCodec.Encode(frame);

        Assert.Equal(FrameVerdictKind.Ok, FrameCodec.Classify(bytes).Kind);

        bytes[2] = 2;
        Assert.Equal(FrameVerdictKind.OlderSender, FrameCodec.Classify(bytes).Kind);

        bytes[2] = 3;
        bytes[3] = 1;
        Assert.Equal(FrameVerdictKind.UnsupportedFlags, FrameCodec.Classify(bytes).Kind);
    }

    [Fact]
    public async Task Dcf2RoundTripsWithoutCompression()
    {
        var source = Encoding.UTF8.GetBytes("Decimen DCF2 interoperability test.");
        var packed = await OpticalFileContainer.PackAsync("folder\\example.txt", "text/plain", source);
        var unpacked = await OpticalFileContainer.UnpackAsync(packed.Container);

        Assert.Equal("example.txt", unpacked.Name);
        Assert.Equal("text/plain", unpacked.Type);
        Assert.Equal(source, unpacked.Bytes);
        Assert.True(OpticalFileContainer.VerifySha256(unpacked));
        Assert.Equal(packed.TransmittedSize, unpacked.TransmittedSize);
    }

    [Fact]
    public async Task Dcf2RoundTripsWithGzip()
    {
        var source = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Decimen optical transfer interoperability. ", 200)));
        var packed = await OpticalFileContainer.PackAsync("sample.txt", "text/plain", source);

        Assert.Equal(OpticalFileContainer.CompressionMode.Gzip, packed.Compression);
        var unpacked = await OpticalFileContainer.UnpackAsync(packed.Container);
        Assert.Equal(source, unpacked.Bytes);
        Assert.True(OpticalFileContainer.VerifySha256(unpacked));
    }

    [Fact]
    public void CarouselSystematicFramesMatchBlockPositions()
    {
        var encoder = new CarouselFountainEncoder(Enumerable.Range(0, 32).Select(static i => (byte)i).ToArray(), 8, 7);
        Assert.Equal(new[] { 0 }, FrameComposition.Compose(encoder.SourceBlocks, encoder.SessionId, 0));
        Assert.Equal(new[] { 1 }, FrameComposition.Compose(encoder.SourceBlocks, encoder.SessionId, 1));
        Assert.Equal(new[] { 2 }, FrameComposition.Compose(encoder.SourceBlocks, encoder.SessionId, 2));
        Assert.Equal(new[] { 3 }, FrameComposition.Compose(encoder.SourceBlocks, encoder.SessionId, 3));
    }
}
