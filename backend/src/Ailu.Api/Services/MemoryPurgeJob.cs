using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

// "Deleting really deletes" (roadmap): a deleted memory sits in the bin for 30 days, then its files
// and rows are gone. Storing data the couple no longer has would cost money and break their trust.
public sealed class MemoryPurgeJob(AiluDb db, IPhotoStore store, TimeProvider clock, ILogger<MemoryPurgeJob> log)
{
    public static readonly TimeSpan BinTime = TimeSpan.FromDays(30);

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var cutoff = clock.GetUtcNow() - BinTime;
        var due = await db.Memories.Include(m => m.Photos)
            .Where(m => m.DeletedAt != null && m.DeletedAt < cutoff)
            .Take(100).ToListAsync(ct);

        foreach (var memory in due)
        {
            foreach (var photo in memory.Photos)
                await store.DeleteFolderAsync(PhotoKeys.Folder(photo.CoupleId, photo.Id), ct);
            db.Memories.Remove(memory);
        }
        await db.SaveChangesAsync(ct);
        if (due.Count > 0) log.LogInformation("Purged {Count} deleted memories", due.Count);
        return due.Count;
    }
}

public sealed class MemoryPurgeScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<MemoryPurgeScheduler> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<MemoryPurgeJob>().RunOnceAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Memory purge failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
