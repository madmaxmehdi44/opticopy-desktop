using OptiCopy.Data;
using Xunit;

namespace OptiCopy.Tests;

public sealed class TransferHistoryStoreTests
{
    [Fact]
    public async Task AddAndGetPreservesNewestFirstOrder()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opticopy-history-{Guid.NewGuid():N}.json");
        try
        {
            var store = new TransferHistoryStore(path);
            var first = CreateEntry("first.bin");
            var second = CreateEntry("second.bin");

            await store.AddAsync(first);
            await store.AddAsync(second);

            var entries = await store.GetAsync();
            Assert.Equal(2, entries.Count);
            Assert.Equal(second.Id, entries[0].Id);
            Assert.Equal(first.Id, entries[1].Id);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task UpdateChangesOnlyRequestedEntry()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opticopy-history-{Guid.NewGuid():N}.json");
        try
        {
            var store = new TransferHistoryStore(path);
            var entry = CreateEntry("file.bin");
            await store.AddAsync(entry);

            await store.UpdateAsync(entry.Id, value => value with
            {
                Status = TransferStatus.Completed,
                Frames = 42,
                CompletedAtUtc = DateTimeOffset.UtcNow
            });

            var result = Assert.Single(await store.GetAsync());
            Assert.Equal(TransferStatus.Completed, result.Status);
            Assert.Equal((uint)42, result.Frames);
            Assert.NotNull(result.CompletedAtUtc);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ClearRemovesAllEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opticopy-history-{Guid.NewGuid():N}.json");
        try
        {
            var store = new TransferHistoryStore(path);
            await store.AddAsync(CreateEntry("file.bin"));
            await store.ClearAsync();

            Assert.Empty(await store.GetAsync());
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static TransferHistoryEntry CreateEntry(string fileName) => new(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        null,
        TransferDirection.Send,
        TransferStatus.Started,
        fileName,
        "application/octet-stream",
        100,
        100,
        "abc123",
        1,
        0,
        1,
        1443,
        null);
}
