using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Ailu.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Features.Timeline;

public static class TimelineKind
{
    public const string Start = "start";
    public const string DaysTogether = "days_together";
    public const string Anniversary = "anniversary";
    public const string Phase = "phase";
    public const string FirstTask = "first_task";
    public const string FirstAnswers = "first_answers";
    public const string Memory = "memory";
    public const string GoalDone = "goal_done";
    public const string Achievement = "achievement";
}

// One moment in the couple's history. The app writes the words from Kind and the fields that kind uses,
// so the history reads in the person's own language; titles are the couple's own text, except an
// achievement's, which is the library's in the caller's language.
public sealed record TimelineEvent(string Id, string Kind, DateOnly Date, DateTimeOffset At,
    int? Days = null, int? Years = null, int? Cycle = null, int? Phase = null,
    Guid? MemoryId = null, Guid? GoalId = null, string? Title = null, string? Place = null,
    string? Thumb = null, int? Photos = null, string? Slug = null, string? Icon = null);

public sealed record TimelineSummary(int DaysTogether, int Memories, int GoalsDone, int NurEarned);

// Newest first. On the free plan `Hidden` counts the older moments kept for paid couples, and `FreeDays`
// says how far back a free couple sees; both are null on a paid plan. The start of the couple always shows.
public sealed record TimelinePage(TimelineSummary Summary, IReadOnlyList<TimelineEvent> Items, string? NextCursor,
    int? Hidden, int? FreeDays);

// The relationship history (roadmap phase 3): a feed of everything that happened to the couple. It is
// built from the records the couple already has, so it needs no table of its own and can't drift from them.
public static class TimelineEndpoints
{
    // "Полная история отношений" is a Plus feature (monetization doc). Free couples see their last
    // 90 days and the day they began; nothing they made is locked, memories stay open in their own tab.
    public const int FreeDays = 90;
    private const int PageSize = 30;

    // Days-together milestones before the yearly anniversaries take over.
    private static readonly int[] DayMilestones = [100, 500, 1000];

    public static void Map(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/v1/couple").WithTags("Timeline").RequireAuthorization();
        g.MapGet("/timeline", Get);
    }

    private static async Task<IResult> Get(string? cursor, HttpContext http, AiluDb db, PhotoUrls urls, TimeProvider clock,
        CancellationToken ct)
    {
        var scope = await CurrentUser.CoupleAsync(http.User, db, ct);
        if (scope is null) return Errors.NoCouple();
        var couple = scope.Couple;
        var today = CoupleCalendar.Today(couple, clock);

        (DateOnly Date, DateTimeOffset At, string Id)? after = null;
        if (cursor is not null)
        {
            if (Cursor.Parse(cursor) is not { } parsed) return Errors.Validation("cursor", "Неверный курсор.");
            after = parsed;
        }

        var events = await BuildAsync(couple, today, scope.Me.User?.Locale, db, urls, ct);
        var ordered = events.OrderByDescending(e => e.Date).ThenByDescending(e => e.At)
            .ThenByDescending(e => e.Id, StringComparer.Ordinal).ToList();

        int? hidden = null;
        if (couple.Plan == CouplePlan.Free)
        {
            var from = today.AddDays(-(FreeDays - 1));
            bool Visible(TimelineEvent e) => e.Kind == TimelineKind.Start || e.Date >= from;
            hidden = ordered.Count(e => !Visible(e));
            ordered = ordered.Where(Visible).ToList();
        }

        if (after is var (day, at, id))
            ordered = ordered.SkipWhile(e => e.Date > day || (e.Date == day &&
                (e.At > at || (e.At == at && string.CompareOrdinal(e.Id, id) >= 0)))).ToList();

        var page = ordered.Take(PageSize).ToList();
        var next = ordered.Count > PageSize ? Cursor.Of(page[^1]) : null;

        var summary = new TimelineSummary(
            Math.Max(today.DayNumber - CoupleCalendar.DayOf(couple, couple.StartedAt).DayNumber + 1, 1),
            events.Count(e => e.Kind == TimelineKind.Memory),
            events.Count(e => e.Kind == TimelineKind.GoalDone),
            couple.NurEarned);
        return Results.Ok(new TimelinePage(summary, page, next, hidden, hidden is null ? null : FreeDays));
    }

