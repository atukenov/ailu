using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Features.Daily;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Features.Widget;

// The home-screen widget's whole picture in one small call, so the app can refresh it on every
// foreground without fetching today, the couple and the phase separately. The widget only shows
// state, so it carries no answers and no task text beyond the title.
public sealed record WidgetTaskDto(string Title, string State, int Done, int PerDay);
public sealed record WidgetQuestionDto(bool Answered, bool PartnerAnswered, bool Revealed);
public sealed record WidgetDto(
    DateOnly Date,
    // When the couple's next day starts. The widget switches to "a new task is waiting" then,
    // even if the app has not been opened.
    DateTimeOffset NextDayAt,
    int DaysTogether,
    string? PartnerName,
    string Plan,
    int Streak,
    // The streak as it will read tomorrow if nothing more happens today: it drops to 0 then
    // unless today counted.
    int StreakTomorrow,
    int NurBalance,
    int NurEarned,
    int PhaseIndex,
    double PhaseFill,
    int? NextPhaseAt,
    WidgetTaskDto Task,
    WidgetQuestionDto Question);

public static class WidgetEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapGroup("/v1/couple").WithTags("Widget").RequireAuthorization().MapGet("/widget", Get);
    }

    private static async Task<IResult> Get(HttpContext http, AiluDb db, DailyContentService content, TimeProvider clock,
        CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var c = scope.Couple;

        var today = CoupleCalendar.Today(c, clock);
        var (task, question) = await content.EnsureAsync(c, today, ct);
        var tasks = await db.CoupleTasks.Include(t => t.DailyTask)
            .Where(t => t.CoupleId == c.Id && t.Date == today)
            .OrderBy(t => t.Ordinal).ToListAsync(ct);

        // The task to show is the newest one opened today: once it is done the widget says so.
        var current = DailyEndpoints.ToDto(tasks.LastOrDefault() ?? task, scope);
        var done = tasks.Count(t => t.ConfirmedAt is not null);
        var q = DailyEndpoints.ToDto(question, scope);

        var streak = StreakService.StateOf(c);
        var phase = Phases.For(c.NurEarned);
        var days = today.DayNumber - CoupleCalendar.DayOf(c, c.StartedAt).DayNumber + 1;

        return Results.Ok(new WidgetDto(
            today,
            CoupleCalendar.StartOf(c, today.AddDays(1)),
            Math.Max(days, 1),
            scope.Partner?.User?.Name,
            c.Plan.ToString().ToLowerInvariant(),
            Domain.Streak.Current(streak, today),
            Domain.Streak.Current(streak, today.AddDays(1)),
            c.NurBalance,
            c.NurEarned,
            phase.Phase.Index,
            Math.Round(phase.Fill, 4),
            phase.NextAt,
            new WidgetTaskDto(current.Title, current.State, done, Plans.TasksPerDay(c.Plan)),
            new WidgetQuestionDto(q.MyAnswer is not null, q.PartnerAnswered, q.RevealedAt is not null)));
    }
}
