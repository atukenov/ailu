using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Features.Goals;

public static class GoalState
{
    public const string Proposed = "proposed";
    public const string JoinNeeded = "join_needed";
    public const string Active = "active";
    public const string Done = "done";
}

public static class StepState
{
    public const string Open = "open";
    public const string WaitingForPartner = "waiting_for_partner";
    public const string ConfirmNeeded = "confirm_needed";
    public const string Done = "done";
}

// TargetMonth is "yyyy-MM". On PATCH a missing field keeps its value and an empty TargetMonth clears it.
public sealed record GoalRequest(string? Title, string? Category, string? TargetMonth, bool? WeeklyReminder, List<string>? Steps);
public sealed record StepRequest(string? Title);
public sealed record StepDto(Guid Id, string Title, string State, DateTimeOffset? ClosedAt);
public sealed record GoalDto(Guid Id, string Title, string Category, string? TargetMonth, bool WeeklyReminder, string State,
    int Progress, int StepsDone, int StepsTotal, bool CreatedByMe, bool RemovalRequestedByMe, bool RemovalRequestedByPartner,
    DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, IReadOnlyList<StepDto> Steps);
public sealed record GoalList(IReadOnlyList<GoalDto> Items, int? Limit, int Unfinished, bool CanCreate);

public static class GoalEndpoints
{
    public const int MaxSteps = 20;

    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/couple").WithTags("Goals").RequireAuthorization();
        g.MapGet("/goals", List);
        g.MapPost("/goals", Create);
        g.MapGet("/goals/{id:guid}", Get);
        g.MapPatch("/goals/{id:guid}", Update);
        g.MapDelete("/goals/{id:guid}", Remove);
        g.MapPost("/goals/{id:guid}/keep", Keep);
        g.MapPost("/goals/{id:guid}/join", Join);
        g.MapPost("/goals/{id:guid}/steps", AddStep);
        g.MapPatch("/goals/{id:guid}/steps/{stepId:guid}", RenameStep);
        g.MapDelete("/goals/{id:guid}/steps/{stepId:guid}", DeleteStep);
        g.MapPost("/goals/{id:guid}/steps/{stepId:guid}/done", MarkStep);
        g.MapDelete("/goals/{id:guid}/steps/{stepId:guid}/done", UnmarkStep);
    }

    // Goals in progress first, oldest first, then finished ones, latest first.
    private static async Task<IResult> List(HttpContext http, AiluDb db, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();

        var goals = await Live(db, scope).ToListAsync(ct);
        var ordered = goals.Where(x => x.CompletedAt is null).OrderBy(x => x.CreatedAt)
            .Concat(goals.Where(x => x.CompletedAt is not null).OrderByDescending(x => x.CompletedAt))
            .Select(x => ToDto(x, scope)).ToList();
        var unfinished = goals.Count(x => x.CompletedAt is null);
        var limit = Plans.GoalLimit(scope.Couple.Plan);
        return Results.Ok(new GoalList(ordered, limit, unfinished, limit is null || unfinished < limit));
    }

    private static async Task<IResult> Get(Guid id, HttpContext http, AiluDb db, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var goal = await Live(db, scope).SingleOrDefaultAsync(x => x.Id == id, ct);
        return goal is null ? NotFound() : Results.Ok(ToDto(goal, scope));
    }

    private static async Task<IResult> Create(GoalRequest body, HttpContext http, AiluDb db, ActivityService activity,
        TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();

        var now = clock.GetUtcNow();
        var today = CoupleCalendar.Today(scope.Couple, clock);
        var goal = new Goal
        {
            Id = Guid.NewGuid(), CoupleId = scope.Couple.Id, CreatedById = scope.UserId, Title = "",
            WeeklyReminder = body.WeeklyReminder ?? true, CreatedAt = now
        };
        if (Apply(body with { Category = body.Category ?? "", TargetMonth = body.TargetMonth ?? "" }, goal, today) is { } invalid)
            return invalid;

        var steps = (body.Steps ?? []).Select(s => s?.Trim() ?? "").Where(s => s.Length > 0).ToList();
        if (steps.Count > MaxSteps) return Errors.Validation("steps", $"В цели до {MaxSteps} этапов.");
        if (steps.Any(s => s.Length > 120)) return Errors.Validation("steps", "Этап до 120 символов.");
        goal.Steps = steps.Select((s, i) => new GoalStep
        {
            Id = Guid.NewGuid(), GoalId = goal.Id, CoupleId = goal.CoupleId, Position = i, Title = s, CreatedAt = now
        }).ToList();

        // The couple row is locked while counting, so two goals created at once can't both take the last slot.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Couples\" WHERE \"Id\" = {scope.Couple.Id} FOR UPDATE", ct);
        if (Plans.GoalLimit(scope.Couple.Plan) is int limit &&
            await db.Goals.CountAsync(x => x.CoupleId == scope.Couple.Id && x.RemovedAt == null && x.CompletedAt == null, ct) >= limit)
            return Errors.Conflict("goal_limit",
                $"В бесплатной версии можно вести {limit} цели одновременно. Завершите одну или откройте Plus, чтобы ставить больше.");

        db.Goals.Add(goal);
        await activity.RecordAsync(scope.Couple, scope.UserId, today, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Created($"/v1/couple/goals/{goal.Id}", ToDto(goal, scope));
    }

    // A shared goal: either partner can change its title, category, deadline and reminder.
    private static async Task<IResult> Update(Guid id, GoalRequest body, HttpContext http, AiluDb db, TimeProvider clock,
        CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var goal = await Live(db, scope).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (goal is null) return NotFound();

        if (Apply(body, goal, CoupleCalendar.Today(scope.Couple, clock)) is { } invalid) return invalid;
        if (body.WeeklyReminder is bool remind) goal.WeeklyReminder = remind;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(goal, scope));
    }

    private static async Task<IResult> Join(Guid id, HttpContext http, AiluDb db, ActivityService activity, TimeProvider clock,
        CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var goal = await Live(db, scope).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (goal is null) return NotFound();
        if (goal.CreatedById == scope.UserId && goal.JoinedAt is null)
            return Errors.Conflict("join_by_partner", "Цель станет общей, когда к ней присоединится партнёр.");

        if (goal.JoinedAt is null)
        {
            goal.JoinedAt = clock.GetUtcNow();
            await activity.RecordAsync(scope.Couple, scope.UserId, CoupleCalendar.Today(scope.Couple, clock), ct);
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(ToDto(goal, scope));
    }

    // Nobody removes a shared goal alone (gamification doc). The first request asks the partner; the
    // partner's own request confirms it. A proposal nobody joined yet can be withdrawn or declined at once.
    private static async Task<IResult> Remove(Guid id, HttpContext http, AiluDb db, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var goal = await Live(db, scope).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (goal is null) return NotFound();

        var now = clock.GetUtcNow();
        if (goal.JoinedAt is not null && (goal.RemovalRequestedById is null || goal.RemovalRequestedById == scope.UserId))
        {
            goal.RemovalRequestedById = scope.UserId;
            goal.RemovalRequestedAt ??= now;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(goal, scope));
        }

        goal.RemovedAt = now;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // Withdraws a removal request, or turns the partner's request down.
    private static async Task<IResult> Keep(Guid id, HttpContext http, AiluDb db, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var goal = await Live(db, scope).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (goal is null) return NotFound();

        goal.RemovalRequestedById = null;
        goal.RemovalRequestedAt = null;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(goal, scope));
    }

    private static async Task<IResult> AddStep(Guid id, StepRequest body, HttpContext http, AiluDb db, TimeProvider clock,
        CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var goal = await Live(db, scope).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (goal is null) return NotFound();
        if (StepTitle(body) is not { } title) return InvalidStep();
        if (goal.Steps.Count >= MaxSteps) return Errors.Conflict("too_many_steps", $"В цели до {MaxSteps} этапов.");

        db.GoalSteps.Add(new GoalStep
        {
            Id = Guid.NewGuid(), GoalId = goal.Id, CoupleId = goal.CoupleId, Title = title, CreatedAt = clock.GetUtcNow(),
            Position = goal.Steps.Count == 0 ? 0 : goal.Steps.Max(s => s.Position) + 1
        }); // fix-up adds it to goal.Steps too
        goal.CompletedAt = null;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(goal, scope));
    }

    private static async Task<IResult> RenameStep(Guid id, Guid stepId, StepRequest body, HttpContext http, AiluDb db,
        CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var goal = await Live(db, scope).SingleOrDefaultAsync(x => x.Id == id, ct);
        var step = goal?.Steps.SingleOrDefault(s => s.Id == stepId);
        if (goal is null || step is null) return NotFound();
        if (step.ClosedAt is not null) return StepClosed();
        if (StepTitle(body) is not { } title) return InvalidStep();

        step.Title = title;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(goal, scope));
    }

    private static async Task<IResult> DeleteStep(Guid id, Guid stepId, HttpContext http, AiluDb db, TimeProvider clock,
        CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var goal = await Live(db, scope).SingleOrDefaultAsync(x => x.Id == id, ct);
        var step = goal?.Steps.SingleOrDefault(s => s.Id == stepId);
        if (goal is null || step is null) return NotFound();
        // A closed step earned нұр and stays part of the goal's story.
        if (step.ClosedAt is not null) return StepClosed();

        db.GoalSteps.Remove(step);
        goal.Steps.Remove(step);
        CompleteIfDone(goal, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(goal, scope));
    }

    // Either partner marks a step done and the other confirms; the couple gets +100 нұр on confirmation.
    private static async Task<IResult> MarkStep(Guid id, Guid stepId, HttpContext http, AiluDb db, NurLedgerService nur,
        ActivityService activity, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var goal = await Live(db, scope).SingleOrDefaultAsync(x => x.Id == id, ct);
        var step = goal?.Steps.SingleOrDefault(s => s.Id == stepId);
        if (goal is null || step is null) return NotFound();
        if (goal.JoinedAt is null)
            return Errors.Conflict("goal_not_joined", "Этапы отмечаются, когда цель общая: сначала партнёр должен присоединиться.");

        var now = clock.GetUtcNow();
        var today = CoupleCalendar.Today(scope.Couple, clock);
        if (step.MarkedAt is null)
        {
            step.MarkedById = scope.UserId;
            step.MarkedAt = now;
        }
        else if (step.ClosedAt is null && step.MarkedById != scope.UserId)
        {
            step.ClosedAt = now;
            await nur.AwardAsync(scope.Couple, NurSource.GoalStep, step.Id.ToString("N"), Nur.GoalStepClosed, today, ct);
            CompleteIfDone(goal, now);
        }

        await activity.RecordAsync(scope.Couple, scope.UserId, today, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(goal, scope));
    }

    // Takes back a mark the partner hasn't confirmed yet.
    private static async Task<IResult> UnmarkStep(Guid id, Guid stepId, HttpContext http, AiluDb db, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var goal = await Live(db, scope).SingleOrDefaultAsync(x => x.Id == id, ct);
        var step = goal?.Steps.SingleOrDefault(s => s.Id == stepId);
        if (goal is null || step is null) return NotFound();
        if (step.ClosedAt is not null) return StepClosed();

        if (step.MarkedById == scope.UserId)
        {
            step.MarkedById = null;
            step.MarkedAt = null;
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(ToDto(goal, scope));
    }

    private static void CompleteIfDone(Goal goal, DateTimeOffset now)
    {
        if (goal.Steps.Count > 0 && goal.Steps.All(s => s.ClosedAt is not null)) goal.CompletedAt ??= now;
    }

    public static int Progress(Goal goal) =>
        goal.Steps.Count == 0 ? 0 : goal.Steps.Count(s => s.ClosedAt is not null) * 100 / goal.Steps.Count;

    private static IQueryable<Goal> Live(AiluDb db, CoupleScope scope) =>
        db.Goals.Include(x => x.Steps).Where(x => x.CoupleId == scope.Couple.Id && x.RemovedAt == null);

    // Applies the fields that are present. Category and TargetMonth arrive as strings so a bad value is a
    // readable validation error rather than a failed bind.
    private static IResult? Apply(GoalRequest body, Goal goal, DateOnly today)
    {
        if (body.Title is not null || goal.Title.Length == 0)
        {
            var title = body.Title?.Trim() ?? "";
            if (title.Length is 0 or > 80) return Errors.Validation("title", "Название цели от 1 до 80 символов.");
            goal.Title = title;
        }

        if (body.Category is not null)
        {
            if (body.Category.Length == 0) goal.Category = GoalCategory.Together;
            else if (Enum.TryParse<GoalCategory>(body.Category, ignoreCase: true, out var category) &&
                     Enum.IsDefined(category) && !int.TryParse(body.Category, out _))
                goal.Category = category;
            else return Errors.Validation("category", "Такой категории нет.");
        }

        if (body.TargetMonth is not null)
        {
            if (body.TargetMonth.Length == 0) goal.TargetMonth = null;
            else if (DateOnly.TryParseExact(body.TargetMonth + "-01", "yyyy-MM-dd", out var month) &&
                     month >= new DateOnly(today.Year, today.Month, 1) && month.Year <= today.Year + 50)
                goal.TargetMonth = month;
            else return Errors.Validation("targetMonth", "Срок — месяц не раньше текущего.");
        }
        return null;
    }

    private static string? StepTitle(StepRequest body)
    {
        var title = body.Title?.Trim() ?? "";
        return title.Length is 0 or > 120 ? null : title;
    }

    private static GoalDto ToDto(Goal g, CoupleScope scope)
    {
        var state =
            g.JoinedAt is null ? (g.CreatedById == scope.UserId ? GoalState.Proposed : GoalState.JoinNeeded) :
            g.CompletedAt is not null ? GoalState.Done :
            GoalState.Active;
        var steps = g.Steps.OrderBy(s => s.Position).Select(s => new StepDto(s.Id, s.Title,
            s.ClosedAt is not null ? StepState.Done :
            s.MarkedAt is null ? StepState.Open :
            s.MarkedById == scope.UserId ? StepState.WaitingForPartner : StepState.ConfirmNeeded,
            s.ClosedAt)).ToList();

        return new GoalDto(g.Id, g.Title, g.Category.ToString().ToLowerInvariant(), g.TargetMonth?.ToString("yyyy-MM"),
            g.WeeklyReminder, state, Progress(g), steps.Count(s => s.State == StepState.Done), steps.Count,
            g.CreatedById == scope.UserId, g.RemovalRequestedById == scope.UserId,
            g.RemovalRequestedById is not null && g.RemovalRequestedById != scope.UserId, g.CreatedAt, g.CompletedAt, steps);
    }

    private static IResult NotFound() => Errors.NotFound("goal_not_found", "Этой цели больше нет.");
    private static IResult InvalidStep() => Errors.Validation("title", "Этап от 1 до 120 символов.");
    private static IResult StepClosed() => Errors.Conflict("step_closed", "Этот этап вы уже закрыли вдвоём, его не изменить.");
}
