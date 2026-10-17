using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Features.Achievements;

public static class AchievementState
{
    // Needs a bigger plan before it can be marked.
    public const string Locked = "locked";
    public const string Open = "open";
    public const string WaitingForPartner = "waiting_for_partner";
    public const string ConfirmNeeded = "confirm_needed";
    public const string Earned = "earned";
}

// HappenedOn is "yyyy-MM-dd" and defaults to today. A missing MemoryId leaves the link as it is.
public sealed record MarkRequest(string? HappenedOn, Guid? MemoryId);
public sealed record AchievementDto(string Slug, string Category, string Tier, string Icon, string Title, string Body,
    string State, string? HappenedOn, Guid? MemoryId, DateTimeOffset? EarnedAt, int Nur);
public sealed record AchievementList(IReadOnlyList<AchievementDto> Items, int Earned, int Total, string Plan);

public static class AchievementEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/couple").WithTags("Achievements").RequireAuthorization();
        g.MapGet("/achievements", List);
        g.MapGet("/achievements/{slug}", Get);
        g.MapPost("/achievements/{slug}/done", Mark);
        g.MapDelete("/achievements/{slug}/done", Unmark);
    }

    // The whole library in its order, optionally one category. Earned and Total always count all of it.
    private static async Task<IResult> List(string? category, HttpContext http, AiluDb db, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();

        AchievementCategory? only = null;
        if (!string.IsNullOrEmpty(category))
        {
            if (!TryCategory(category, out var parsed)) return Errors.Validation("category", "Такой категории нет.");
            only = parsed;
        }

        var library = await db.Achievements.OrderBy(a => a.Position).ToListAsync(ct);
        var mine = await db.CoupleAchievements.Where(x => x.CoupleId == scope.Couple.Id).ToDictionaryAsync(x => x.AchievementId, ct);
        var items = library.Where(a => only is null || a.Category == only)
            .Select(a => ToDto(a, mine.GetValueOrDefault(a.Id), scope, Locale(scope))).ToList();
        return Results.Ok(new AchievementList(items, mine.Values.Count(x => x.EarnedAt is not null), library.Count,
            PlanName(scope.Couple.Plan)));
    }

    private static async Task<IResult> Get(string slug, HttpContext http, AiluDb db, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var achievement = await db.Achievements.SingleOrDefaultAsync(a => a.Slug == slug, ct);
        if (achievement is null) return NotFound();
        var row = await db.CoupleAchievements.SingleOrDefaultAsync(x => x.CoupleId == scope.Couple.Id && x.AchievementId == achievement.Id, ct);
        return Results.Ok(ToDto(achievement, row, scope, Locale(scope)));
    }

    // One partner says it happened; the other partner's own mark confirms it and the couple gets +80 нұр.
    // Marking again before that only changes the date or the memory; after it, only the memory.
    private static async Task<IResult> Mark(string slug, MarkRequest? body, HttpContext http, AiluDb db, NurLedgerService nur,
        ActivityService activity, DemoCouple demo, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var achievement = await db.Achievements.SingleOrDefaultAsync(a => a.Slug == slug, ct);
        if (achievement is null) return NotFound();

        var now = clock.GetUtcNow();
        var today = CoupleCalendar.Today(scope.Couple, clock);
        DateOnly? happenedOn = null;
        if (!string.IsNullOrEmpty(body?.HappenedOn))
        {
            if (!DateOnly.TryParseExact(body.HappenedOn, "yyyy-MM-dd", out var day) || day > today || day.Year < 1950)
                return Errors.Validation("happenedOn", "Дата — день не позже сегодняшнего.");
            happenedOn = day;
        }
        if (body?.MemoryId is Guid memoryId &&
            !await db.Memories.AnyAsync(m => m.Id == memoryId && m.CoupleId == scope.Couple.Id && m.DeletedAt == null, ct))
            return Errors.Validation("memoryId", "Этого воспоминания больше нет.");

        var row = await db.CoupleAchievements.SingleOrDefaultAsync(x => x.CoupleId == scope.Couple.Id && x.AchievementId == achievement.Id, ct);
        if (row?.EarnedAt is not null)
        {
            // An earned achievement keeps its date, but a memory of it can still be added.
            if (body?.MemoryId is not null)
            {
                row.MemoryId = body.MemoryId;
                await db.SaveChangesAsync(ct);
            }
            return Results.Ok(ToDto(achievement, row, scope, Locale(scope)));
        }

        if (row is null)
        {
            // A plan that lapsed later doesn't take back what was marked under it: only a new mark checks the plan.
            if (scope.Couple.Plan < achievement.Tier)
                return Errors.Forbidden("achievement_locked", "Это достижение открывается с подпиской Plus.");
            row = new CoupleAchievement
            {
                CoupleId = scope.Couple.Id, AchievementId = achievement.Id, MarkedById = scope.UserId, MarkedAt = now,
                HappenedOn = happenedOn ?? today, MemoryId = body?.MemoryId
            };
            db.CoupleAchievements.Add(row);
        }
        else
        {
            if (happenedOn is not null) row.HappenedOn = happenedOn.Value;
            if (body?.MemoryId is not null) row.MemoryId = body.MemoryId;
            if (row.MarkedById != scope.UserId)
            {
                row.EarnedAt = now;
                await nur.AwardAsync(scope.Couple, NurSource.Achievement, achievement.Slug, Nur.AchievementEarned, today, ct);
            }
        }

        await activity.RecordAsync(scope.Couple, scope.UserId, today, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (row.EarnedAt is null)
        {
            // Both partners marked it at the same moment: one insert won, and that partner now waits for the other.
            return Errors.Conflict("achievement_marked", "Партнёр только что отметил это достижение. Подтвердите его.");
        }
        // The store reviewer's partner confirms at once, so the whole flow can be seen alone.
        if (scope.Couple.IsDemo && row.EarnedAt is null) await demo.PlayPartnerAsync(scope, ct);
        return Results.Ok(ToDto(achievement, row, scope, Locale(scope)));
    }

    // Takes back a mark, or turns the partner's mark down. Nothing undoes an earned achievement.
    private static async Task<IResult> Unmark(string slug, HttpContext http, AiluDb db, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var achievement = await db.Achievements.SingleOrDefaultAsync(a => a.Slug == slug, ct);
        if (achievement is null) return NotFound();

        var row = await db.CoupleAchievements.SingleOrDefaultAsync(x => x.CoupleId == scope.Couple.Id && x.AchievementId == achievement.Id, ct);
        if (row?.EarnedAt is not null)
            return Errors.Conflict("achievement_earned", "Это достижение уже ваше, его не отменить.");
        if (row is not null)
        {
            db.CoupleAchievements.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(ToDto(achievement, null, scope, Locale(scope)));
    }

    private static AchievementDto ToDto(Achievement a, CoupleAchievement? row, CoupleScope scope, string? locale)
    {
        var state =
            row?.EarnedAt is not null ? AchievementState.Earned :
            row is not null ? (row.MarkedById == scope.UserId ? AchievementState.WaitingForPartner : AchievementState.ConfirmNeeded) :
            scope.Couple.Plan < a.Tier ? AchievementState.Locked :
            AchievementState.Open;
        return new AchievementDto(a.Slug, a.Category.ToString().ToLowerInvariant(), PlanName(a.Tier), a.Icon,
            a.TitleIn(locale), a.BodyIn(locale), state, row?.HappenedOn.ToString("yyyy-MM-dd"),
            row?.MemoryId, row?.EarnedAt, Nur.AchievementEarned);
    }

    // Library texts follow the caller's language; anything but Kazakh reads Russian until English exists.
    private static string? Locale(CoupleScope scope) => scope.Me.User?.Locale;

    private static string PlanName(CouplePlan plan) => plan.ToString().ToLowerInvariant();

    private static bool TryCategory(string value, out AchievementCategory category) =>
        Enum.TryParse(value, ignoreCase: true, out category) && Enum.IsDefined(category) && !int.TryParse(value, out _);

    private static IResult NotFound() => Errors.NotFound("achievement_not_found", "Такого достижения нет.");
}
