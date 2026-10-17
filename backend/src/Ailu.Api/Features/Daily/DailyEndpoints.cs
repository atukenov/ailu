using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Features.Pairing;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Features.Daily;

public static class TaskState
{
    public const string Offered = "offered";
    public const string WaitingForPartner = "waiting_for_partner";
    public const string Accepted = "accepted";
    public const string WaitingForConfirmation = "waiting_for_confirmation";
    public const string ConfirmNeeded = "confirm_needed";
    public const string Done = "done";
}

public sealed record TaskDto(Guid Id, DateOnly Date, string Slug, string Category, string Title, string Body, int CostKzt,
    int DurationMin, string State, bool AcceptedByMe, bool AcceptedByPartner, DateTimeOffset? CompletedAt, DateTimeOffset? ConfirmedAt);

// The partner's answer is only present once both have answered. Hiding it in the UI would not be
// enough: what reaches the device is what the partner could read.
public sealed record QuestionDto(Guid Id, DateOnly Date, string Text, string? MyAnswer, bool PartnerAnswered,
    string? PartnerAnswer, DateTimeOffset? RevealedAt);

// `Task` is the task of the day; `MoreTasks` are the ones a paid couple asked for after it, oldest
// first. `TasksPerDay` is what the plan allows today, the task of the day included.
public sealed record TodayDto(DateOnly Date, TaskDto Task, IReadOnlyList<TaskDto> MoreTasks, int TasksPerDay,
    QuestionDto Question, StreakDto Streak, PhaseDto Phase);
public sealed record AnswerRequest(string Text);

