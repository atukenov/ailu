using Ailu.Api.Data;
using Ailu.Api.Features.Goals;
using Ailu.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ailu.Api.Services;

// At most two pushes a day per person, both in the couple's own time zone:
// - 09:00, the task of the day, to both partners (the day's items are created here if needed);
// - 20:00, only to someone whose partner has already answered the question and who hasn't;
//   on Sundays, anyone who isn't owed that nudge hears about their goals instead (weekly reminder).
// Each push is in the language the person chose in the app (Kazakh or Russian).
// Each push has a window so a late start (a restart, a couple paired at night) never sends a
// morning push in the evening.
public sealed class DailyPushJob(AiluDb db, DailyContentService content, IPushSender push, TimeProvider clock,
    ILogger<DailyPushJob> log)
{
    public static readonly TimeOnly MorningFrom = new(9, 0), MorningUntil = new(12, 0);
    public static readonly TimeOnly EveningFrom = new(20, 0), EveningUntil = new(23, 0);

    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        // Fine for the closed test; at scale this becomes a query for couples whose local time is in a window.
        var couples = await db.Couples
            .Where(c => c.Status == CoupleStatus.Active)
            .Include(c => c.Members.Where(m => m.LeftAt == null)).ThenInclude(m => m.User)
            .ToListAsync(ct);

        foreach (var couple in couples)
        {
            if (couple.Members.Count != 2) continue;
            var day = CoupleCalendar.DayOf(couple, now);
            var time = CoupleCalendar.TimeOf(couple, now);

            if (time >= MorningFrom && time < MorningUntil) await MorningAsync(couple, day, ct);
            else if (time >= EveningFrom && time < EveningUntil) await EveningAsync(couple, day, ct);
        }
    }

    private async Task MorningAsync(Couple couple, DateOnly day, CancellationToken ct)
    {
        var (task, _) = await content.EnsureAsync(couple, day, ct);
        foreach (var member in couple.Members)
        {
            var kk = member.User?.Locale == Locales.Kazakh;
            await SendOnceAsync(member, day, PushKind.MorningTask, kk ? "Бүгінгі тапсырма" : "Задание на сегодня",
                task.DailyTask!.TitleIn(member.User?.Locale), ct);
        }
    }

    private async Task EveningAsync(Couple couple, DateOnly day, CancellationToken ct)
    {
        var q = await db.DailyQuestions.AsNoTracking().SingleOrDefaultAsync(x => x.CoupleId == couple.Id && x.Date == day, ct);
        var goal = day.DayOfWeek == DayOfWeek.Sunday ? await GoalToRemindAsync(couple, ct) : null;

        foreach (var member in couple.Members)
        {
            // One evening push at most, whichever comes first.
            if (await db.PushLog.AnyAsync(p => p.UserId == member.UserId && p.Date == day &&
                                               (p.Kind == PushKind.EveningAnswer || p.Kind == PushKind.GoalReminder), ct))
                continue;

            var kk = member.User?.Locale == Locales.Kazakh;
            var mine = member.Role == MemberRole.A ? q?.AnswerA : q?.AnswerB;
            var theirs = member.Role == MemberRole.A ? q?.AnswerB : q?.AnswerA;
            if (q is not null && q.RevealedAt is null && mine is null && theirs is not null)
            {
                var partner = couple.Members.First(m => m.UserId != member.UserId).User?.Name ?? (kk ? "Серіктесіңіз" : "Партнёр");
                await SendOnceAsync(member, day, PushKind.EveningAnswer, kk ? "Күн сұрағы" : "Вопрос дня",
                    kk ? $"{partner} жауап берді. Сіз жауап бергенде, жауаптар ашылады."
                       : $"{partner} уже ответил(а). Ответы откроются, когда ответите вы.", ct);
            }
            else if (goal is not null)
            {
                await SendOnceAsync(member, day, PushKind.GoalReminder, kk ? "Ортақ мақсатыңыз" : "Ваша цель",
                    kk ? $"«{goal.Title}» — {GoalEndpoints.Progress(goal)}%. Осы аптада екеуіңіз қандай қадам жасайсыз?"
                       : $"«{goal.Title}» — {GoalEndpoints.Progress(goal)}%. Какой шаг сделаете на этой неделе?", ct);
            }
        }
    }

    // The oldest shared goal still in progress that asked for a weekly reminder.
    private Task<Goal?> GoalToRemindAsync(Couple couple, CancellationToken ct) =>
        db.Goals.AsNoTracking().Include(g => g.Steps)
            .Where(g => g.CoupleId == couple.Id && g.WeeklyReminder && g.JoinedAt != null && g.CompletedAt == null && g.RemovedAt == null)
            .OrderBy(g => g.CreatedAt)
            .FirstOrDefaultAsync(ct);

    // Records the push before sending it: a failed send loses one push, but a retry can never send two.
    private async Task SendOnceAsync(CoupleMember member, DateOnly day, PushKind kind, string title, string body,
        CancellationToken ct)
    {
        if (await db.PushLog.AnyAsync(p => p.UserId == member.UserId && p.Date == day && p.Kind == kind, ct)) return;

        var entry = new PushLog { UserId = member.UserId, Date = day, Kind = kind, CoupleId = member.CoupleId, SentAt = clock.GetUtcNow() };
        db.PushLog.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(entry).State = EntityState.Detached; // another instance sent it
            return;
        }

        var tokens = await db.Devices.Where(d => d.UserId == member.UserId).Select(d => d.PushToken).ToListAsync(ct);
        if (tokens.Count == 0) return;
        try
        {
            await push.SendAsync(tokens.Select(t => new PushMessage(t, title, body)).ToList(), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogError(e, "Push {Kind} to {User} failed", kind, member.UserId);
        }
    }
}

// Runs the push job once a minute inside the API process. It can move to its own worker service
// later without changes: the job is safe to run from several places at once.
public sealed class DailyPushScheduler(IServiceScopeFactory scopes, TimeProvider clock, ILogger<DailyPushScheduler> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<DailyPushJob>().RunOnceAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogError(e, "Daily push run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
