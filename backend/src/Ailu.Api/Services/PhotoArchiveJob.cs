using Ailu.Api.Data;
using Ailu.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

// Cold storage (roadmap, phase 4): a year after upload, a photo's full and screen sizes move to the
// archive storage class, which costs about half. They open a little slower, which is fine for an old
// memory. Thumbnails stay in the standard class, so scrolling the memory list stays quick.
// Photos in the bin are left alone: they are gone within 30 days, and archive classes charge a
// minimum storage time.
public sealed class PhotoArchiveJob(AiluDb db, IPhotoStore store, TimeProvider clock, ILogger<PhotoArchiveJob> log)
{
    public static readonly TimeSpan Age = TimeSpan.FromDays(365);
    public static readonly int[] ArchivedSizes = [PhotoProcessor.Screen, PhotoProcessor.Full];
    private const int Batch = 200;

    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var cutoff = clock.GetUtcNow() - Age;
        var failed = new HashSet<Guid>();
        var moved = 0;
        while (true)
        {
            var due = await db.MemoryPhotos
                .Where(p => p.ArchivedAt == null && p.CreatedAt < cutoff && !failed.Contains(p.Id) &&
                            db.Memories.Any(m => m.Id == p.MemoryId && m.DeletedAt == null))
                .OrderBy(p => p.CreatedAt).Take(Batch).ToListAsync(ct);
            if (due.Count == 0) break;

            foreach (var photo in due)
            {
                try
                {
                    foreach (var size in ArchivedSizes)
                        await store.ArchiveAsync(PhotoKeys.File(photo.CoupleId, photo.Id, size), ct);
                    photo.ArchivedAt = clock.GetUtcNow();
                    moved++;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Tried again on the next run; the photo still opens from wherever its files are.
                    failed.Add(photo.Id);
                    log.LogError(e, "Could not archive photo {PhotoId}", photo.Id);
                }
            }
            await db.SaveChangesAsync(ct);
        }
        if (moved > 0) log.LogInformation("Moved {Count} photos to archive storage", moved);
        return moved;
    }
}
