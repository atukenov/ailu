using System.Text.Json;
using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Features.Timeline;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

// Which years a couple can have a story of, and what goes into one (see YearStoryPlan for the choice).
public sealed class YearStoryService(AiluDb db, TimeProvider clock)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // A year's story opens on 1 December, ready for the end-of-year peak the roadmap warns about. Past years
    // stay open.
    public static DateOnly OpensOn(int year) => new(year, 12, 1);

    public static bool IsOpen(int year, DateOnly today) => today >= OpensOn(year);

    // Every year the couple has been together, oldest first.
    public static List<int> Years(Couple couple, DateOnly today)
    {
        var first = CoupleCalendar.DayOf(couple, couple.StartedAt).Year;
        return Enumerable.Range(first, Math.Max(1, today.Year - first + 1)).ToList();
    }

    // In December, this year; before that, last year's, if the couple was together then.
    public static int DefaultYear(Couple couple, DateOnly today)
    {
        var years = Years(couple, today);
        return today.Month == 12 || !years.Contains(today.Year - 1) ? today.Year : today.Year - 1;
    }

    public sealed record Plan(List<StorySlide> Slides, int Memories);

    public async Task<Plan> BuildAsync(Couple couple, int year, string? locale, CancellationToken ct)
    {
        var today = CoupleCalendar.Today(couple, clock);
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31) < today ? new DateOnly(year, 12, 31) : today;

        var memories = await db.Memories.AsNoTracking()
            .Where(m => m.CoupleId == couple.Id && m.DeletedAt == null && m.HappenedOn >= from && m.HappenedOn <= to && m.Photos.Any())
            .Select(m => new StoryMemory(m.Id, m.HappenedOn, m.Title, m.Place,
                m.Photos.OrderBy(p => p.Position).Select(p => p.Id).First(), m.Photos.Count, m.Reactions.Count, m.Favorite,
                m.Text != null && m.Text != ""))
            .ToListAsync(ct);

        var dates = new List<StoryDate>();
        var startDay = CoupleCalendar.DayOf(couple, couple.StartedAt);
        bool InYear(DateOnly d) => d >= from && d <= to;
        if (InYear(startDay)) dates.Add(new(StorySlideKind.Start, startDay, "start"));
        foreach (var n in TimelineEndpoints.DayMilestones)
            if (InYear(startDay.AddDays(n - 1))) dates.Add(new(StorySlideKind.DaysTogether, startDay.AddDays(n - 1), $"days:{n}", Days: n));
        for (var y = 1; startDay.AddYears(y) <= to; y++)
            if (InYear(startDay.AddYears(y))) dates.Add(new(StorySlideKind.Anniversary, startDay.AddYears(y), $"years:{y}", Years: y));

        // Goals and achievements are dated by when the couple finished or earned them. Instants are fetched
        // with a day's margin on each side and placed on the couple's own calendar here.
        var after = new DateTimeOffset(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var before = new DateTimeOffset(to.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var goals = await db.Goals.AsNoTracking()
            .Where(g => g.CoupleId == couple.Id && g.RemovedAt == null && g.CompletedAt > after && g.CompletedAt < before)
            .Select(g => new { g.Id, g.Title, CompletedAt = g.CompletedAt!.Value }).ToListAsync(ct);
        dates.AddRange(goals.Select(g => new StoryDate(StorySlideKind.GoalDone, CoupleCalendar.DayOf(couple, g.CompletedAt), $"goal:{g.Id:N}",
            Title: g.Title)).Where(d => InYear(d.Date)));
        var achievements = await db.CoupleAchievements.AsNoTracking().Include(x => x.Achievement)
            .Where(x => x.CoupleId == couple.Id && x.EarnedAt > after && x.EarnedAt < before).ToListAsync(ct);
        dates.AddRange(achievements.Select(x => new StoryDate(StorySlideKind.Achievement, CoupleCalendar.DayOf(couple, x.EarnedAt!.Value),
            $"achievement:{x.Achievement!.Slug}", Title: x.Achievement.TitleIn(locale), Icon: x.Achievement.Icon, Slug: x.Achievement.Slug))
            .Where(d => InYear(d.Date)));

        var memoriesInYear = await db.Memories.CountAsync(m => m.CoupleId == couple.Id && m.DeletedAt == null
            && m.HappenedOn >= from && m.HappenedOn <= to, ct);
        var tasks = await db.CoupleTasks.CountAsync(t => t.CoupleId == couple.Id && t.ConfirmedAt != null && t.Date >= from && t.Date <= to, ct);
        var awards = await db.NurLedger.Where(e => e.CoupleId == couple.Id && e.Amount > 0 && e.CreatedAt > after && e.CreatedAt < before)
            .Select(e => new { e.Amount, e.CreatedAt }).ToListAsync(ct);
        var stats = new StoryStats(
            Math.Max(to.DayNumber - startDay.DayNumber + 1, 0),
            memoriesInYear,
            tasks,
            awards.Where(a => InYear(CoupleCalendar.DayOf(couple, a.CreatedAt))).Sum(a => a.Amount));

        return new Plan(YearStoryPlan.Build(year, memories, dates, stats), memories.Count);
    }

    public static string Serialize(IReadOnlyList<StorySlide> slides) => JsonSerializer.Serialize(slides, Json);

    public static List<StorySlide> Deserialize(string storyboard) => JsonSerializer.Deserialize<List<StorySlide>>(storyboard, Json) ?? [];
}
