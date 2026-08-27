namespace OptiCopy.Core.Fountain;

public static class SplitMix32
{
    public static Func<uint> Create(uint seed)
    {
        var state = seed;
        return () =>
        {
            state = unchecked(state + 0x9E3779B9u);
            var z = state;
            z = unchecked((z ^ (z >> 16)) * 0x21F0AAADu);
            z ^= z >> 15;
            z = unchecked((z ^ (z >> 15)) * 0x735A2D97u);
            z ^= z >> 15;
            return z;
        };
    }
}

public static class FrameComposition
{
    private const int RepairMin = 4;
    private const int RepairMax = 24;

    public static int CycleLength(int k) => checked(k * 2);

    public static int[] Compose(int k, ushort sessionId, uint sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(k);
        var position = (int)(sequence % (uint)CycleLength(k));
        return position < k ? [position] : RepairIndices(k, sessionId, sequence);
    }

    private static int[] RepairIndices(int k, ushort sessionId, uint sequence)
    {
        var rnd = SplitMix32.Create(FrameSeed(sessionId, sequence));
        var degree = Math.Min(k, RepairMin + (int)(rnd() % (RepairMax - RepairMin + 1)));
        var selected = new HashSet<int>();
        while (selected.Count < degree) selected.Add((int)(rnd() % (uint)k));
        return selected.ToArray();
    }

    private static uint FrameSeed(ushort sessionId, uint sequence)
    {
        var h = unchecked((uint)((sessionId + 1) * 0x9E3779B1u)) ^ unchecked(sequence + 0x85EBCA6Bu);
        h = unchecked((h ^ (h >> 13)) * 0xC2B2AE35u);
        return h ^ (h >> 16);
    }
}

public sealed class CarouselFountainEncoder
{
    private readonly byte[][] _blocks;

    public CarouselFountainEncoder(ReadOnlySpan<byte> payload, int blockLength, ushort sessionId)
    {
        if (blockLength is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(blockLength), blockLength, "Block length must be between 1 and UInt16.MaxValue.");
        if (payload.IsEmpty) throw new ArgumentException("Payload cannot be empty.", nameof(payload));

        BlockLength = blockLength;
        SessionId = sessionId;
        TotalLength = payload.Length;
        SourceBlocks = Math.Max(1, (payload.Length + blockLength - 1) / blockLength);
        if (SourceBlocks > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(blockLength), "Source block count exceeds the Decimen u16 wire field.");

        _blocks = new byte[SourceBlocks][];
        for (var i = 0; i < SourceBlocks; i++)
        {
            var block = new byte[blockLength];
            var start = i * blockLength;
            var length = Math.Min(blockLength, payload.Length - start);
            payload.Slice(start, length).CopyTo(block);
            _blocks[i] = block;
        }
    }

    public int SourceBlocks { get; }
    public int BlockLength { get; }
    public ushort SessionId { get; }
    public int TotalLength { get; }

    public byte[] Encode(uint sequence)
    {
        var indices = FrameComposition.Compose(SourceBlocks, SessionId, sequence);
        var output = new byte[BlockLength];
        foreach (var index in indices)
            for (var i = 0; i < BlockLength; i++) output[i] ^= _blocks[index][i];
        return output;
    }
}

public sealed class CarouselFountainDecoder
{
    private sealed class Pending
    {
        public required HashSet<int> Indices { get; init; }
        public required byte[] Words { get; init; }
    }

    private readonly byte[][] _solved;
    private readonly Dictionary<int, HashSet<Pending>> _waiting = new();
    private readonly HashSet<uint> _seen = new();

    public CarouselFountainDecoder(int sourceBlocks, int blockLength, ushort sessionId, int totalLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceBlocks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(blockLength);
        ArgumentOutOfRangeException.ThrowIfNegative(totalLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sourceBlocks, ushort.MaxValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(totalLength, global::OptiCopy.Core.Protocol.FrameCodec.MaxFileBytes);

        SourceBlocks = sourceBlocks;
        BlockLength = blockLength;
        SessionId = sessionId;
        TotalLength = totalLength;
        _solved = new byte[sourceBlocks][];
    }

    public int SourceBlocks { get; }
    public int BlockLength { get; }
    public ushort SessionId { get; }
    public int TotalLength { get; }
    public int SolvedCount { get; private set; }
    public int NewFrames { get; private set; }
    public int DuplicateFrames { get; private set; }
    public int RedundantFrames { get; private set; }
    public bool IsComplete => SolvedCount >= SourceBlocks;

    public void AddFrame(uint sequence, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < BlockLength) throw new ArgumentException("Frame payload is shorter than block length.", nameof(payload));
        if (!_seen.Add(sequence)) { DuplicateFrames++; return; }
        NewFrames++;
        if (IsComplete) return;

        var indices = new HashSet<int>(FrameComposition.Compose(SourceBlocks, SessionId, sequence));
        var words = payload[..BlockLength].ToArray();
        foreach (var block in indices.ToArray())
        {
            if (_solved[block] is { } solved)
            {
                Xor(words, solved);
                indices.Remove(block);
            }
        }

        if (indices.Count == 0) { RedundantFrames++; return; }
        if (indices.Count == 1) { Resolve(indices.Single(), words); return; }

        var pending = new Pending { Indices = indices, Words = words };
        foreach (var block in indices)
        {
            if (!_waiting.TryGetValue(block, out var set))
            {
                set = new HashSet<Pending>();
                _waiting[block] = set;
            }
            set.Add(pending);
        }
    }

    public byte[]? Assemble()
    {
        if (!IsComplete) return null;
        var result = new byte[TotalLength];
        for (var i = 0; i < SourceBlocks; i++)
        {
            var start = i * BlockLength;
            var length = Math.Min(BlockLength, TotalLength - start);
            if (length > 0) Buffer.BlockCopy(_solved[i]!, 0, result, start, length);
        }
        return result;
    }

    private void Resolve(int block, byte[] value)
    {
        var queue = new Stack<(int Block, byte[] Value)>();
        queue.Push((block, value));
        while (queue.Count != 0)
        {
            var (current, bytes) = queue.Pop();
            if (_solved[current] is not null) continue;
            _solved[current] = bytes;
            SolvedCount++;
            if (!_waiting.TryGetValue(current, out var waiting)) continue;
            _waiting.Remove(current);
            foreach (var pending in waiting.ToArray())
            {
                Xor(pending.Words, bytes);
                pending.Indices.Remove(current);
                if (pending.Indices.Count == 1)
                {
                    var next = pending.Indices.Single();
                    _waiting.TryGetValue(next, out var set);
                    set?.Remove(pending);
                    if (_solved[next] is null) queue.Push((next, pending.Words));
                }
            }
        }
    }

    private static void Xor(byte[] left, byte[] right)
    {
        for (var i = 0; i < left.Length; i++) left[i] ^= right[i];
    }
}
