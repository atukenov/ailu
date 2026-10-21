using Ailu.Api.Data;
using Ailu.Api.Domain;
using Ailu.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Ailu.Api.Services;

public enum PickOutcome { Ready, NoPlan, QuotaUsed, Unavailable }

// AI selections and personal tasks for Premium couples (monetization doc: "Идеи для свиданий и подборки
// от AI", "Персональные задания под вашу пару"). The model sees only what CoupleProfile carries.
public sealed class AiService(AiluDb db, IAiWriter writer, TimeProvider clock, ILogger<AiService> log)
{
    // A pending selection older than this was cut off (a crash, a deploy) and no longer holds a place.
    private static readonly TimeSpan PendingHold = TimeSpan.FromMinutes(2);

    // Selections are counted per calendar month of the couple's own days.
    public static DateOnly MonthOf(DateOnly day) => new(day.Year, day.Month, 1);

    public async Task<int> UsedThisMonthAsync(Couple couple, CancellationToken ct)
    {
        var month = MonthOf(CoupleCalendar.Today(couple, clock));
        var holdSince = clock.GetUtcNow() - PendingHold;
        return await db.AiPicks.CountAsync(p => p.CoupleId == couple.Id && p.Month == month
            && (p.Status == AiPickStatus.Ready || (p.Status == AiPickStatus.Pending && p.CreatedAt > holdSince)), ct);
    }

    public async Task<(PickOutcome Outcome, AiPick? Pick)> PickAsync(CoupleScope scope, AiPickKind kind, AiBudget budget, string? note,
        CancellationToken ct)
    {
        var couple = scope.Couple;
        var limit = Plans.AiPicksPerMonth(couple.Plan);
        if (limit == 0) return (PickOutcome.NoPlan, null);

        // The place in the month's limit is taken under the couple row lock, before the model is asked,
        // so two partners asking at once can't go over it. The slow call happens outside the lock.
        var pick = new AiPick
        {
            Id = Guid.NewGuid(), CoupleId = couple.Id, RequestedById = scope.UserId, Kind = kind, Budget = budget,
            Note = note, Status = AiPickStatus.Pending, CreatedAt = clock.GetUtcNow(),
            Month = MonthOf(CoupleCalendar.Today(couple, clock))
        };
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Couples\" WHERE \"Id\" = {couple.Id} FOR UPDATE", ct);
            if (await UsedThisMonthAsync(couple, ct) >= limit) return (PickOutcome.QuotaUsed, null);
            db.AiPicks.Add(pick);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        IReadOnlyList<AiIdea> ideas;
        try
        {
            var avoid = await db.AiPicks
                .Where(p => p.CoupleId == couple.Id && p.Kind == kind && p.Status == AiPickStatus.Ready)
                .OrderByDescending(p => p.CreatedAt).Take(8)
                .SelectMany(p => p.Items.Select(i => i.Title)).ToListAsync(ct);
            ideas = await writer.IdeasAsync(new IdeaRequest(kind, budget, note, await ProfileAsync(couple, ct), avoid), ct);
            if (ideas.Count == 0) throw new AiUnavailableException("No ideas came back.");
        }
        catch (AiUnavailableException e)
        {
            log.LogWarning(e, "AI selection {Pick} failed", pick.Id);
            pick.Status = AiPickStatus.Failed;
            pick.CompletedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(CancellationToken.None);
            return (PickOutcome.Unavailable, null);
        }

        // Added through the set: items with their ids already set would otherwise be taken for existing rows.
        db.AiPickItems.AddRange(ideas.Select((idea, i) => new AiPickItem
        {
            Id = Guid.NewGuid(), PickId = pick.Id, CoupleId = couple.Id, Position = i,
            Title = idea.Title, Body = idea.Body, TitleKk = idea.TitleKk, BodyKk = idea.BodyKk,
            CostKzt = idea.CostKzt, DurationMin = idea.DurationMin
        }));
        pick.Status = AiPickStatus.Ready;
        pick.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (PickOutcome.Ready, pick);
    }

    // A personal task for today, written for this couple and not yet in the database. Null when the
    // model can't be reached: the caller then hands out a library task instead.
    public async Task<AiTask?> PersonalTaskAsync(Couple couple, DateOnly day, CancellationToken ct)
    {
        var categories = await db.TaskLibrary.Select(t => t.Category).Distinct().OrderBy(c => c).ToListAsync(ct);
        var avoid = await db.CoupleTasks.Where(t => t.CoupleId == couple.Id && t.Date >= day.AddDays(-30))
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Ordinal)
            .Select(t => t.DailyTask!.Title).Take(30).ToListAsync(ct);
        try
        {
            return await writer.TaskAsync(new TaskRequest(await ProfileAsync(couple, ct), categories, avoid), ct);
        }
        catch (AiUnavailableException e)
        {
            log.LogWarning(e, "Personal task for couple {Couple} failed; a library task is used", couple.Id);
            return null;
        }
    }

    public async Task<CoupleProfile> ProfileAsync(Couple couple, CancellationToken ct)
    {
        var today = CoupleCalendar.Today(couple, clock);
        var quiz = await db.LoveQuizRounds.Where(r => r.CoupleId == couple.Id && r.CompletedAt != null)
            .OrderByDescending(r => r.CompletedAt).Take(1)
            .SelectMany(r => r.Answers.OrderBy(a => a.UserId).Select(a => a.Picks)).ToListAsync(ct);
        IReadOnlyList<string> Top(int i) => i < quiz.Count ? LoveQuiz.Top(LoveQuiz.Scores(quiz[i])) : [];

        var recent = await db.CoupleTasks
            .Where(t => t.CoupleId == couple.Id && t.ConfirmedAt != null && t.DailyTask!.CoupleId == null)
            .OrderByDescending(t => t.ConfirmedAt).Select(t => t.DailyTask!.Title).Take(10).ToListAsync(ct);
        var goals = await db.Goals.Where(g => g.CoupleId == couple.Id && g.RemovedAt == null && g.CompletedAt == null)
            .Select(g => g.Category).Distinct().ToListAsync(ct);

        return new CoupleProfile(today, Math.Max(1, today.DayNumber - CoupleCalendar.DayOf(couple, couple.StartedAt).DayNumber + 1),
            Top(0), Top(1), recent, goals.Select(g => g.ToString().ToLowerInvariant()).Order().ToList());
    }
}
