using OptiCopy.Core.Fountain;
using OptiCopy.Core.Protocol;

namespace OptiCopy.Core.Transfer;

public sealed record OpticalTransferMetadata(
    string FileName,
    string MimeType,
    int OriginalSize,
    int TransmittedSize,
    OpticalFileContainer.CompressionMode Compression,
    string Sha256,
    ushort SessionId,
    int SourceBlocks,
    int BlockLength,
    uint PayloadFnv);

public sealed record OpticalTransferFrame(
    uint Sequence,
    Frame Frame,
    string PayloadBase64,
    string ProtocolPacket);

/// <summary>
/// Owns the sender-side DCF2 container, carousel fountain encoder and v3 frame
/// header. The Windows UI only asks for the next frame and never constructs
/// protocol bytes itself.
/// </summary>
public sealed class OpticalTransferSession
{
    private readonly CarouselFountainEncoder _encoder;
    private uint _nextSequence;

    private OpticalTransferSession(
        byte[] container,
        OpticalTransferMetadata metadata,
        CarouselFountainEncoder encoder)
    {
        Container = container;
        Metadata = metadata;
        _encoder = encoder;
    }

    public byte[] Container { get; }
    public OpticalTransferMetadata Metadata { get; }
    public uint FramesEmitted { get; private set; }
    public uint MinimumFrames => checked((uint)Math.Max(1, _encoder.SourceBlocks * 2));

    public static async Task<OpticalTransferSession> CreateAsync(
        ReadOnlyMemory<byte> bytes,
        string fileName,
        string mimeType,
        ushort sessionId,
        int blockLength = 256,
        CancellationToken cancellationToken = default)
    {
        var packed = await OpticalFileContainer.PackAsync(
            fileName,
            mimeType,
            bytes,
            cancellationToken).ConfigureAwait(false);

        var encoder = new CarouselFountainEncoder(
            packed.Container,
            blockLength,
            sessionId);

        var metadata = new OpticalTransferMetadata(
            fileName,
            mimeType,
            packed.OriginalSize,
            packed.TransmittedSize,
            packed.Compression,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.Span)),
            sessionId,
            encoder.SourceBlocks,
            encoder.BlockLength,
            Fnv1a.Hash(packed.Container));

        return new OpticalTransferSession(packed.Container, metadata, encoder);
    }

    public void Reset()
    {
        _nextSequence = 0;
        FramesEmitted = 0;
    }

    public OpticalTransferFrame NextFrame()
    {
        var sequence = _nextSequence++;
        var payload = _encoder.Encode(sequence);
        var frame = new Frame(
            FrameCodec.WireVersion,
            0,
            Metadata.SessionId,
            sequence,
            checked((ushort)Metadata.SourceBlocks),
            checked((ushort)Metadata.BlockLength),
            checked((uint)Container.Length),
            Metadata.PayloadFnv,
            payload);

        FramesEmitted++;
        return new OpticalTransferFrame(
            sequence,
            frame,
            Convert.ToBase64String(FrameCodec.Encode(frame)),
            string.Empty);
    }
}
