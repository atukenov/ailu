using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Features.Ai;

// Kind: date, home, film or music. Budget (dates and evenings at home only): any, free or low. Wish: what
// the partner would like ("на воздухе"), sent to the model as is.
public sealed record AiPickRequest(string? Kind, string? Budget, string? Wish);

public sealed record AiItemDto(Guid Id, Guid PickId, string Kind, string Title, string Body, int CostKzt, int DurationMin,
    bool Saved, DateTimeOffset? SavedAt);

public sealed record AiPickDto(Guid Id, string Kind, string Budget, string? Wish, bool ByMe, DateTimeOffset CreatedAt,
    IReadOnlyList<AiItemDto> Items);

// `Available` is whether the plan lets the couple ask for new selections. Selections and saved ideas
// stay visible after the plan ends. `Used` and `Limit` count this calendar month; `ResetsOn` is the
// first day of the next one.
public sealed record AiDto(bool Available, int Used, int Limit, DateOnly ResetsOn, IReadOnlyList<AiPickDto> Picks,
    IReadOnlyList<AiItemDto> Saved);

public static class AiEndpoints
{
    private const int RecentPicks = 20;
    private const int SavedShown = 100;

    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/couple/ai").WithTags("AI").RequireAuthorization();
        g.MapGet("", Get);
        g.MapPost("/picks", Pick);
        g.MapGet("/picks/{id:guid}", GetPick);
        g.MapPut("/items/{id:guid}/saved", (Guid id, HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct) =>
            Save(id, true, http, db, clock, ct));
        g.MapDelete("/items/{id:guid}/saved", (Guid id, HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct) =>
            Save(id, false, http, db, clock, ct));
    }

    private static async Task<IResult> Get(HttpContext http, AiluDb db, AiService ai, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();

        var picks = await db.AiPicks.AsNoTracking().Include(p => p.Items)
            .Where(p => p.CoupleId == scope.Couple.Id && p.Status == AiPickStatus.Ready)
            .OrderByDescending(p => p.CreatedAt).Take(RecentPicks).ToListAsync(ct);
        var saved = await db.AiPickItems.AsNoTracking()
            .Where(i => i.CoupleId == scope.Couple.Id && i.SavedAt != null)
            .OrderByDescending(i => i.SavedAt).Take(SavedShown)
            .Join(db.AiPicks, i => i.PickId, p => p.Id, (i, p) => new { Item = i, p.Kind }).ToListAsync(ct);

        var today = CoupleCalendar.Today(scope.Couple, clock);
        return Results.Ok(new AiDto(
            Plans.AiPicksPerMonth(scope.Couple.Plan) > 0,
            await ai.UsedThisMonthAsync(scope.Couple, ct),
            Plans.AiPicksPerMonth(scope.Couple.Plan),
            AiService.MonthOf(today).AddMonths(1),
            picks.Select(p => ToDto(p, scope)).ToList(),
            saved.Select(x => ToDto(x.Item, x.Kind, scope)).ToList()));
    }

    private static async Task<IResult> Pick(AiPickRequest body, HttpContext http, AiluDb db, AiService ai, ActivityService activity,
        TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        if (!Enum.TryParse<AiPickKind>(body.Kind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind) || int.TryParse(body.Kind, out _))
            return Errors.Validation("kind", "Выберите, что подобрать.");
        var budget = AiBudget.Any;
        if (body.Budget is not null && (!Enum.TryParse(body.Budget, ignoreCase: true, out budget) || !Enum.IsDefined(budget)
                                        || int.TryParse(body.Budget, out _)))
            return Errors.Validation("budget", "Такого бюджета нет.");
        if (kind is AiPickKind.Film or AiPickKind.Music) budget = AiBudget.Any;
        var note = string.IsNullOrWhiteSpace(body.Wish) ? null : body.Wish.Trim();
        if (note?.Length > AiPrompts.NoteLength)
            return Errors.Validation("wish", $"Пожелание до {AiPrompts.NoteLength} символов.");

        var (outcome, pick) = await ai.PickAsync(scope, kind, budget, note, ct);
        switch (outcome)
        {
            case PickOutcome.NoPlan:
                return Errors.Forbidden("ai_plan", "Подборки от AI открыты в Premium.");
            case PickOutcome.QuotaUsed:
                return Errors.Conflict("ai_quota",
                    $"В этом месяце все {Plans.AiPicksPerMonth(scope.Couple.Plan)} подборок уже готовы. Новые появятся 1-го числа.");
            case PickOutcome.Unavailable:
                return Errors.Unavailable("ai_unavailable", "Не получилось составить подборку. Попробуйте чуть позже, попытка не потрачена.");
        }

        await activity.RecordAsync(scope.Couple, scope.UserId, CoupleCalendar.Today(scope.Couple, clock), ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(pick!, scope));
    }

    private static async Task<IResult> GetPick(Guid id, HttpContext http, AiluDb db, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var pick = await db.AiPicks.AsNoTracking().Include(p => p.Items)
            .SingleOrDefaultAsync(p => p.Id == id && p.CoupleId == scope.Couple.Id && p.Status == AiPickStatus.Ready, ct);
        return pick is null ? NotFound() : Results.Ok(ToDto(pick, scope));
    }

    // Either partner saves an idea for both, or takes it back out.
    private static async Task<IResult> Save(Guid id, bool saved, HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var item = await db.AiPickItems.SingleOrDefaultAsync(i => i.Id == id && i.CoupleId == scope.Couple.Id, ct);
        if (item is null) return NotFound();
        var kind = await db.AiPicks.Where(p => p.Id == item.PickId).Select(p => p.Kind).SingleAsync(ct);

        if (saved && item.SavedAt is null)
        {
            item.SavedAt = clock.GetUtcNow();
            item.SavedById = scope.UserId;
        }
        else if (!saved)
        {
            item.SavedAt = null;
            item.SavedById = null;
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(item, kind, scope));
    }

    private static IResult NotFound() => Errors.NotFound("ai_not_found", "Эта подборка не найдена.");

    private static string Code<T>(T value) where T : Enum => value.ToString().ToLowerInvariant();

    private static AiPickDto ToDto(AiPick p, CoupleScope scope) =>
        new(p.Id, Code(p.Kind), Code(p.Budget), p.Note, p.RequestedById == scope.UserId, p.CreatedAt,
            p.Items.OrderBy(i => i.Position).Select(i => ToDto(i, p.Kind, scope)).ToList());

    private static AiItemDto ToDto(AiPickItem i, AiPickKind kind, CoupleScope scope)
    {
        var locale = scope.Me.User?.Locale;
        return new AiItemDto(i.Id, i.PickId, Code(kind), i.TitleIn(locale), i.BodyIn(locale), i.CostKzt, i.DurationMin,
            i.SavedAt is not null, i.SavedAt);
    }
}
