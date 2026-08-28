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
    public const int DefaultFrameBytes = 1465;
    public const int DefaultBlockLength = DefaultFrameBytes - FrameCodec.HeaderLength;

    private readonly byte[] _container;
    private CarouselFountainEncoder _encoder;
    private uint _nextSequence;

    private OpticalTransferSession(
        byte[] container,
        OpticalTransferMetadata metadata,
        CarouselFountainEncoder encoder)
    {
        _container = container;
        _encoder = encoder;
        Metadata = metadata;
    }

    public byte[] Container => _container;
    public OpticalTransferMetadata Metadata { get; private set; }
    public uint FramesEmitted { get; private set; }
    public uint CycleLength => checked((uint)Math.Max(1, _encoder.SourceBlocks * 2));

    public static async Task<OpticalTransferSession> CreateAsync(
        ReadOnlyMemory<byte> bytes,
        string fileName,
        string mimeType,
        ushort sessionId,
        int blockLength = DefaultBlockLength,
        CancellationToken cancellationToken = default)
    {
        var packed = await OpticalFileContainer.PackAsync(
            fileName,
            mimeType,
            bytes,
            cancellationToken).ConfigureAwait(false);

        return CreateFromPacked(
            packed.Container,
            packed.OriginalSize,
            packed.TransmittedSize,
            packed.Compression,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.Span)),
            fileName,
            mimeType,
            sessionId,
            blockLength);
    }

    /// <summary>
    /// Starts a fresh Decimen stream over the same protected container with a
    /// new session id, matching the reference sender's start semantics.
    /// </summary>
    public OpticalTransferSession Restart(ushort sessionId)
    {
        return CreateFromPacked(
            _container,
            Metadata.OriginalSize,
            Metadata.TransmittedSize,
            Metadata.Compression,
            Metadata.Sha256,
            Metadata.FileName,
            Metadata.MimeType,
            sessionId,
            Metadata.BlockLength);
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
            checked((uint)_container.Length),
            Metadata.PayloadFnv,
            payload);

        FramesEmitted++;
        var wireBytes = FrameCodec.Encode(frame);
        var wireBase64 = Convert.ToBase64String(wireBytes);
        return new OpticalTransferFrame(sequence, frame, wireBase64, wireBase64);
    }

    private static OpticalTransferSession CreateFromPacked(
        byte[] container,
        int originalSize,
        int transmittedSize,
        OpticalFileContainer.CompressionMode compression,
        string sha256,
        string fileName,
        string mimeType,
        ushort sessionId,
        int blockLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(blockLength, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(blockLength, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(container.Length, FrameCodec.MaxFileBytes);

        var encoder = new CarouselFountainEncoder(container, blockLength, sessionId);
        var metadata = new OpticalTransferMetadata(
            fileName,
            mimeType,
            originalSize,
            transmittedSize,
            compression,
            sha256,
            sessionId,
            encoder.SourceBlocks,
            encoder.BlockLength,
            Fnv1a.Hash(container));

        return new OpticalTransferSession(container, metadata, encoder);
    }
}
