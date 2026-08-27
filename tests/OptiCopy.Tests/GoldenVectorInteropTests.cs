using System.Linq;
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
        var valid = FrameCodec.Encode(new Frame(
            FrameCodec.WireVersion, 0, 0xBEEF, 0x01020304, 0x0111, 6,
            0x00FEDCBA, 0x89ABCDEF, [1, 2, 3, 4, 5, 6]));

        Assert.Equal(FrameVerdictKind.Ok, FrameCodec.Classify(valid).Kind);

        var legacyV1 = valid.ToArray();
        legacyV1[1] = 0x0C;
        Assert.Equal(FrameVerdictKind.OlderSender, FrameCodec.Classify(legacyV1).Kind);
        Assert.Equal(1, FrameCodec.Classify(legacyV1).Version);

        var legacyV2 = valid.ToArray();
        legacyV2[1] = 0x0D;
        Assert.Equal(FrameVerdictKind.OlderSender, FrameCodec.Classify(legacyV2).Kind);
        Assert.Equal(2, FrameCodec.Classify(legacyV2).Version);

        var foreign = valid.ToArray();
        foreign[1] = 0x42;
        Assert.Equal(FrameVerdictKind.Foreign, FrameCodec.Classify(foreign).Kind);

        var wrongMagic = valid.ToArray();
        wrongMagic[0] = 0xD2;
        Assert.Equal(FrameVerdictKind.Foreign, FrameCodec.Classify(wrongMagic).Kind);

        var newer = valid.ToArray();
        newer[2] = 4;
        Assert.Equal(FrameVerdictKind.NewerSender, FrameCodec.Classify(newer).Kind);
        Assert.Equal(4, FrameCodec.Classify(newer).Version);

        var older = valid.ToArray();
        older[2] = 2;
        Assert.Equal(FrameVerdictKind.OlderSender, FrameCodec.Classify(older).Kind);
        Assert.Equal(2, FrameCodec.Classify(older).Version);

        var zeroVersion = valid.ToArray();
        zeroVersion[2] = 0;
        Assert.Equal(FrameVerdictKind.Malformed, FrameCodec.Classify(zeroVersion).Kind);
    }

    [Fact]
    public void CriticalAndIgnorableFlagsMatchDecimenSemantics()
    {
        var critical = FrameCodec.Encode(new Frame(
            FrameCodec.WireVersion, 0, 1, 0, 1, 4, 4, 0, [1, 2, 3, 4]));
        critical[3] = 0x01;
        Assert.Equal(FrameVerdictKind.UnsupportedFlags, FrameCodec.Classify(critical).Kind);

        var ignorable = FrameCodec.Encode(new Frame(
            FrameCodec.WireVersion, 0, 1, 0, 1, 4, 4, 0, [1, 2, 3, 4]));
        ignorable[3] = 0x10;
        Assert.Equal(FrameVerdictKind.Ok, FrameCodec.Classify(ignorable).Kind);
        Assert.True(FrameCodec.TryDecode(ignorable, out var decoded));
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
    public void CarouselRepairVectorsMatchDecimenReferenceImplementation()
    {
        var expected23 = new[] { 1, 5, 7, 8, 12, 15 };
        var expected24 = new[] { 5, 10, 16, 17, 18, 21 };
        var expected25 = new[] { 3, 8, 11, 15, 17, 20 };
        var expected26 = new[] { 10, 11, 13, 22 };

        Assert.Equal(expected23, FrameComposition.Compose(23, 7, 23).OrderBy(x => x).ToArray());
        Assert.Equal(expected24, FrameComposition.Compose(23, 7, 24).OrderBy(x => x).ToArray());
        Assert.Equal(expected25, FrameComposition.Compose(23, 7, 25).OrderBy(x => x).ToArray());
        Assert.Equal(expected26, FrameComposition.Compose(23, 7, 26).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void SplitMix32MatchesDecimenReferenceImplementation()
    {
        var random = SplitMix32.Create(7);
        Assert.Equal(0xE614C12Cu, random());
        Assert.Equal(0xA9ED3E17u, random());
        Assert.Equal(0x74F98B78u, random());
        Assert.Equal(0x777AD4A6u, random());
        Assert.Equal(0x28AE866Fu, random());
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
        var encoder = new CarouselFountainEncoder(packed.Container, blockLength, 0xBEEF);
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
        Assert.True((double)fed / encoder.SourceBlocks < 1.3,
            $"Overhead {(double)fed / encoder.SourceBlocks:0.000}x is too high.");
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
