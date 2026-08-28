using System.Text.Json;
using OptiCopy.Core.Fountain;
using OptiCopy.Core.Protocol;
using Xunit;

namespace OptiCopy.Tests;

public sealed class DecimenInteropFactAttribute : FactAttribute
{
    public DecimenInteropFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DECIMEN_FIXTURE_ROOT")))
            Skip = "Cross-platform interoperability tests require DECIMEN_FIXTURE_ROOT; CI configures it automatically.";
    }
}

public sealed class CrossPlatformInteropTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private sealed record TsFixture(string Source, string Dcf2, string Frame, FountainFixture Fountain);
    private sealed record FountainFixture(int BlockLength, ushort SessionId, int TotalLength, int K, uint PayloadFnv, string[] Frames);
    private sealed record CsFixture(string Source, string Dcf2, Dictionary<string, string> Frames, CsFountainFixture Fountain);
    private sealed record CsFountainFixture(int BlockLength, ushort SessionId, int TotalLength, int K, uint PayloadFnv, string[] Frames);

    [DecimenInteropFact]
    public async Task CSharpDecodesTypeScriptDcf2FrameAndFountainFixtures()
    {
        var root = GetFixtureRoot();
        var path = Path.Combine(root, "ts-to-cs.json");
        Assert.True(File.Exists(path), $"TypeScript fixture not found: {path}");

        var fixture = JsonSerializer.Deserialize<TsFixture>(await File.ReadAllTextAsync(path), JsonOptions);
        Assert.NotNull(fixture);

        var source = Convert.FromBase64String(fixture!.Source);
        var dcf2 = Convert.FromBase64String(fixture.Dcf2);
        var unpacked = await OpticalFileContainer.UnpackAsync(dcf2);
        Assert.Equal("test.bin", unpacked.Name);
        Assert.Equal("application/octet-stream", unpacked.Type);
        Assert.Equal(source, unpacked.Bytes);
        Assert.True(OpticalFileContainer.VerifySha256(unpacked));

        var frameBytes = Convert.FromBase64String(fixture.Frame);
        Assert.True(FrameCodec.TryDecode(frameBytes, out var frame));
        Assert.Equal(FrameCodec.WireVersion, frame.Version);
        Assert.Equal((ushort)0xBEEF, frame.SessionId);
        Assert.Equal(0x01020304u, frame.Sequence);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, frame.Payload);
        Assert.Equal(fixture.Fountain.PayloadFnv, frame.PayloadFnv);
        Assert.Equal(fixture.Fountain.TotalLength, (int)frame.TotalLength);

        var decoder = new CarouselFountainDecoder(fixture.Fountain.K, fixture.Fountain.BlockLength, fixture.Fountain.SessionId, fixture.Fountain.TotalLength);
        foreach (var encoded in fixture.Fountain.Frames)
        {
            var bytes = Convert.FromBase64String(encoded);
            Assert.True(FrameCodec.TryDecode(bytes, out var decoded));
            decoder.AddFrame(decoded.Sequence, decoded.Payload);
        }

        var recovered = decoder.Assemble();
        Assert.NotNull(recovered);
        Assert.Equal(fixture.Fountain.PayloadFnv, Fnv1a.Hash(recovered!));
        var recoveredFile = await OpticalFileContainer.UnpackAsync(recovered!);
        Assert.True(OpticalFileContainer.VerifySha256(recoveredFile));
        Assert.Equal(source, recoveredFile.Bytes);
    }

    [DecimenInteropFact]
    public async Task CSharpProducesFixturesForTypeScriptDecoder()
    {
        var root = GetFixtureRoot();
        Directory.CreateDirectory(root);

        var source = new byte[] { 0, 1, 2, 127, 128, 254, 255, 0x44, 0x43, 0x46, 0x32 };
        var packed = await OpticalFileContainer.PackAsync("interop/test.bin", "application/octet-stream", source);
        var payloadFnv = Fnv1a.Hash(packed.Container);

        var framePayload = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 };
        var frame = new Frame(FrameCodec.WireVersion, 0, 0xBEEF, 0x01020304, 3, checked((ushort)framePayload.Length), checked((uint)packed.Container.Length), payloadFnv, framePayload);

        var encoder = new CarouselFountainEncoder(packed.Container, 8, 0xBEEF);
        var fountainFrames = new List<string>();
        for (uint seq = 0; seq < (uint)(encoder.SourceBlocks * 2); seq++)
        {
            var fountainFrame = new Frame(FrameCodec.WireVersion, 0, encoder.SessionId, seq, checked((ushort)encoder.SourceBlocks), checked((ushort)encoder.BlockLength), checked((uint)packed.Container.Length), payloadFnv, encoder.Encode(seq));
            fountainFrames.Add(Convert.ToBase64String(FrameCodec.Encode(fountainFrame)));
        }

        var fixture = new CsFixture(
            Convert.ToBase64String(source),
            Convert.ToBase64String(packed.Container),
            new Dictionary<string, string> { ["fixed"] = Convert.ToBase64String(FrameCodec.Encode(frame)) },
            new CsFountainFixture(encoder.BlockLength, encoder.SessionId, encoder.TotalLength, encoder.SourceBlocks, payloadFnv, fountainFrames.ToArray()));

        await File.WriteAllTextAsync(Path.Combine(root, "cs-to-ts.json"), JsonSerializer.Serialize(fixture, JsonOptions));
    }

    private static string GetFixtureRoot() => Environment.GetEnvironmentVariable("DECIMEN_FIXTURE_ROOT")
        ?? throw new InvalidOperationException("DECIMEN_FIXTURE_ROOT must be configured.");
}
