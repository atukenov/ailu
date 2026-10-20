using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

// "Deleting really deletes" (roadmap): a deleted memory sits in the bin for 30 days, then its files
// (in every storage class) and rows are gone. Storing data the couple no longer has would cost money
// and break their trust.
public sealed class MemoryPurgeJob(AiluDb db, IPhotoStore store, TimeProvider clock, ILogger<MemoryPurgeJob> log)
{
    public static readonly TimeSpan BinTime = TimeSpan.FromDays(30);

    // A folder with no photo row is left alone this long, so an upload still in flight (file first,
    // row second) is never swept away under it.
    public static readonly TimeSpan OrphanGrace = TimeSpan.FromDays(1);
    private const int Batch = 100;

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var cutoff = clock.GetUtcNow() - BinTime;
        var failed = new HashSet<Guid>();
        var purged = 0;
        while (true)
        {
            var due = await db.Memories.Include(m => m.Photos)
                .Where(m => m.DeletedAt != null && m.DeletedAt < cutoff && !failed.Contains(m.Id))
                .OrderBy(m => m.DeletedAt).Take(Batch).ToListAsync(ct);
            if (due.Count == 0) break;

            foreach (var memory in due)
            {
                try
                {
                    foreach (var photo in memory.Photos)
                        await store.DeleteFolderAsync(PhotoKeys.Folder(photo.CoupleId, photo.Id), ct);
                    db.Memories.Remove(memory);
                    purged++;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // The rows stay, so the next run tries the files again; one stuck file can't hold up the rest.
                    failed.Add(memory.Id);
                    log.LogError(e, "Could not purge memory {MemoryId}", memory.Id);
                }
            }
            await db.SaveChangesAsync(ct);
        }
        if (purged > 0) log.LogInformation("Purged {Count} deleted memories", purged);
        return purged;
    }

    // Files no row points to: an upload that crashed between the file and the row, a size generated
    // while the purge removed its folder, a deletion whose file step failed. Nobody can see them, but
    // they would be paid for forever.
    public async Task<int> SweepOrphansAsync(CancellationToken ct = default)
    {
        var before = clock.GetUtcNow() - OrphanGrace;
        var swept = 0;
        var chunk = new List<(string Key, Guid PhotoId)>();
        await foreach (var folder in store.ListPhotoFoldersAsync(ct))
        {
            if (folder.ModifiedAt >= before || PhotoKeys.PhotoOf(folder.Key) is not Guid id) continue;
            chunk.Add((folder.Key, id));
            if (chunk.Count == 500) swept += await SweepAsync(chunk, ct);
        }
        swept += await SweepAsync(chunk, ct);
        if (swept > 0) log.LogWarning("Removed {Count} photo folders no memory points to", swept);
        return swept;
    }

    private async Task<int> SweepAsync(List<(string Key, Guid PhotoId)> chunk, CancellationToken ct)
    {
        var ids = chunk.Select(c => c.PhotoId).ToList();
        var known = (await db.MemoryPhotos.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var swept = 0;
        foreach (var (key, id) in chunk.Where(c => !known.Contains(c.PhotoId)))
        {
            await store.DeleteFolderAsync(key, ct);
            swept++;
        }
        chunk.Clear();
        return swept;
    }
}

// Purges the bin and archives old photos every hour; looks for orphaned files once a day.
public sealed class MemoryPurgeScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<MemoryPurgeScheduler> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        DateTimeOffset? lastSweep = null;
        do
        {
            await RunAsync<MemoryPurgeJob>(j => j.RunOnceAsync(stoppingToken), "Memory purge");
            await RunAsync<PhotoArchiveJob>(j => j.RunOnceAsync(stoppingToken), "Photo archiving");
            if (lastSweep is null || clock.GetUtcNow() - lastSweep >= TimeSpan.FromDays(1))
            {
                lastSweep = clock.GetUtcNow();
                await RunAsync<MemoryPurgeJob>(j => j.SweepOrphansAsync(stoppingToken), "Orphan photo sweep");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync<TJob>(Func<TJob, Task<int>> run, string what) where TJob : notnull
    {
        try
        {
            using var scope = scopes.CreateScope();
            await run(scope.ServiceProvider.GetRequiredService<TJob>());
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "{What} failed", what);
        }
    }
}