public static class DailyEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/couple").WithTags("Daily").RequireAuthorization();
        g.MapGet("/today", Today);
        g.MapPost("/tasks/more", MoreTask);
        g.MapPost("/tasks/{id:guid}/accept", AcceptTask);
        g.MapPost("/tasks/{id:guid}/complete", CompleteTask);
        g.MapPost("/questions/{id:guid}/answer", Answer);
        g.MapPost("/streak/restore", RestoreStreak);
    }

    private static async Task<IResult> Today(HttpContext http, AiluDb db, DailyContentService content, StreakService streaks,
        DemoCouple demo, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        if (scope.Couple.IsDemo) await demo.PlayPartnerAsync(scope, ct);

        var today = CoupleCalendar.Today(scope.Couple, clock);
        var (task, question) = await content.EnsureAsync(scope.Couple, today, ct);
        var streak = await streaks.DtoAsync(scope.Couple, today, ct);
        var more = await db.CoupleTasks.Include(t => t.DailyTask)
            .Where(t => t.CoupleId == scope.Couple.Id && t.Date == today && t.Ordinal > 0)
            .OrderBy(t => t.Ordinal).ToListAsync(ct);

        return Results.Ok(new TodayDto(today, ToDto(task, scope), more.Select(t => ToDto(t, scope)).ToList(),
            Plans.TasksPerDay(scope.Couple.Plan), ToDto(question, scope), streak, PairingEndpoints.ToPhase(scope.Couple)));
    }

    // The next task for today, once the previous one is done and the plan has room for it. When both
    // partners ask at once, the second gets the task the first one opened.
    private static async Task<IResult> MoreTask(HttpContext http, AiluDb db, DailyContentService content,
        ActivityService activity, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var today = CoupleCalendar.Today(scope.Couple, clock);
        await content.EnsureAsync(scope.Couple, today, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Couples\" WHERE \"Id\" = {scope.Couple.Id} FOR UPDATE", ct);
        var last = await db.CoupleTasks.Include(t => t.DailyTask)
            .Where(t => t.CoupleId == scope.Couple.Id && t.Date == today)
            .OrderByDescending(t => t.Ordinal).FirstAsync(ct);

        if (last.ConfirmedAt is null)
            return last.Ordinal > 0
                ? Results.Ok(ToDto(last, scope))
                : Errors.Conflict("task_not_done", "Сначала завершите задание дня, потом откроется следующее.");

        var limit = Plans.TasksPerDay(scope.Couple.Plan);
        if (last.Ordinal + 1 >= limit)
            return scope.Couple.Plan == CouplePlan.Free
                ? Errors.Conflict("task_limit", "В бесплатной версии одно задание в день. В Plus их три.")
                : Errors.Conflict("task_limit", $"На сегодня все {limit} задания выполнены. Новое появится завтра.");

        var task = await content.AddExtraAsync(scope.Couple, today, last.Ordinal + 1, ct);
        await activity.RecordAsync(scope.Couple, scope.UserId, today, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(ToDto(task, scope));
    }

    private static async Task<IResult> RestoreStreak(HttpContext http, AiluDb db, StreakService streaks, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        return scope is null ? Errors.NoCouple() : await streaks.RestoreAsync(scope.Couple, ct);
    }

    private static async Task<IResult> AcceptTask(Guid id, HttpContext http, AiluDb db, ActivityService activity,
        TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var task = await FindTodayTask(id, scope, db, clock, ct);
        if (task is null) return TaskNotFound();

        var now = clock.GetUtcNow();
        if (scope.Role == MemberRole.A) task.AcceptedAtA ??= now;
        else task.AcceptedAtB ??= now;

        await activity.RecordAsync(scope.Couple, scope.UserId, task.Date, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(task, scope));
    }

    // Either partner can mark the task done; the other one confirms. Нұр is awarded on confirmation.
    private static async Task<IResult> CompleteTask(Guid id, HttpContext http, AiluDb db, ActivityService activity,
        NurLedgerService nur, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var task = await FindTodayTask(id, scope, db, clock, ct);
        if (task is null) return TaskNotFound();
        if (task.AcceptedAtA is null || task.AcceptedAtB is null)
            return Errors.Conflict("not_accepted", "Задание нужно принять вдвоём, прежде чем отмечать выполнение.");

        var now = clock.GetUtcNow();
        if (task.CompletedAt is null)
        {
            task.CompletedAt = now;
            task.CompletedByUserId = scope.UserId;
        }
        else if (task.ConfirmedAt is null && task.CompletedByUserId != scope.UserId)
        {
            task.ConfirmedAt = now;
            // Нұр for a task is capped at one a day (gamification doc), so paid extras don't buy phases faster.
            if (task.Ordinal == 0)
                await nur.AwardAsync(scope.Couple, NurSource.Task, task.Id.ToString("N"), Nur.TaskCompleted, task.Date, ct);
        }

        await activity.RecordAsync(scope.Couple, scope.UserId, task.Date, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(task, scope));
    }

    private static async Task<IResult> Answer(Guid id, AnswerRequest body, HttpContext http, AiluDb db,
        ActivityService activity, NurLedgerService nur, TimeProvider clock, CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();

        var text = body.Text?.Trim() ?? "";
        if (text.Length is 0 or > 1000) return Errors.Validation("text", "Ответ от 1 до 1000 символов.");

        var today = CoupleCalendar.Today(scope.Couple, clock);
        var q = await db.DailyQuestions.Include(x => x.Question)
            .SingleOrDefaultAsync(x => x.Id == id && x.CoupleId == scope.Couple.Id, ct);
        if (q is null || q.Date != today)
            return Errors.NotFound("question_not_found", "Этот вопрос уже закрыт. Загляните в вопрос на сегодня.");

        // A blind answer stops being blind if it can be edited after seeing the partner's.
        if (q.RevealedAt is not null)
            return Errors.Conflict("answer_locked", "Ответы уже открыты, изменить свой нельзя.");

        var now = clock.GetUtcNow();
        if (scope.Role == MemberRole.A) { q.AnswerA = text; q.AnsweredAtA = now; }
        else { q.AnswerB = text; q.AnsweredAtB = now; }

        if (q.AnswerA is not null && q.AnswerB is not null)
        {
            q.RevealedAt = now;
            await nur.AwardAsync(scope.Couple, NurSource.Question, q.Id.ToString("N"), Nur.QuestionAnsweredByBoth, q.Date, ct);
        }

        await activity.RecordAsync(scope.Couple, scope.UserId, q.Date, ct);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(q, scope));
    }

    private static async Task<CoupleTask?> FindTodayTask(Guid id, CoupleScope scope, AiluDb db, TimeProvider clock,
        CancellationToken ct)
    {
        var today = CoupleCalendar.Today(scope.Couple, clock);
        var task = await db.CoupleTasks.Include(t => t.DailyTask)
            .SingleOrDefaultAsync(t => t.Id == id && t.CoupleId == scope.Couple.Id, ct);
        return task?.Date == today ? task : null;
    }

    private static IResult TaskNotFound() =>
        Errors.NotFound("task_not_found", "Это задание уже закрыто. Загляните в задание на сегодня.");

    private static TaskDto ToDto(CoupleTask t, CoupleScope scope)
    {
        var mine = scope.Role == MemberRole.A ? t.AcceptedAtA : t.AcceptedAtB;
        var theirs = scope.Role == MemberRole.A ? t.AcceptedAtB : t.AcceptedAtA;
        var state =
            t.ConfirmedAt is not null ? TaskState.Done :
            t.CompletedAt is not null ? (t.CompletedByUserId == scope.UserId ? TaskState.WaitingForConfirmation : TaskState.ConfirmNeeded) :
            mine is not null && theirs is not null ? TaskState.Accepted :
            mine is not null ? TaskState.WaitingForPartner :
            TaskState.Offered;

        var d = t.DailyTask!;
        return new TaskDto(t.Id, t.Date, d.Slug, d.Category, d.Title, d.Body, d.CostKzt, d.DurationMin, state,
            mine is not null, theirs is not null, t.CompletedAt, t.ConfirmedAt);
    }

    private static QuestionDto ToDto(DailyQuestion q, CoupleScope scope)
    {
        var mine = scope.Role == MemberRole.A ? q.AnswerA : q.AnswerB;
        var theirs = scope.Role == MemberRole.A ? q.AnswerB : q.AnswerA;
        return new QuestionDto(q.Id, q.Date, q.Question!.Text, mine, theirs is not null,
            q.RevealedAt is not null ? theirs : null, q.RevealedAt);
    }
}
