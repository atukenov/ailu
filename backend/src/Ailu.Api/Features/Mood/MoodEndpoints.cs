using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ailu.Api.Features.Mood;

public sealed record MoodRequest(string? Mood, string? Note);
public sealed record MyMoodDto(string Mood, string? Note, bool NoteShared, DateTimeOffset MarkedAt);

// The partner gets the mood and when it was marked. The note is only present once its author has
// shared it: what reaches the device is what the partner could read.
public sealed record PartnerMoodDto(string Mood, DateTimeOffset MarkedAt, string? Note);
public sealed record MoodDayDto(DateOnly Date, MyMoodDto? Mine, PartnerMoodDto? Partner);

// Days runs oldest first and ends with today.
public sealed record MoodDto(DateOnly Date, bool Consented, IReadOnlyList<MoodDayDto> Days);

public static class MoodEndpoints
{
    public const int MaxNote = 280;
    public const int HistoryDays = 7;

    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/couple/mood").WithTags("Mood").RequireAuthorization();
        g.MapGet("", Get);
        g.MapPut("", Set);
        g.MapPut("/note-shared", (HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct) =>
            ShareNote(true, http, db, clock, ct));
        g.MapDelete("/note-shared", (HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct) =>
            ShareNote(false, http, db, clock, ct));

        // Consent is the person's, not the couple's, so it lives under /v1/me.
        var me = app.MapGroup("/v1/me/mood-consent").WithTags("Mood").RequireAuthorization();
        me.MapPut("", GiveConsent);
        me.MapDelete("", WithdrawConsent);
    }

    private static async Task<IResult> Get(HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        return Results.Ok(await Load(scope, db, clock, ct));
    }

    // One tap sets the mood; it can be changed during the day. A note left out stays as it was,
    // an empty one is removed. Changing the note makes it private again.
    private static async Task<IResult> Set(MoodRequest body, HttpContext http, AiluDb db, ActivityService activity,
        NurLedgerService nur, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        if (scope.Me.User!.MoodConsentAt is null)
            return Errors.Conflict("consent_required", "Чтобы отмечать настроение, нужно ваше согласие.");

        if (!Enum.TryParse<MoodLevel>(body.Mood, ignoreCase: true, out var mood) || !Enum.IsDefined(mood) ||
            int.TryParse(body.Mood, out _))
            return Errors.Validation("mood", "Выберите настроение.");
        var note = body.Note?.Trim();
        if (note?.Length > MaxNote) return Errors.Validation("note", $"Не больше {MaxNote} символов.");

        var today = CoupleCalendar.Today(scope.Couple, clock);
        var now = clock.GetUtcNow();
        var entry = await db.Moods.SingleOrDefaultAsync(
            m => m.CoupleId == scope.Couple.Id && m.Date == today && m.UserId == scope.UserId, ct);
        if (entry is null)
        {
            entry = new MoodEntry { CoupleId = scope.Couple.Id, Date = today, UserId = scope.UserId, CreatedAt = now };
            db.Moods.Add(entry);
        }

        entry.Mood = mood;
        entry.UpdatedAt = now;
        if (note is not null)
        {
            var text = note.Length == 0 ? null : note;
            if (text != entry.Note) entry.NoteSharedAt = null;
            entry.Note = text;
        }

        await activity.RecordAsync(scope.Couple, scope.UserId, today, ct);
        await db.SaveChangesAsync(ct);

        // Checked after our own row is saved, so when both partners mark at the same moment at least one
        // of them sees the other. If both do, the ledger's unique key keeps it to one award.
        var partnerMarked = scope.Partner is not null && await db.Moods.AnyAsync(
            m => m.CoupleId == scope.Couple.Id && m.Date == today && m.UserId == scope.Partner.UserId, ct);
        if (partnerMarked &&
            await nur.AwardAsync(scope.Couple, NurSource.Mood, today.ToString("yyyyMMdd"), Nur.MoodByBoth, today, ct))
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
            }
        }
        return Results.Ok(await Load(scope, db, clock, ct));
    }

    private static async Task<IResult> ShareNote(bool shared, HttpContext http, AiluDb db, TimeProvider clock,
        CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();

        var today = CoupleCalendar.Today(scope.Couple, clock);
        var entry = await db.Moods.SingleOrDefaultAsync(
            m => m.CoupleId == scope.Couple.Id && m.Date == today && m.UserId == scope.UserId, ct);
        if (entry?.Note is null)
            return Errors.NotFound("note_not_found", "Сначала добавьте пару слов к сегодняшнему настроению.");

        entry.NoteSharedAt = shared ? entry.NoteSharedAt ?? clock.GetUtcNow() : null;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await Load(scope, db, clock, ct));
    }

    private static async Task<IResult> GiveConsent(HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(u => u.Id == CurrentUser.Id(http.User), ct);
        user.MoodConsentAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // Withdrawing consent removes every mood the person has marked, monthly counts included.
    private static async Task<IResult> WithdrawConsent(HttpContext http, AiluDb db, CancellationToken ct)
    {
        var userId = CurrentUser.Id(http.User);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Moods.Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);
        await db.MoodMonths.Where(m => m.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(u => u.SetProperty(x => x.MoodConsentAt, (DateTimeOffset?)null), ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<MoodDto> Load(CoupleScope scope, AiluDb db, TimeProvider clock, CancellationToken ct)
    {
        var today = CoupleCalendar.Today(scope.Couple, clock);
        var from = today.AddDays(-(HistoryDays - 1));
        var rows = await db.Moods.AsNoTracking()
            .Where(m => m.CoupleId == scope.Couple.Id && m.Date >= from && m.Date <= today)
            .ToListAsync(ct);

        var days = Enumerable.Range(0, HistoryDays).Select(i =>
        {
            var day = from.AddDays(i);
            var mine = rows.SingleOrDefault(r => r.Date == day && r.UserId == scope.UserId);
            var theirs = rows.SingleOrDefault(r => r.Date == day && r.UserId != scope.UserId);
            return new MoodDayDto(day,
                mine is null ? null : new MyMoodDto(Name(mine.Mood), mine.Note, mine.NoteSharedAt is not null, mine.UpdatedAt),
                theirs is null ? null : new PartnerMoodDto(Name(theirs.Mood), theirs.UpdatedAt,
                    theirs.NoteSharedAt is not null ? theirs.Note : null));
        }).ToList();

        return new MoodDto(today, scope.Me.User!.MoodConsentAt is not null, days);
    }

    private static string Name(MoodLevel mood) => mood.ToString().ToLowerInvariant();
}
