using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptiCopy.Data;

public enum TransferDirection
{
    Send,
    Receive
}

public enum TransferStatus
{
    Started,
    Completed,
    Failed,
    Cancelled
}

public sealed record TransferHistoryEntry(
    Guid Id,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    TransferDirection Direction,
    TransferStatus Status,
    string FileName,
    string MimeType,
    long OriginalSize,
    long TransmittedSize,
    string Sha256,
    ushort SessionId,
    uint Frames,
    int SourceBlocks,
    int BlockLength,
    string? Error);

public sealed class TransferHistoryStore
{
    private const int MaxEntries = 500;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TransferHistoryStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OptiCopy",
            "history.json");
    }

    public async Task<IReadOnlyList<TransferHistoryEntry>> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(TransferHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var next = new List<TransferHistoryEntry>(entries.Count + 1) { entry };
            next.AddRange(entries);
            if (next.Count > MaxEntries)
                next.RemoveRange(MaxEntries, next.Count - MaxEntries);
            await WriteUnsafeAsync(next, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(Guid id, Func<TransferHistoryEntry, TransferHistoryEntry> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var index = entries.FindIndex(item => item.Id == id);
            if (index < 0)
                return;

            entries[index] = update(entries[index]);
            await WriteUnsafeAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<TransferHistoryEntry>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
            return [];

        try
        {
            await using var stream = File.OpenRead(_path);
            var entries = await JsonSerializer.DeserializeAsync<List<TransferHistoryEntry>>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);
            return entries ?? [];
        }
        catch (JsonException)
        {
            var backup = $"{_path}.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.corrupt";
            try
            {
                File.Move(_path, backup, overwrite: false);
            }
            catch
            {
                // A corrupt history file must not prevent transfers from working.
            }

            return [];
        }
    }

    private async Task WriteUnsafeAsync(IReadOnlyList<TransferHistoryEntry> entries, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, entries, JsonOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, _path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Cleanup failure is non-fatal after the atomic move attempt.
            }
        }
    }
}
