using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Features.Memories;

public sealed record MemoryRequest(string? Title, string? Text, string? Place, DateOnly? HappenedOn, Guid? CoupleTaskId);
public sealed record PhotoDto(Guid Id, int Width, int Height, string Thumb, string Screen, string Full);
public sealed record AuthorDto(Guid Id, string? Name);
public sealed record MemoryDto(Guid Id, string Title, string? Text, string? Place, DateOnly HappenedOn, AuthorDto Author,
    bool Mine, bool Favorite, bool ReactedByMe, bool ReactedByPartner, Guid? CoupleTaskId, DateTimeOffset CreatedAt,
    IReadOnlyList<PhotoDto> Photos);
public sealed record MemoryPage(IReadOnlyList<MemoryDto> Items, string? NextCursor);
public sealed record StorageDto(string Plan, int Photos, int? PhotoLimit, long UsedBytes, long QuotaBytes);

public static class MemoryEndpoints
{
    public const int MaxPhotos = 10;
    public const long MaxUploadBytes = 20 * 1024 * 1024;
    private const int PageSize = 20;

    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/couple").WithTags("Memories").RequireAuthorization();
        g.MapGet("/memories", List);
        g.MapPost("/memories", Create);
        g.MapGet("/memories/{id:guid}", Get);
        g.MapPatch("/memories/{id:guid}", Update);
        g.MapDelete("/memories/{id:guid}", Delete);
        g.MapPost("/memories/{id:guid}/photos", AddPhoto).DisableAntiforgery();
        g.MapPut("/memories/{id:guid}/reaction", React);
        g.MapDelete("/memories/{id:guid}/reaction", Unreact);
        g.MapPut("/memories/{id:guid}/favorite", (Guid id, HttpContext http, AiluDb db, PhotoUrls urls, CancellationToken ct) =>
            SetFavorite(id, true, http, db, urls, ct));
        g.MapDelete("/memories/{id:guid}/favorite", (Guid id, HttpContext http, AiluDb db, PhotoUrls urls, CancellationToken ct) =>
            SetFavorite(id, false, http, db, urls, ct));
        g.MapGet("/storage", Storage);

