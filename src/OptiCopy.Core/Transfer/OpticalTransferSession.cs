using System.Security.Cryptography;
using OptiCopy.Core.Fountain;
using OptiCopy.Core.Protocol;

namespace OptiCopy.Core.Transfer;

public sealed record OpticalTransferMetadata(
    ushort SessionId,
    string FileName,
    string MimeType,
    long OriginalLength,
    string Sha256,
    ushort SourceBlocks,
    ushort BlockLength,
    uint FrameCountHint);

public sealed record OpticalTransferFrame(
    uint Sequence,
    Frame Frame,
    string PayloadBase64);

public sealed class OpticalTransferSession
{
    private readonly CarouselFountainEncoder _encoder;
    private readonly uint _frameCountHint;

    private OpticalTransferSession(byte[] payload, OpticalTransferMetadata metadata, CarouselFountainEncoder encoder, uint frameCountHint)
    {
        Payload = payload;
        Metadata = metadata;
        _encoder = encoder;
        _frameCountHint = frameCountHint;
    }

    public byte[] Payload { get; }
    public OpticalTransferMetadata Metadata { get; }
    public uint Sequence { get; private set; }
    public uint FramesEmitted { get; private set; }

    public static OpticalTransferSession Create(byte[] payload, string fileName, string mimeType, ushort sessionId, ushort blockLength = 768, uint repairFramesPerBlock = 3)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentOutOfRangeException.ThrowIfZero(sessionId);
        ArgumentOutOfRangeException.ThrowIfZero(blockLength);
        ArgumentOutOfRangeException.ThrowIfZero(repairFramesPerBlock);
        if (payload.LongLength > uint.MaxValue)
            throw new NotSupportedException("The current wire format supports payloads up to 4 GiB.");

        var sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var encoder = new CarouselFountainEncoder(payload, blockLength, sessionId);
        var hint = checked((uint)encoder.SourceBlocks * 2u);
        var metadata = new OpticalTransferMetadata(
            sessionId,
            SanitizeMetadata(fileName),
            SanitizeMetadata(mimeType),
            payload.LongLength,
            sha256,
            checked((ushort)encoder.SourceBlocks),
            blockLength,
            hint);

        return new OpticalTransferSession(payload, metadata, encoder, hint);
    }

    public OpticalTransferFrame NextFrame()
    {
        var sequence = Sequence++;
        FramesEmitted++;
        var encoded = _encoder.Encode(sequence);
        var frame = new Frame(
            FrameCodec.WireVersion,
            0,
            Metadata.SessionId,
            sequence,
            Metadata.SourceBlocks,
            Metadata.BlockLength,
            checked((uint)Metadata.OriginalLength),
            Fnv1a.Hash(encoded),
            encoded);

        return new OpticalTransferFrame(sequence, frame, Convert.ToBase64String(FrameCodec.Encode(frame)));
    }

    public void Reset()
    {
        Sequence = 0;
        FramesEmitted = 0;
    }

    public uint MinimumFrames => _frameCountHint;

    private static string SanitizeMetadata(string value) => value.Replace('|', '_').Replace('\r', '_').Replace('\n', '_');
}
