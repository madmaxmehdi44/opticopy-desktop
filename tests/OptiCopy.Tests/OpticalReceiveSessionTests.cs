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
        ReceiveFrameResult lastResult = ReceiveFrameResult.Foreign;
        foreach (var frame in frames)
        {
            var wire = Convert.FromBase64String(frame.PayloadBase64);
            lastResult = receiver.AcceptFrame(wire);
            if (frame.Sequence == frames[0].Sequence)
                Assert.Contains(lastResult, new[] { ReceiveFrameResult.Started, ReceiveFrameResult.Accepted, ReceiveFrameResult.Complete });
            if (frame.Sequence == 0)
                Assert.Equal(ReceiveFrameResult.Duplicate, receiver.AcceptFrame(wire));
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
    }

    [Fact]
    public async Task ReceiverResetsAutomaticallyWhenSenderStartsNewSession()
    {
        var payloadA = Enumerable.Repeat((byte)0x11, 4096).ToArray();
        var payloadB = Enumerable.Repeat((byte)0xA7, 4096).ToArray();

        var senderA = await OpticalTransferSession.CreateAsync(payloadA, "a.bin", "application/octet-stream", 100);
        var senderB = await OpticalTransferSession.CreateAsync(payloadB, "b.bin", "application/octet-stream", 200);
        var receiver = new OpticalReceiveSession();

        var frameA = senderA.NextFrame();
        var frameB = senderB.NextFrame();
        Assert.NotEqual(frameA.Frame.SessionId, frameB.Frame.SessionId);

        var resultA = receiver.AcceptFrame(FrameCodec.Encode(frameA.Frame));
        var resultB = receiver.AcceptFrame(FrameCodec.Encode(frameB.Frame));

        Assert.Equal(ReceiveFrameResult.Started, resultA);
        Assert.Equal(ReceiveFrameResult.Started, resultB);
        Assert.Equal((ushort)200, receiver.Progress.SessionId);
        Assert.Equal(0, receiver.Progress.NewFrames - 1);
    }

    [Fact]
    public async Task ReceiverRejectsCorruptedCompletedContainerBySha256()
    {
        var payload = new byte[12_000];
        new Random(99).NextBytes(payload);
        var sender = await OpticalTransferSession.CreateAsync(payload, "sample.dat", "application/octet-stream", 300);
        var receiver = new OpticalReceiveSession();

        var wires = Enumerable.Range(0, (int)sender.CycleLength)
            .Select(_ => sender.NextFrame())
            .Select(static frame => FrameCodec.Encode(frame.Frame))
            .ToArray();

        var corrupted = wires[0].ToArray();
        corrupted[^1] ^= 0x5A;
        Assert.NotEqual(ReceiveFrameResult.Complete, receiver.AcceptFrame(corrupted));
    }
}
