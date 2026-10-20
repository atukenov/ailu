using Ailu.Api.Data;
using Ailu.Api.Features.Pairing;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Features.Memories;

public sealed record PastCoupleDto(Guid Id, PartnerDto? Partner, DateTimeOffset StartedAt, DateTimeOffset EndedAt,
    int Memories, DateTimeOffset PartnerPhotosUntil);

// After a couple ends, both keep read-only access to its memories (privacy doc, "Пара распалась"):
// anyone can read, only the author can delete. The partner's photos stay viewable for 30 days after
// the end, then only their author sees them. Nothing here earns нұр or changes the couple.
public static class ArchiveEndpoints
{
    public static readonly TimeSpan PartnerPhotosFor = TimeSpan.FromDays(30);
    private const int PageSize = 20;

    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/archive").WithTags("Archive").RequireAuthorization();
        g.MapGet("", List);
        g.MapGet("/{coupleId:guid}/memories", Memories);
        g.MapGet("/{coupleId:guid}/memories/{id:guid}", Get);
        g.MapDelete("/{coupleId:guid}/memories/{id:guid}", Delete);
    }

    // Newest ended couple first.
    private static async Task<IResult> List(HttpContext http, AiluDb db, CancellationToken ct)
    {
        var me = CurrentUser.Id(http.User);
        var couples = await db.CoupleMembers.Where(m => m.UserId == me && m.Couple!.Status == CoupleStatus.Ended)
            .Select(m => m.Couple!)
            .Select(c => new
            {
                c.Id, c.StartedAt, EndedAt = c.EndedAt ?? c.StartedAt,
                Partner = c.Members.Where(x => x.UserId != me).Select(x => new { x.UserId, x.User!.Name }).FirstOrDefault(),
                Memories = db.Memories.Count(x => x.CoupleId == c.Id && x.DeletedAt == null)
            })
            .OrderByDescending(c => c.EndedAt).ToListAsync(ct);
        return Results.Ok(couples.Select(c => new PastCoupleDto(c.Id,
            c.Partner is null ? null : new PartnerDto(c.Partner.UserId, c.Partner.Name),
            c.StartedAt, c.EndedAt, c.Memories, c.EndedAt + PartnerPhotosFor)).ToList());
    }

    private static async Task<IResult> Memories(Guid coupleId, string? cursor, HttpContext http, AiluDb db, PhotoUrls urls,
        TimeProvider clock, CancellationToken ct)
    {
        if (await PastAsync(coupleId, http, db, ct) is not var (couple, me)) return NotFound();

        var q = Live(db, couple.Id);
        if (cursor is not null)
        {
            if (MemoryEndpoints.Cursor.Parse(cursor) is not var (day, created, id)) return Errors.Validation("cursor", "Неверный курсор.");
            q = q.Where(m => m.HappenedOn < day ||
                             (m.HappenedOn == day && (m.CreatedAt < created || (m.CreatedAt == created && m.Id.CompareTo(id) < 0))));
        }

        var page = await q.OrderByDescending(m => m.HappenedOn).ThenByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(PageSize + 1).ToListAsync(ct);
        var next = page.Count > PageSize ? MemoryEndpoints.Cursor.Of(page[PageSize - 1]) : null;
        return Results.Ok(new MemoryPage(page.Take(PageSize).Select(m => ToDto(m, couple, me, urls, clock)).ToList(), next));
    }

    private static async Task<IResult> Get(Guid coupleId, Guid id, HttpContext http, AiluDb db, PhotoUrls urls,
        TimeProvider clock, CancellationToken ct)
    {
        if (await PastAsync(coupleId, http, db, ct) is not var (couple, me)) return NotFound();
        var memory = await Live(db, couple.Id).SingleOrDefaultAsync(m => m.Id == id, ct);
        return memory is null ? MemoryNotFound() : Results.Ok(ToDto(memory, couple, me, urls, clock));
    }

    // Same as deleting in a live couple: gone at once, files purged after 30 days in the bin.
    private static async Task<IResult> Delete(Guid coupleId, Guid id, HttpContext http, AiluDb db, TimeProvider clock,
        CancellationToken ct)
    {
        if (await PastAsync(coupleId, http, db, ct) is not var (couple, me)) return NotFound();
        var memory = await Live(db, couple.Id).SingleOrDefaultAsync(m => m.Id == id, ct);
        if (memory is null) return MemoryNotFound();
        if (memory.AuthorId != me)
            return Errors.Forbidden("not_author", "Удалить воспоминание может только тот, кто его добавил.");

        var bytes = memory.Photos.Sum(p => p.Bytes);
        var count = memory.Photos.Count;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        memory.DeletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await db.Couples.Where(c => c.Id == couple.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.StorageBytes, c => c.StorageBytes - bytes)
                .SetProperty(c => c.PhotoCount, c => c.PhotoCount - count), ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    // Only an ended couple the caller was part of. Anything else looks like it doesn't exist.
    private static async Task<(Couple, Guid)?> PastAsync(Guid coupleId, HttpContext http, AiluDb db, CancellationToken ct)
    {
        var me = CurrentUser.Id(http.User);
        var couple = await db.Couples.AsNoTracking().SingleOrDefaultAsync(c => c.Id == coupleId && c.Status == CoupleStatus.Ended &&
            c.Members.Any(m => m.UserId == me), ct);
        return couple is null ? null : (couple, me);
    }

    private static MemoryDto ToDto(Memory m, Couple couple, Guid me, PhotoUrls urls, TimeProvider clock)
    {
        var photos = m.AuthorId == me || clock.GetUtcNow() < (couple.EndedAt ?? couple.StartedAt) + PartnerPhotosFor;
        return MemoryEndpoints.ToDto(m, me, urls, photos);
    }

    private static IQueryable<Memory> Live(AiluDb db, Guid coupleId) =>
        db.Memories.Include(m => m.Photos).Include(m => m.Reactions).Include(m => m.Author).AsSplitQuery()
            .Where(m => m.CoupleId == coupleId && m.DeletedAt == null);

    private static IResult NotFound() => Errors.NotFound("archive_not_found", "Такого архива нет.");

    private static IResult MemoryNotFound() => Errors.NotFound("memory_not_found", "Этого воспоминания больше нет.");
}
