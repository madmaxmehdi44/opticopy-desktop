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
    private readonly CarouselFountainEncoder _encoder;
    private readonly uint _frameCountHint;
    private readonly uint _containerFnv;

    private OpticalTransferSession(
        byte[] originalPayload,
        byte[] container,
        OpticalTransferMetadata metadata,
        CarouselFountainEncoder encoder,
        uint frameCountHint,
        uint containerFnv)
    {
        Payload = originalPayload;
        Container = container;
        Metadata = metadata;
        _encoder = encoder;
        _frameCountHint = frameCountHint;
        _containerFnv = containerFnv;
    }

    public byte[] Payload { get; }
    public byte[] Container { get; }
    public OpticalTransferMetadata Metadata { get; }
    public uint Sequence { get; private set; }
    public uint FramesEmitted { get; private set; }

    public static OpticalTransferSession Create(
        byte[] payload,
        string fileName,
        string mimeType,
        ushort sessionId,
        ushort blockLength = 360,
        uint repairFramesPerBlock = 3)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentOutOfRangeException.ThrowIfZero(sessionId);
        ArgumentOutOfRangeException.ThrowIfZero(blockLength);
        ArgumentOutOfRangeException.ThrowIfZero(repairFramesPerBlock);
        if (payload.LongLength > uint.MaxValue)
            throw new NotSupportedException("The current wire format supports payloads up to 4 GiB.");

        var container = OpticalFileContainer.Pack(fileName, mimeType, payload);
        var sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var encoder = new CarouselFountainEncoder(container, blockLength, sessionId);
        var sourceBlocks = checked((ushort)encoder.SourceBlocks);
        var frameCountHint = checked((uint)FrameComposition.CycleLength(encoder.SourceBlocks));
        var containerFnv = Fnv1a.Hash(container);

        var metadata = new OpticalTransferMetadata(
            sessionId,
            SanitizeMetadata(fileName),
            SanitizeMetadata(mimeType),
            payload.LongLength,
            sha256,
            sourceBlocks,
            blockLength,
            frameCountHint);

        return new OpticalTransferSession(
            payload,
            container,
            metadata,
            encoder,
            frameCountHint,
            containerFnv);
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
            checked((uint)Container.Length),
            _containerFnv,
            encoded);

        var wire = FrameCodec.Encode(frame);
        return new OpticalTransferFrame(
            sequence,
            frame,
            Convert.ToBase64String(wire),
            string.Empty);
    }

    public void Reset()
    {
        Sequence = 0;
        FramesEmitted = 0;
    }

    public uint MinimumFrames => _frameCountHint;

    private static string SanitizeMetadata(string value) => value.Replace('|', '_').Replace('\r', '_').Replace('\n', '_');
}
