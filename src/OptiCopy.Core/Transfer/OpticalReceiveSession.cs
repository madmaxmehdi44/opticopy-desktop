using OptiCopy.Core.Fountain;
using OptiCopy.Core.Protocol;

namespace OptiCopy.Core.Transfer;

public enum ReceiveFrameResult
{
    Foreign,
    OlderSender,
    NewerSender,
    UnsupportedFlags,
    Malformed,
    Started,
    Duplicate,
    Accepted,
    Complete,
    InvalidPayload,
    InvalidContainer,
    HashMismatch
}

public sealed record OpticalReceiveProgress(
    ushort SessionId,
    int SourceBlocks,
    int SolvedBlocks,
    int NewFrames,
    int DuplicateFrames,
    int RedundantFrames,
    int TotalLength,
    bool IsComplete,
    double EstimatedProgress);

public sealed record OpticalReceivedFile(
    ushort SessionId,
    string FileName,
    string MimeType,
    byte[] Bytes,
    string Sha256,
    OpticalFileContainer.CompressionMode Compression,
    int OriginalSize,
    int TransmittedSize);

/// <summary>
/// Receiver-side Decimen v3 state machine. Camera/QR code backends feed the
/// successful QR RawBytes here; stream identity changes automatically reset
/// the fountain decoder so a restarted sender can be acquired mid-flight.
/// </summary>
public sealed class OpticalReceiveSession
{
    private CarouselFountainDecoder? _decoder;
    private string? _streamIdentity;
    private FrameHeader _header;

    public FrameHeader? Header => _decoder is null ? null : _header;
    public OpticalReceiveProgress Progress => BuildProgress();
    public OpticalReceivedFile? CompletedFile { get; private set; }

    public void Reset()
    {
        _decoder = null;
        _streamIdentity = null;
        _header = default;
        CompletedFile = null;
    }

    public ReceiveFrameResult AcceptFrame(ReadOnlySpan<byte> wireBytes)
    {
        CompletedFile = null;

        var verdict = FrameCodec.Classify(wireBytes);
        switch (verdict.Kind)
        {
            case FrameVerdictKind.Foreign:
                return ReceiveFrameResult.Foreign;
            case FrameVerdictKind.OlderSender:
                return ReceiveFrameResult.OlderSender;
            case FrameVerdictKind.NewerSender:
                return ReceiveFrameResult.NewerSender;
            case FrameVerdictKind.UnsupportedFlags:
                return ReceiveFrameResult.UnsupportedFlags;
            case FrameVerdictKind.Malformed:
                return ReceiveFrameResult.Malformed;
        }

        if (!FrameCodec.TryDecode(wireBytes, out var frame))
            return ReceiveFrameResult.Malformed;

        if (frame.TotalLength == 0 || frame.TotalLength > FrameCodec.MaxFileBytes || frame.SourceBlocks == 0 || frame.BlockLength == 0)
            return ReceiveFrameResult.Malformed;

        var header = new FrameHeader(
            frame.SessionId,
            frame.Sequence,
            frame.SourceBlocks,
            frame.BlockLength,
            frame.TotalLength,
            frame.PayloadFnv,
            frame.Flags);
        var identity = FrameCodec.StreamIdentity(header);
        var started = false;

        if (_decoder is null || !string.Equals(identity, _streamIdentity, StringComparison.Ordinal))
        {
            try
            {
                _decoder = new CarouselFountainDecoder(
                    frame.SourceBlocks,
                    frame.BlockLength,
                    frame.SessionId,
                    checked((int)frame.TotalLength));
            }
            catch (ArgumentOutOfRangeException)
            {
                return ReceiveFrameResult.Malformed;
            }

            _header = header;
            _streamIdentity = identity;
            started = true;
        }

        var before = _decoder.NewFrames;
        try
        {
            _decoder.AddFrame(frame.Sequence, frame.Payload);
        }
        catch (ArgumentException)
        {
            return ReceiveFrameResult.InvalidPayload;
        }

        if (_decoder.NewFrames == before)
            return ReceiveFrameResult.Duplicate;

        if (!_decoder.IsComplete)
            return started ? ReceiveFrameResult.Started : ReceiveFrameResult.Accepted;

        var container = _decoder.Assemble();
        if (container is null)
            return ReceiveFrameResult.Accepted;

        OpticalFileContainer.UnpackedFile unpacked;
        try
        {
            unpacked = OpticalFileContainer.UnpackAsync(container).GetAwaiter().GetResult();
        }
        catch (InvalidDataException)
        {
            return ReceiveFrameResult.InvalidContainer;
        }

        if (!OpticalFileContainer.VerifySha256(unpacked))
            return ReceiveFrameResult.HashMismatch;

        CompletedFile = new OpticalReceivedFile(
            frame.SessionId,
            unpacked.Name,
            unpacked.Type,
            unpacked.Bytes,
            Convert.ToHexString(unpacked.Sha256).ToLowerInvariant(),
            unpacked.Compression,
            unpacked.Bytes.Length,
            unpacked.TransmittedSize);
        return ReceiveFrameResult.Complete;
    }

    private OpticalReceiveProgress BuildProgress()
    {
        if (_decoder is null)
            return new OpticalReceiveProgress(0, 0, 0, 0, 0, 0, 0, false, 0);

        var sourceBlocks = _decoder.SourceBlocks;
        var frameEstimate = Math.Max(sourceBlocks + Math.Max(1, sourceBlocks / 5), 1);
        var collected = _decoder.NewFrames;
        var estimated = _decoder.IsComplete
            ? 1d
            : Math.Min(0.99d, collected / (double)frameEstimate);

        return new OpticalReceiveProgress(
            _header.SessionId,
            sourceBlocks,
            _decoder.SolvedCount,
            _decoder.NewFrames,
            _decoder.DuplicateFrames,
            _decoder.RedundantFrames,
            checked((int)_header.TotalLength),
            _decoder.IsComplete,
            estimated);
    }
}
