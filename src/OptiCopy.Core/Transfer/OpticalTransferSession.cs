using System.Globalization;
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
    string PayloadBase64,
    string ProtocolPacket);

public sealed class OpticalTransferSession
{
    private const string ProtocolVersion = "DOT3";
    private readonly CarouselFountainEncoder _encoder;
    private readonly uint _frameCountHint;
    private readonly string _transferId;

    private OpticalTransferSession(byte[] payload, OpticalTransferMetadata metadata, CarouselFountainEncoder encoder, uint frameCountHint, string transferId)
    {
        Payload = payload;
        Metadata = metadata;
        _encoder = encoder;
        _frameCountHint = frameCountHint;
        _transferId = transferId;
    }

    public byte[] Payload { get; }
    public OpticalTransferMetadata Metadata { get; }
    public uint Sequence { get; private set; }
    public uint FramesEmitted { get; private set; }

    public static OpticalTransferSession Create(byte[] payload, string fileName, string mimeType, ushort sessionId, ushort blockLength = 360, uint repairFramesPerBlock = 3)
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
        var sourceBlocks = checked((ushort)encoder.SourceBlocks);
        var frameCountHint = sourceBlocks;
        var transferId = sessionId.ToString("x4", CultureInfo.InvariantCulture);

        var metadata = new OpticalTransferMetadata(
            sessionId,
            SanitizeMetadata(fileName),
            SanitizeMetadata(mimeType),
            payload.LongLength,
            sha256,
            sourceBlocks,
            blockLength,
            frameCountHint);

        return new OpticalTransferSession(payload, metadata, encoder, frameCountHint, transferId);
    }

    public OpticalTransferFrame NextFrame()
    {
        if (FramesEmitted >= Metadata.SourceBlocks)
            throw new InvalidOperationException("The systematic DOT3 transfer cycle is complete.");

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

        var binaryFrameBase64 = Convert.ToBase64String(FrameCodec.Encode(frame));
        var chunkBase64 = Convert.ToBase64String(encoded);
        var protocolPacket = string.Join(
            '|',
            ProtocolVersion,
            _transferId,
            sequence,
            Metadata.SourceBlocks,
            Metadata.SourceBlocks,
            Metadata.OriginalLength,
            Metadata.Sha256,
            "0",
            SanitizeMetadata(Metadata.MimeType),
            SanitizeMetadata(Metadata.FileName),
            chunkBase64);

        return new OpticalTransferFrame(sequence, frame, binaryFrameBase64, protocolPacket);
    }

    public void Reset()
    {
        Sequence = 0;
        FramesEmitted = 0;
    }

    public uint MinimumFrames => _frameCountHint;

    private static string SanitizeMetadata(string value) => value.Replace('|', '_').Replace('\r', '_').Replace('\n', '_');
}