        // Anonymous: the signature in the URL is the permission (see PhotoUrls).
        app.MapGet("/v1/photos/{id:guid}/{size:int}", Photo).WithTags("Memories");
    }

    // Newest first by the day it happened, so a memory added today about last summer sits with last summer.
    private static async Task<IResult> List(string? cursor, HttpContext http, AiluDb db, PhotoUrls urls, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();

        var q = Live(db, scope);
        if (cursor is not null)
        {
            if (Cursor.Parse(cursor) is not var (day, created, id)) return Errors.Validation("cursor", "Неверный курсор.");
            q = q.Where(m => m.HappenedOn < day ||
                             (m.HappenedOn == day && (m.CreatedAt < created || (m.CreatedAt == created && m.Id.CompareTo(id) < 0))));
        }

        var page = await q.OrderByDescending(m => m.HappenedOn).ThenByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
            .Take(PageSize + 1).ToListAsync(ct);
        var next = page.Count > PageSize ? Cursor.Of(page[PageSize - 1]) : null;
        return Results.Ok(new MemoryPage(page.Take(PageSize).Select(m => ToDto(m, scope, urls)).ToList(), next));
    }

    private static async Task<IResult> Get(Guid id, HttpContext http, AiluDb db, PhotoUrls urls, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var memory = await Live(db, scope).SingleOrDefaultAsync(m => m.Id == id, ct);
        return memory is null ? NotFound() : Results.Ok(ToDto(memory, scope, urls));
    }

    private static async Task<IResult> Create(MemoryRequest body, HttpContext http, AiluDb db, ActivityService activity,
        PhotoUrls urls, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();

        var today = CoupleCalendar.Today(scope.Couple, clock);
        var memory = new Memory
        {
            Id = Guid.NewGuid(), CoupleId = scope.Couple.Id, AuthorId = scope.UserId, Title = "",
            HappenedOn = today, CreatedAt = clock.GetUtcNow()
        };
        if (Apply(body, memory, today) is { } invalid) return invalid;

        if (body.CoupleTaskId is Guid taskId)
        {
            if (!await db.CoupleTasks.AnyAsync(t => t.Id == taskId && t.CoupleId == scope.Couple.Id, ct))
                return Errors.Validation("coupleTaskId", "Такого задания у вас нет.");
            memory.CoupleTaskId = taskId;
        }

        db.Memories.Add(memory);
        await activity.RecordAsync(scope.Couple, scope.UserId, today, ct);
        await db.SaveChangesAsync(ct);
        memory.Author = scope.Me.User;
        return Results.Created($"/v1/couple/memories/{memory.Id}", ToDto(memory, scope, urls));
    }

    private static async Task<IResult> Update(Guid id, MemoryRequest body, HttpContext http, AiluDb db, PhotoUrls urls,
        TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var memory = await Live(db, scope).SingleOrDefaultAsync(m => m.Id == id, ct);
        if (memory is null) return NotFound();
        if (memory.AuthorId != scope.UserId) return NotAuthor();

        var merged = new MemoryRequest(body.Title ?? memory.Title, body.Text ?? memory.Text, body.Place ?? memory.Place,
            body.HappenedOn ?? memory.HappenedOn, null);
        if (Apply(merged, memory, CoupleCalendar.Today(scope.Couple, clock)) is { } invalid) return invalid;

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(memory, scope, urls));
    }

    // Only the author can delete (privacy doc: deleting is the author's right, seeing is both partners').
    // The quota is freed now; the purge job removes the files after 30 days.
    private static async Task<IResult> Delete(Guid id, HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var memory = await Live(db, scope).SingleOrDefaultAsync(m => m.Id == id, ct);
        if (memory is null) return NotFound();
        if (memory.AuthorId != scope.UserId) return NotAuthor();

        var bytes = memory.Photos.Sum(p => p.Bytes);
        var count = memory.Photos.Count;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        memory.DeletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await db.Couples.Where(c => c.Id == scope.Couple.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.StorageBytes, c => c.StorageBytes - bytes)
                .SetProperty(c => c.PhotoCount, c => c.PhotoCount - count), ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AddPhoto(Guid id, IFormFile? file, HttpContext http, AiluDb db, IPhotoStore store,
        PhotoProcessor processor, PhotoUrls urls, TimeProvider clock, ILogger<Memory> log, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var memory = await Live(db, scope).SingleOrDefaultAsync(m => m.Id == id, ct);
        if (memory is null) return NotFound();
        if (memory.AuthorId != scope.UserId) return NotAuthor();
        if (memory.Photos.Count >= MaxPhotos)
            return Errors.Conflict("too_many_photos", $"В одном воспоминании до {MaxPhotos} фотографий.");

        if (file is null || file.Length == 0) return Errors.Validation("file", "Прикрепите фотографию.");
        if (file.Length > MaxUploadBytes) return Errors.Validation("file", "Фотография слишком большая.");

        await using var input = file.OpenReadStream();
        var photo = await processor.NormalizeAsync(input, ct);
        if (photo is null) return Errors.Validation("file", "Это не похоже на фотографию. Подойдут JPEG, PNG и WebP.");

        var quota = Plans.QuotaBytes(scope.Couple.Plan);
        var limit = Plans.PhotoLimit(scope.Couple.Plan);
        var bytes = (long)photo.Jpeg.Length;
        var row = new MemoryPhoto
        {
            Id = Guid.NewGuid(), MemoryId = memory.Id, CoupleId = scope.Couple.Id,
            Position = memory.Photos.Count == 0 ? 0 : memory.Photos.Max(p => p.Position) + 1,
            Bytes = bytes, Width = photo.Width, Height = photo.Height, CreatedAt = clock.GetUtcNow()
        };

        // The file goes in first: a crash after this leaves an orphan file, never a row without a file.
        await store.PutAsync(PhotoKeys.File(row.CoupleId, row.Id, PhotoProcessor.Full), photo.Jpeg, StorageTier.Standard, ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Check and count in one statement, so two uploads at once can't both squeeze under the quota.
            var counted = await db.Couples
                .Where(c => c.Id == scope.Couple.Id && c.PhotoCount < limit && c.StorageBytes + bytes <= quota)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.StorageBytes, c => c.StorageBytes + bytes)
                    .SetProperty(c => c.PhotoCount, c => c.PhotoCount + 1), ct);
            if (counted == 0)
            {
                await store.DeleteFolderAsync(PhotoKeys.Folder(row.CoupleId, row.Id), ct);
                return Errors.Conflict("storage_full", scope.Couple.Plan switch
                {
                    CouplePlan.Free => $"В бесплатной версии можно хранить {limit} фотографий, и все они заняты.",
                    var plan => $"В {plan} можно хранить {limit} фотографий, и все они заняты."
                });
            }
            db.MemoryPhotos.Add(row); // fix-up adds it to memory.Photos too
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Saving photo {Photo} failed; removing its file", row.Id);
            await store.DeleteFolderAsync(PhotoKeys.Folder(row.CoupleId, row.Id), CancellationToken.None);
            throw;
        }

        return Results.Ok(ToDto(memory, scope, urls));
    }

    // A heart from the partner who didn't write the memory earns +20 нұр, once per memory and at most
    // three times a day (gamification doc). The author's own heart counts for nothing.
    private static async Task<IResult> React(Guid id, HttpContext http, AiluDb db, NurLedgerService nur,
        ActivityService activity, PhotoUrls urls, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var memory = await Live(db, scope).SingleOrDefaultAsync(m => m.Id == id, ct);
        if (memory is null) return NotFound();

        if (memory.Reactions.All(r => r.UserId != scope.UserId))
        {
            db.MemoryReactions.Add(new MemoryReaction { MemoryId = memory.Id, UserId = scope.UserId, CreatedAt = clock.GetUtcNow() });

            var today = CoupleCalendar.Today(scope.Couple, clock);
            if (memory.AuthorId != scope.UserId)
            {
                var sourceId = memory.Id.ToString("N");
                var todayKeys = $"{scope.Couple.Id:N}:{NurSource.Memory}:%:{today:yyyy-MM-dd}";
                var alreadyPaid = await db.NurLedger.AnyAsync(e =>
                    e.CoupleId == scope.Couple.Id && e.SourceType == NurSource.Memory && e.SourceId == sourceId, ct);
                var paidToday = await db.NurLedger.CountAsync(e => EF.Functions.Like(e.IdempotencyKey, todayKeys), ct);
                if (!alreadyPaid && paidToday < Nur.MemoryAwardsPerDay)
                    await nur.AwardAsync(scope.Couple, NurSource.Memory, sourceId, Nur.MemoryAdded, today, ct);
            }
            await activity.RecordAsync(scope.Couple, scope.UserId, today, ct);
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(ToDto(memory, scope, urls));
    }

    private static async Task<IResult> Unreact(Guid id, HttpContext http, AiluDb db, PhotoUrls urls, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var memory = await Live(db, scope).SingleOrDefaultAsync(m => m.Id == id, ct);
        if (memory is null) return NotFound();

        memory.Reactions.RemoveAll(r => r.UserId == scope.UserId);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(memory, scope, urls));
    }

    private static async Task<IResult> SetFavorite(Guid id, bool favorite, HttpContext http, AiluDb db, PhotoUrls urls,
        CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var memory = await Live(db, scope).SingleOrDefaultAsync(m => m.Id == id, ct);
        if (memory is null) return NotFound();

        memory.Favorite = favorite;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(memory, scope, urls));
    }

    private static async Task<IResult> Storage(HttpContext http, AiluDb db, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var c = scope.Couple;
        await db.Entry(c).ReloadAsync(ct);
        return Results.Ok(new StorageDto(c.Plan.ToString().ToLowerInvariant(), c.PhotoCount, Plans.PhotoLimit(c.Plan),
            c.StorageBytes, Plans.QuotaBytes(c.Plan)));
    }

    // Smaller sizes are made on first request and kept (roadmap: three sizes, generated lazily).
    private static async Task<IResult> Photo(Guid id, int size, long exp, string? sig, HttpContext http, AiluDb db,
        IPhotoStore store, PhotoProcessor processor, PhotoUrls urls, CancellationToken ct)
    {
        if (!PhotoProcessor.Sizes.Contains(size) || !urls.IsValid(id, size, exp, sig)) return Results.NotFound();

        var photo = await db.MemoryPhotos.AsNoTracking()
            .Where(p => p.Id == id && db.Memories.Any(m => m.Id == p.MemoryId && m.DeletedAt == null))
            .SingleOrDefaultAsync(ct);
        if (photo is null) return Results.NotFound();

        var stream = await store.OpenAsync(PhotoKeys.File(photo.CoupleId, photo.Id, size), ct);
        if (stream is null)
        {
            await using var full = await store.OpenAsync(PhotoKeys.File(photo.CoupleId, photo.Id, PhotoProcessor.Full), ct);
            if (full is null) return Results.NotFound();
            var resized = await processor.ResizeAsync(full, size, ct);
            // An archived photo's screen size is made in the archive too; thumbnails always stay standard,
            // because the memory list shows them and they are a few kilobytes each.
            var tier = photo.ArchivedAt is not null && size != PhotoProcessor.Thumb ? StorageTier.Archive : StorageTier.Standard;
            await store.PutAsync(PhotoKeys.File(photo.CoupleId, photo.Id, size), resized, tier, ct);
            stream = new MemoryStream(resized);
        }

        http.Response.Headers.CacheControl = "private, max-age=3600";
        return Results.Stream(stream, "image/jpeg");
    }

    private static IQueryable<Memory> Live(AiluDb db, CoupleScope scope) =>
        db.Memories.Include(m => m.Photos).Include(m => m.Reactions).Include(m => m.Author).AsSplitQuery()
            .Where(m => m.CoupleId == scope.Couple.Id && m.DeletedAt == null);

    private static IResult? Apply(MemoryRequest body, Memory memory, DateOnly today)
    {
        var title = body.Title?.Trim() ?? "";
        if (title.Length is 0 or > 120) return Errors.Validation("title", "Название от 1 до 120 символов.");
        var text = string.IsNullOrWhiteSpace(body.Text) ? null : body.Text.Trim();
        if (text?.Length > 2000) return Errors.Validation("text", "Текст до 2000 символов.");
        var place = string.IsNullOrWhiteSpace(body.Place) ? null : body.Place.Trim();
        if (place?.Length > 120) return Errors.Validation("place", "Место до 120 символов.");
        var day = body.HappenedOn ?? memory.HappenedOn;
        if (day > today || day.Year < 1950) return Errors.Validation("happenedOn", "Дата не может быть в будущем.");

        memory.Title = title;
        memory.Text = text;
        memory.Place = place;
        memory.HappenedOn = day;
        return null;
    }

    private static MemoryDto ToDto(Memory m, CoupleScope scope, PhotoUrls urls) => ToDto(m, scope.UserId, urls, withPhotos: true);

    internal static MemoryDto ToDto(Memory m, Guid me, PhotoUrls urls, bool withPhotos) => new(
        m.Id, m.Title, m.Text, m.Place, m.HappenedOn, new AuthorDto(m.AuthorId, m.Author?.Name), m.AuthorId == me,
        m.Favorite, m.Reactions.Any(r => r.UserId == me), m.Reactions.Any(r => r.UserId != me),
        m.CoupleTaskId, m.CreatedAt,
        withPhotos
            ? m.Photos.OrderBy(p => p.Position).Select(p => new PhotoDto(p.Id, p.Width, p.Height,
                urls.For(p.Id, PhotoProcessor.Thumb), urls.For(p.Id, PhotoProcessor.Screen), urls.For(p.Id, PhotoProcessor.Full))).ToList()
            : []);

    private static IResult NotFound() => Errors.NotFound("memory_not_found", "Этого воспоминания больше нет.");

    private static IResult NotAuthor() =>
        Errors.Forbidden("not_author", "Изменить или удалить воспоминание может только тот, кто его добавил.");

    internal static class Cursor
    {
        public static string Of(Memory m) => $"{m.HappenedOn:yyyy-MM-dd}_{m.CreatedAt.UtcTicks}_{m.Id:N}";

        public static (DateOnly, DateTimeOffset, Guid)? Parse(string s)
        {
            var parts = s.Split('_');
            return parts.Length == 3 && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", out var day) &&
                   long.TryParse(parts[1], out var ticks) && ticks is >= 0 and <= 3155378975999999999 &&
                   Guid.TryParseExact(parts[2], "N", out var id)
                ? (day, new DateTimeOffset(ticks, TimeSpan.Zero), id)
                : null;
        }
    }
}
