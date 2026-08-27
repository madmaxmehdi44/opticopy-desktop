using System.Linq;
using System.Text;
using OptiCopy.Core.Fountain;
using OptiCopy.Core.Protocol;
using Xunit;

namespace OptiCopy.Tests;

public sealed class GoldenVectorInteropTests
{
    [Fact]
    public void CanonicalFrameMatchesDecimenGoldenVector()
    {
        var frame = new Frame(
            FrameCodec.WireVersion,
            0,
            0xBEEF,
            0x01020304,
            0x0111,
            6,
            0x00FEDCBA,
            0x89ABCDEF,
            [1, 2, 3, 4, 5, 6]);

        var actual = Convert.ToHexString(FrameCodec.Encode(frame)).ToLowerInvariant();
        const string expected =
            "d1c30300efbe0403020111010600badcfe00efcdab89010203040506";

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ClassificationVectorsMatchDecimenSemantics()
    {
        Assert.Equal(FrameVerdictKind.Ok,
            FrameCodec.Classify([0xD1, 0xC3, 0x03, 0x00]).Kind);

        Assert.Equal(FrameVerdictKind.OlderSender,
            FrameCodec.Classify([0xD1, 0x0C, 0x03, 0x00]).Kind);
        Assert.Equal(1, FrameCodec.Classify([0xD1, 0x0C, 0x03, 0x00]).Version);

        Assert.Equal(FrameVerdictKind.OlderSender,
            FrameCodec.Classify([0xD1, 0x0D, 0x03, 0x00]).Kind);
        Assert.Equal(2, FrameCodec.Classify([0xD1, 0x0D, 0x03, 0x00]).Version);

        Assert.Equal(FrameVerdictKind.Foreign,
            FrameCodec.Classify([0xD1, 0x42, 0x03, 0x00]).Kind);
        Assert.Equal(FrameVerdictKind.Foreign,
            FrameCodec.Classify([0xD2, 0xC3, 0x03, 0x00]).Kind);

        Assert.Equal(FrameVerdictKind.NewerSender,
            FrameCodec.Classify([0xD1, 0xC3, 0x04, 0x00]).Kind);
        Assert.Equal(4, FrameCodec.Classify([0xD1, 0xC3, 0x04, 0x00]).Version);

        Assert.Equal(FrameVerdictKind.OlderSender,
            FrameCodec.Classify([0xD1, 0xC3, 0x02, 0x00]).Kind);
        Assert.Equal(2, FrameCodec.Classify([0xD1, 0xC3, 0x02, 0x00]).Version);

        Assert.Equal(FrameVerdictKind.Malformed,
            FrameCodec.Classify([0xD1, 0xC3, 0x00, 0x00]).Kind);
    }

    [Fact]
    public void CriticalAndIgnorableFlagsMatchDecimenSemantics()
    {
        Assert.Equal(FrameVerdictKind.UnsupportedFlags,
            FrameCodec.Classify([0xD1, 0xC3, 0x03, 0x01]).Kind);

        var frame = new Frame(FrameCodec.WireVersion, 0x10, 1, 0, 1, 4, 4, 0, [1, 2, 3, 4]);
        var encoded = FrameCodec.Encode(frame);
        Assert.Equal(FrameVerdictKind.Ok, FrameCodec.Classify(encoded).Kind);
        Assert.True(FrameCodec.TryDecode(encoded, out var decoded));
        Assert.Equal((byte)0x10, decoded.Flags);
    }

    [Fact]
    public void StreamIdentityIgnoresIgnorableFlags()
    {
        var a = new FrameHeader(0xBEEF, 1, 0x0111, 6, 0x00FEDCBA, 0x89ABCDEF, 0x00);
        var b = a with { Flags = 0x10 };
        var c = a with { Flags = 0x01 };

        Assert.Equal(FrameCodec.StreamIdentity(a), FrameCodec.StreamIdentity(b));
        Assert.NotEqual(FrameCodec.StreamIdentity(a), FrameCodec.StreamIdentity(c));
    }

    [Fact]
    public async Task CanonicalDcf2ContainerMatchesWireBytes()
    {
        var source = new byte[] { 0, 1, 2, 127, 128, 254, 255 };
        var packed = await OpticalFileContainer.PackAsync(
            "résumé.bin",
            "application/octet-stream",
            source);

        Assert.Equal(OpticalFileContainer.CompressionMode.None, packed.Compression);
        Assert.Equal(
            "44434632000c00180007000000070000007bb6463b30f9e301fed333cdf8960ca9497b602ccd8eeb46ae42693fdea15a4d72c3a973756dc3a92e62696e6170706c69636174696f6e2f6f637465742d73747265616d0001027f80feff",
            Convert.ToHexString(packed.Container).ToLowerInvariant());

        var unpacked = await OpticalFileContainer.UnpackAsync(packed.Container);
        Assert.Equal("résumé.bin", unpacked.Name);
        Assert.Equal("application/octet-stream", unpacked.Type);
        Assert.Equal(source, unpacked.Bytes);
        Assert.True(OpticalFileContainer.VerifySha256(unpacked));
    }

    [Fact]
    public void CarouselRepairVectorsMatchDecimen()
    {
        var expected23 = new[] { 1, 5, 7, 8, 12, 15 };
        var expected24 = new[] { 5, 10, 16, 17, 18, 21 };
        var expected25 = new[] { 3, 8, 11, 15, 17, 20 };
        var expected26 = new[] { 10, 11, 13, 22 };

        Assert.Equal(expected23, FrameComposition.Compose(23, 7, 23).OrderBy(x => x));
        Assert.Equal(expected24, FrameComposition.Compose(23, 7, 24).OrderBy(x => x));
        Assert.Equal(expected25, FrameComposition.Compose(23, 7, 25).OrderBy(x => x));
        Assert.Equal(expected26, FrameComposition.Compose(23, 7, 26).OrderBy(x => x));
    }

    [Fact]
    public void SplitMix32MatchesReferenceVector()
    {
        var random = SplitMix32.Create(7);
        Assert.Equal(0xE62E1D4Cu, random());
        Assert.Equal(0xA9F7A3B7u, random());
        Assert.Equal(0x74FAEA18u, random());
        Assert.Equal(0x7770B886u, random());
        Assert.Equal(0x28B2B1AFu, random());
    }

    [Fact]
    public async Task CrossLayerTransferSurvivesDeterministicLoss()
    {
        const int frameBytes = 2953;
        const int blockLength = frameBytes - FrameCodec.HeaderLength;
        var source = MakeNoise(300_000, 0x5eed);

        var packed = await OpticalFileContainer.PackAsync(
            "payload.bin",
            "application/octet-stream",
            source);
        var encoder = new CarouselFountainEncoder(
            packed.Container,
            blockLength,
            0xBEEF);
        var payloadFnv = Fnv1a.Hash(packed.Container);

        Assert.True(encoder.SourceBlocks > 100);

        CarouselFountainDecoder? decoder = null;
        var fed = 0;
        for (uint seq = 0; decoder is null || !decoder.IsComplete; seq++)
        {
            Assert.True(seq < 10_000, "The decoder never completed.");

            var frame = FrameCodec.Encode(new Frame(
                FrameCodec.WireVersion,
                0,
                encoder.SessionId,
                seq,
                checked((ushort)encoder.SourceBlocks),
                checked((ushort)blockLength),
                checked((uint)packed.Container.Length),
                payloadFnv,
                encoder.Encode(seq)));

            Assert.Equal(frameBytes, frame.Length);

            var dropped = (((uint)seq * 2654435761u) % 100u) < 15u;
            if (dropped)
                continue;

            Assert.True(FrameCodec.TryDecode(frame, out var decoded));
            decoder ??= new CarouselFountainDecoder(
                decoded.SourceBlocks,
                decoded.BlockLength,
                decoded.SessionId,
                checked((int)decoded.TotalLength));
            decoder.AddFrame(decoded.Sequence, decoded.Payload);
            fed++;
        }

        var recoveredContainer = decoder!.Assemble();
        Assert.NotNull(recoveredContainer);
        Assert.Equal(payloadFnv, Fnv1a.Hash(recoveredContainer!));

        var file = await OpticalFileContainer.UnpackAsync(recoveredContainer!);
        Assert.True(OpticalFileContainer.VerifySha256(file));
        Assert.Equal("payload.bin", file.Name);
        Assert.Equal(source, file.Bytes);
        Assert.True((double)fed / encoder.SourceBlocks < 1.3, $"Overhead {(double)fed / encoder.SourceBlocks:0.000}x is too high.");
    }

    private static byte[] MakeNoise(int length, uint seed)
    {
        var output = new byte[length];
        var random = SplitMix32.Create(seed);
        for (var i = 0; i < output.Length; i++)
            output[i] = (byte)random();
        return output;
    }
}
