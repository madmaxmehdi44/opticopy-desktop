using OptiCopy.Core.Protocol;
using OptiCopy.Core.Transfer;
using Xunit;

namespace OptiCopy.Tests;

public sealed class OpticalReceiveSessionTests
{
    [Fact]
    public async Task ReceiverReassemblesFileFromShuffledFramesWithDuplicates()
    {
        var payload = new byte[32_000];
        new Random(2026).NextBytes(payload);

        var sender = await OpticalTransferSession.CreateAsync(
            payload,
            "photo.bin",
            "application/octet-stream",
            777);
        var frames = Enumerable.Range(0, (int)sender.CycleLength)
            .Select(_ => sender.NextFrame())
            .OrderByDescending(static frame => frame.Sequence % 3)
            .ThenBy(static frame => frame.Sequence)
            .ToArray();

        var receiver = new OpticalReceiveSession();
        var firstWire = FrameCodec.Encode(frames[0].Frame);
        Assert.Contains(receiver.AcceptFrame(firstWire), new[] { ReceiveFrameResult.Started, ReceiveFrameResult.Accepted, ReceiveFrameResult.Complete });
        Assert.Equal(ReceiveFrameResult.Duplicate, receiver.AcceptFrame(firstWire));

        ReceiveFrameResult lastResult = ReceiveFrameResult.Accepted;
        for (var i = 1; i < frames.Length; i++)
        {
            lastResult = receiver.AcceptFrame(FrameCodec.Encode(frames[i].Frame));
            if (lastResult == ReceiveFrameResult.Complete)
                break;
        }

        Assert.Equal(ReceiveFrameResult.Complete, lastResult);
        Assert.NotNull(receiver.CompletedFile);
        Assert.Equal("photo.bin", receiver.CompletedFile!.FileName);
        Assert.Equal("application/octet-stream", receiver.CompletedFile.MimeType);
        Assert.Equal(payload, receiver.CompletedFile.Bytes);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant(), receiver.CompletedFile.Sha256);
        Assert.True(receiver.Progress.IsComplete);
        Assert.Equal(1d, receiver.Progress.EstimatedProgress);
        Assert.True(receiver.Progress.DuplicateFrames >= 1);
    }

    [Fact]
    public async Task ReceiverResetsAutomaticallyWhenSenderStartsNewSession()
    {
        var payloadA = Enumerable.Repeat((byte)0x11, 4096).ToArray();
        var payloadB = Enumerable.Repeat((byte)0xA7, 4096).ToArray();

        var senderA = await OpticalTransferSession.CreateAsync(payloadA, "a.bin", "application/octet-stream", 100);
        var senderB = await OpticalTransferSession.CreateAsync(payloadB, "b.bin", "application/octet-stream", 200);
        var receiver = new OpticalReceiveSession();

        var resultA = receiver.AcceptFrame(FrameCodec.Encode(senderA.NextFrame().Frame));
        var resultB = receiver.AcceptFrame(FrameCodec.Encode(senderB.NextFrame().Frame));

        Assert.Equal(ReceiveFrameResult.Started, resultA);
        Assert.Equal(ReceiveFrameResult.Started, resultB);
        Assert.Equal((ushort)200, receiver.Progress.SessionId);
        Assert.Equal(1, receiver.Progress.NewFrames);
    }

    [Fact]
    public async Task ReceiverReportsHashMismatchForCorruptedSolvedBlock()
    {
        var payload = new byte[12_000];
        new Random(99).NextBytes(payload);
        var sender = await OpticalTransferSession.CreateAsync(payload, "sample.dat", "application/octet-stream", 300);
        var receiver = new OpticalReceiveSession();

        var frames = Enumerable.Range(0, (int)sender.CycleLength)
            .Select(_ => sender.NextFrame())
            .Select(static frame => FrameCodec.Encode(frame.Frame))
            .ToArray();

        var corruptedFirst = frames[0].ToArray();
        corruptedFirst[^1] ^= 0x5A;

        var result = receiver.AcceptFrame(corruptedFirst);
        Assert.Contains(result, new[] { ReceiveFrameResult.Started, ReceiveFrameResult.Accepted });

        var final = result;
        for (var i = 1; i < frames.Length; i++)
        {
            final = receiver.AcceptFrame(frames[i]);
            if (final is ReceiveFrameResult.HashMismatch or ReceiveFrameResult.InvalidContainer or ReceiveFrameResult.Complete)
                break;
        }

        Assert.Equal(ReceiveFrameResult.HashMismatch, final);
        Assert.Null(receiver.CompletedFile);
    }
}