    private static async Task<List<TimelineEvent>> BuildAsync(Couple couple, DateOnly today, string? locale, AiluDb db,
        PhotoUrls urls, CancellationToken ct)
    {
        var events = new List<TimelineEvent>();
        var startDay = CoupleCalendar.DayOf(couple, couple.StartedAt);
        events.Add(new(TimelineKind.Start, TimelineKind.Start, startDay, couple.StartedAt));

        // Milestones are dated at the start of their day, so anything else that day sits above them.
        DateTimeOffset Midnight(DateOnly d) => d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        foreach (var n in DayMilestones)
        {
            var d = startDay.AddDays(n - 1);
            if (d <= today) events.Add(new($"days:{n}", TimelineKind.DaysTogether, d, Midnight(d), Days: n));
        }
        for (var y = 1; startDay.AddYears(y) <= today; y++)
        {
            var d = startDay.AddYears(y);
            events.Add(new($"years:{y}", TimelineKind.Anniversary, d, Midnight(d), Years: y));
        }

        // Phases follow earned нұр, so the moment each one was reached is where the running total crossed it.
        var awards = await db.NurLedger.Where(e => e.CoupleId == couple.Id && e.Amount > 0)
            .OrderBy(e => e.Id).Select(e => new { e.Amount, e.CreatedAt }).ToListAsync(ct);
        var earned = 0;
        foreach (var award in awards)
        {
            var before = earned;
            earned += award.Amount;
            for (var step = Phases.For(before); step.NextAt is int at && at <= earned; step = Phases.For(at))
            {
                var reached = Phases.For(at);
                events.Add(new($"phase:{reached.Cycle}:{reached.Phase.Index}", TimelineKind.Phase,
                    CoupleCalendar.DayOf(couple, award.CreatedAt), award.CreatedAt, Cycle: reached.Cycle, Phase: reached.Phase.Index));
            }
        }

        var firstTask = await db.CoupleTasks.Where(t => t.CoupleId == couple.Id && t.ConfirmedAt != null)
            .MinAsync(t => t.ConfirmedAt, ct);
        if (firstTask is { } taskAt)
            events.Add(new(TimelineKind.FirstTask, TimelineKind.FirstTask, CoupleCalendar.DayOf(couple, taskAt), taskAt));

        var firstAnswers = await db.DailyQuestions.Where(q => q.CoupleId == couple.Id && q.RevealedAt != null)
            .MinAsync(q => q.RevealedAt, ct);
        if (firstAnswers is { } answersAt)
            events.Add(new(TimelineKind.FirstAnswers, TimelineKind.FirstAnswers, CoupleCalendar.DayOf(couple, answersAt), answersAt));

        var memories = await db.Memories.Where(m => m.CoupleId == couple.Id && m.DeletedAt == null)
            .Select(m => new
            {
                m.Id, m.Title, m.Place, m.HappenedOn, m.CreatedAt,
                Photos = m.Photos.Count,
                Cover = m.Photos.OrderBy(p => p.Position).Select(p => (Guid?)p.Id).FirstOrDefault()
            })
            .ToListAsync(ct);
        events.AddRange(memories.Select(m => new TimelineEvent($"memory:{m.Id:N}", TimelineKind.Memory, m.HappenedOn,
            m.CreatedAt, MemoryId: m.Id, Title: m.Title, Place: m.Place,
            Thumb: m.Cover is { } cover ? urls.For(cover, PhotoProcessor.Thumb) : null, Photos: m.Photos)));

        var goals = await db.Goals.Where(g => g.CoupleId == couple.Id && g.RemovedAt == null && g.CompletedAt != null)
            .Select(g => new { g.Id, g.Title, CompletedAt = g.CompletedAt!.Value }).ToListAsync(ct);
        events.AddRange(goals.Select(g => new TimelineEvent($"goal:{g.Id:N}", TimelineKind.GoalDone,
            CoupleCalendar.DayOf(couple, g.CompletedAt), g.CompletedAt, GoalId: g.Id, Title: g.Title)));

        // An achievement sits on the day the couple earned it. The day it happened (on its card) can be long
        // before they started using Ailu, and the history begins with the couple.
        var achievements = await db.CoupleAchievements.Include(x => x.Achievement)
            .Where(x => x.CoupleId == couple.Id && x.EarnedAt != null).ToListAsync(ct);
        events.AddRange(achievements.Select(x => new TimelineEvent($"achievement:{x.Achievement!.Slug}",
            TimelineKind.Achievement, CoupleCalendar.DayOf(couple, x.EarnedAt!.Value), x.EarnedAt!.Value, MemoryId: x.MemoryId,
            Title: x.Achievement.TitleIn(locale), Slug: x.Achievement.Slug, Icon: x.Achievement.Icon)));

        return events;
    }

    private static class Cursor
    {
        public static string Of(TimelineEvent e) => $"{e.Date:yyyy-MM-dd}_{e.At.UtcTicks}_{e.Id}";

        public static (DateOnly, DateTimeOffset, string)? Parse(string s)
        {
            var parts = s.Split('_', 3);
            return parts.Length == 3 && DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", out var day) &&
                   long.TryParse(parts[1], out var ticks) && ticks is >= 0 and <= 3155378975999999999 &&
                   parts[2].Length is > 0 and <= 64
                ? (day, new DateTimeOffset(ticks, TimeSpan.Zero), parts[2])
                : null;
        }
    }
}
